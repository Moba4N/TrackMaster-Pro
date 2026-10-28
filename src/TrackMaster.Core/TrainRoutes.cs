using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TrackMaster.Core;

/// <summary>Stellung einer Weiche oder DKW in einer Fahrstraße.</summary>
public sealed record RouteSetting(int PieceId, int Path, string Position, int? Address);

/// <summary>Fahrstraße von einem Signal bis zum nächsten Signal in Fahrtrichtung oder bis zum Prellbock.</summary>
public sealed record TrainRoute(string Name, int StartSignalId, int? EndSignalId, int? EndPieceId, IReadOnlyList<int> PieceIds, IReadOnlyList<RouteSetting> Settings, double LengthMm);

/// <summary>
/// Ermittelt Fahrstraßen aus Gleisen und Signalen: Ein Signal gilt für Fahrten auf sein Gleisende zu; ab dort führt jeder
/// Weg über die Weichen bis zum nächsten Signal derselben Richtung oder bis zum Prellbock. Für jede Weiche auf dem Weg wird die
/// nötige Stellung festgehalten, auch für stumpf befahrene. Dazu ein neutraler Export (CSV, JSON) für Steuerprogramme.
/// </summary>
public static class TrainRoutes
{
    private const int MaxPieces = 400, MaxSteps = 20000;

    /// <summary>Fahrwege eines Gleises als Paare von Anschlüssen (in beiden Richtungen), aus der Geometrie bestimmt.</summary>
    public static IReadOnlyList<(int From, int To, int Path)> Passages(PlacedPiece piece)
    {
        if (piece.Def.IsTable) return [];
        var conns = piece.WorldConnectors();
        var paths = piece.WorldPaths();
        int? Near(double x, double y)
        {
            for (var k = 0; k < conns.Count; k++)
                if (Math.Abs(conns[k].X - x) + Math.Abs(conns[k].Y - y) < 0.5) return k;
            return null;
        }
        var result = new List<(int, int, int)>();
        for (var p = 0; p < paths.Count; p++)
        {
            if (paths[p].Count == 0) continue;
            var a = paths[p][0].Start;
            var b = paths[p][^1].End;
            if (Near(a.X, a.Y) is { } i && Near(b.X, b.Y) is { } j && i != j)
            {
                result.Add((i, j, p));
                result.Add((j, i, p));
            }
        }
        return result;
    }

    /// <summary>Bezeichnung einer Stellung: gerade, abzweigend, links/rechts (Dreiwegweiche) oder Strang n.</summary>
    public static string PositionName(PlacedPiece piece, int path)
    {
        var paths = piece.WorldPaths();
        if (path < 0 || path >= paths.Count) return $"Strang {path + 1}";
        bool Straight(int p) => paths[p].All(l => !l.IsArc);
        if (Straight(path)) return "gerade";
        var straightOthers = Enumerable.Range(0, paths.Count).Count(Straight);
        if (paths.Count >= 3 && straightOthers > 0)
        {
            var turn = paths[path].Where(l => l.IsArc).Sum(l => l.Turn * l.Angle);
            return turn > 0 ? "rechts" : "links";
        }
        return straightOthers > 0 ? "abzweigend" : $"Strang {path + 1}";
    }

    public static string SignalName(Signal s) => string.IsNullOrWhiteSpace(s.Name) ? $"Signal Nr. {s.Id}" : s.Name;

    public static IReadOnlyList<TrainRoute> Find(LayoutPlan plan)
    {
        var conn = Connectivity.Analyze(plan);
        var passages = plan.Pieces.ToDictionary(p => p.Id, Passages);
        var signalsAt = plan.Signals.GroupBy(s => (s.PieceId, s.Index)).ToDictionary(g => g.Key, g => g.First());
        var routes = new List<TrainRoute>();
        foreach (var start in plan.Signals.OrderBy(s => s.Name, StringComparer.CurrentCulture).ThenBy(s => s.Id))
        {
            if (conn.Find(start.PieceId, start.Index)?.Link is not { } first) continue;
            var pieces = new List<int>();
            var settings = new List<RouteSetting>();
            var visited = new HashSet<int>();
            var steps = 0;
            void Walk(int pieceId, int entry, double length)
            {
                // Ohne weitere Signale kann die Zahl der Wege stark wachsen: Suche begrenzen
                if (++steps > MaxSteps || pieces.Count > MaxPieces || plan.Find(pieceId) is not { } piece || !visited.Add(pieceId)) return;
                pieces.Add(pieceId);
                var options = passages[pieceId].Where(x => x.From == entry).ToList();
                var drives = piece.Def.Drives > 0 && passages[pieceId].Select(x => x.Path).Distinct().Count() > 1;
                foreach (var (_, exit, path) in options)
                {
                    if (drives) settings.Add(new RouteSetting(pieceId, path, PositionName(piece, path), piece.Address));
                    var len = length + TrackGeometry.PathLength(piece.Def, path);
                    if (signalsAt.TryGetValue((pieceId, exit), out var end))
                        Add(end.Id, null, len);
                    else if (conn.Find(pieceId, exit)?.Link is { } next)
                        Walk(next.PieceId, next.Index, len);
                    else
                        Add(null, pieceId, len);
                    if (drives) settings.RemoveAt(settings.Count - 1);
                }
                // Prellbock: kein Fahrweg weiter
                if (options.Count == 0 && piece.Def.Kind == PieceKind.Buffer) Add(null, pieceId, length + piece.Def.TrackLength);
                pieces.RemoveAt(pieces.Count - 1);
                visited.Remove(pieceId);
            }
            void Add(int? endSignal, int? endPiece, double length)
            {
                var target = endSignal is { } id ? SignalName(plan.Signals.First(s => s.Id == id))
                    : plan.Find(endPiece!.Value)?.Def.Kind == PieceKind.Buffer ? $"Prellbock Nr. {endPiece}" : $"Gleisende Nr. {endPiece}";
                routes.Add(new TrainRoute($"{SignalName(start)} → {target}", start.Id, endSignal, endSignal is null ? endPiece : null, [.. pieces], [.. settings], length));
            }
            Walk(first.PieceId, first.Index, 0);
        }
        return routes;
    }

    /// <summary>Paare von Fahrstraßen, die sich Gleise teilen und daher nicht gleichzeitig eingestellt werden dürfen.</summary>
    public static IReadOnlyList<(int A, int B)> Conflicts(IReadOnlyList<TrainRoute> routes)
    {
        var sets = routes.Select(r => r.PieceIds.ToHashSet()).ToList();
        var result = new List<(int, int)>();
        for (var i = 0; i < routes.Count; i++)
            for (var j = i + 1; j < routes.Count; j++)
                if (sets[i].Overlaps(sets[j])) result.Add((i, j));
        return result;
    }

    public static string ToText(LayoutPlan plan, IReadOnlyList<TrainRoute> routes)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Fahrstraßen – {plan.Name}");
        sb.AppendLine(new string('=', 60));
        if (plan.Signals.Count == 0) sb.AppendLine("Noch keine Signale gesetzt. Fahrstraßen beginnen und enden an Signalen (Register Elektrik › Signal).");
        else if (routes.Count == 0) sb.AppendLine("Keine Fahrstraßen gefunden: Hinter den Signalen fehlt ein angeschlossenes Gleis.");
        var conflicts = Conflicts(routes);
        for (var i = 0; i < routes.Count; i++)
        {
            var r = routes[i];
            sb.AppendLine();
            sb.AppendLine($"{i + 1}. {r.Name} ({Fmt.Num(r.LengthMm / 1000, 2)} m, {r.PieceIds.Count} Gleise)");
            if (r.Settings.Count == 0) sb.AppendLine("   ohne Weichen");
            foreach (var s in r.Settings)
                sb.AppendLine($"   Weiche Nr. {s.PieceId}{(s.Address is { } a ? $" (Adresse {a})" : "")}: {s.Position}");
            var excl = conflicts.Where(c => c.A == i || c.B == i).Select(c => (c.A == i ? c.B : c.A) + 1).ToList();
            if (excl.Count > 0) sb.AppendLine($"   schließt aus: {string.Join(", ", excl)}");
        }
        return sb.ToString();
    }

    /// <summary>Tabelle für Tabellenkalkulation und Steuerprogramme: eine Zeile je Weichenstellung.</summary>
    public static string ToCsv(IReadOnlyList<TrainRoute> routes)
    {
        var sb = new StringBuilder("Fahrstraße;Name;Länge (mm);Weiche Nr.;Adresse;Stellung\r\n");
        static string Q(string s) => s.Contains(';') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        for (var i = 0; i < routes.Count; i++)
        {
            var r = routes[i];
            var len = Math.Round(r.LengthMm).ToString(CultureInfo.InvariantCulture);
            if (r.Settings.Count == 0) sb.Append($"{i + 1};{Q(r.Name)};{len};;;\r\n");
            foreach (var s in r.Settings)
                sb.Append($"{i + 1};{Q(r.Name)};{len};{s.PieceId};{s.Address?.ToString(CultureInfo.InvariantCulture)};{s.Position}\r\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Neutrales Austauschformat (JSON) mit Weichen, Signalen, Stromabschnitten und Fahrstraßen samt Digitaladressen,
    /// zum Übernehmen in Steuerprogramme über deren Import oder ein kleines Skript.
    /// </summary>
    public static string ToJson(LayoutPlan plan)
    {
        var routes = Find(plan);
        var sections = Electrics.Sections(plan);
        var data = new
        {
            app = "TrackMaster Pro",
            format = "steuerung-1",
            plan = plan.Name,
            gauge = plan.GaugeId,
            switches = plan.Pieces.Where(p => p.Def.Drives > 0).Select(p => new
            {
                id = p.Id, article = p.Def.Number, kind = p.Def.IsDoubleSlip ? "dkw" : "weiche", address = p.Address, drives = p.Def.Drives,
                positions = Passages(p).Select(x => x.Path).Distinct().OrderBy(x => x).Select(x => PositionName(p, x)).ToList(),
            }).ToList(),
            signals = plan.Signals.Select(s => new { id = s.Id, name = SignalName(s), type = Signal.TypeName(s.Type), address = s.Address, addresses = s.AddressCount, piece = s.PieceId }).ToList(),
            sections = sections.Select(s => new { number = s.Number, pieces = s.PieceIds, lengthMm = Math.Round(s.LengthMm), circuits = s.Circuits }).ToList(),
            routes = routes.Select((r, i) => new
            {
                number = i + 1, name = r.Name, from = r.StartSignalId, to = r.EndSignalId, endPiece = r.EndPieceId, lengthMm = Math.Round(r.LengthMm), pieces = r.PieceIds,
                settings = r.Settings.Select(s => new { piece = s.PieceId, address = s.Address, position = s.Position }).ToList(),
            }).ToList(),
        };
        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}
