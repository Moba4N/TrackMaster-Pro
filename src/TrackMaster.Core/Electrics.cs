using System.Globalization;
using System.Text;

namespace TrackMaster.Core;

/// <summary>Ein Stromabschnitt: Gleise, die ohne Trennstelle miteinander verbunden sind.</summary>
public sealed record PowerSection(int Number, IReadOnlyList<int> PieceIds, double LengthMm, IReadOnlyList<Feeder> Feeders)
{
    public IReadOnlyList<string> Circuits => Feeders.Select(f => f.Circuit).Distinct().ToList();
}

/// <summary>Eine Kehrschleife: Gleise, auf denen ein Zug gewendet ankommt und so einen Kurzschluss verursachen würde.</summary>
public sealed record ReverseLoop(IReadOnlyList<int> PieceIds);

public enum IssueLevel { Hinweis, Warnung, Fehler }

/// <summary>Ein Befund aus Plan- oder Elektrikprüfung, mit Ort zum Hinspringen.</summary>
public sealed record PlanIssue(IssueLevel Level, string Category, string Text, IReadOnlyList<int> PieceIds, double X, double Y);

/// <summary>Weiche, DKW oder Signal mit Digitaladresse.</summary>
public sealed record SwitchingItem(string Kind, string Label, int? PieceId, int? SignalId, int? Address, int AddressCount, double X, double Y)
{
    public int? Decoder => Address is { } a && a > 0 ? (a - 1) / 4 + 1 : null;
    public int? Port => Address is { } a && a > 0 ? (a - 1) % 4 + 1 : null;
    public string AddressText => Address is not { } a ? "–" : AddressCount > 1 ? $"{a}–{a + AddressCount - 1}" : a.ToString(CultureInfo.InvariantCulture);
}

public sealed record ElectricReport(IReadOnlyList<PowerSection> Sections, IReadOnlyList<ReverseLoop> ReverseLoops, IReadOnlyList<PlanIssue> Issues, bool ThreeRail);

/// <summary>Stromabschnitte, Kehrschleifen, Einspeisungen und Digitaladressen.</summary>
public static class Electrics
{
    /// <summary>Empfohlener Abstand zwischen Einspeisungen in Metern Gleis (H0); skaliert mit der Spurweite.</summary>
    public const double FeederSpacingH0 = 3.0;

    public static bool IsThreeRail(LayoutPlan plan)
        => plan.Pieces.Any(p => Catalog.GetSystem(p.Def.GaugeId, p.Def.SystemId)?.ThreeRail == true);

    private static bool Isolated(LayoutPlan plan, ConnectorRef c)
        => plan.Isolations.Any(i => (i.PieceId == c.PieceId && i.Index == c.Index) || (c.Link is { } l && i.PieceId == l.PieceId && i.Index == l.Index));

    public static IReadOnlyList<PowerSection> Sections(LayoutPlan plan, Connectivity? connectivity = null)
    {
        var conn = connectivity ?? Connectivity.Analyze(plan);
        var parent = plan.Pieces.ToDictionary(p => p.Id, p => p.Id);
        int Root(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        foreach (var c in conn.All)
            if (c.Link is { } l && !Isolated(plan, c) && parent.ContainsKey(l.PieceId))
                parent[Root(c.PieceId)] = Root(l.PieceId);
        var groups = plan.Pieces.GroupBy(p => Root(p.Id))
            .Select(g => g.ToList())
            .OrderByDescending(g => g.Sum(p => p.Def.TrackLength))
            .ToList();
        var result = new List<PowerSection>();
        for (var i = 0; i < groups.Count; i++)
        {
            var ids = groups[i].Select(p => p.Id).ToList();
            var set = ids.ToHashSet();
            result.Add(new PowerSection(i + 1, ids, groups[i].Sum(p => p.Def.TrackLength), plan.Feeders.Where(f => set.Contains(f.PieceId)).ToList()));
        }
        return result;
    }

    /// <summary>
    /// Sucht Kehrschleifen im Zweileiter-System: Jeder Gleisabschnitt hat eine linke und rechte Schiene. Über eine Verbindung
    /// geht links in links über; schließt sich ein Weg so, dass links auf rechts trifft, ist es eine Kehrschleife.
    /// Trennstellen unterbrechen die Verbindung. Beim Mittelleiter (Märklin) gibt es das Problem nicht.
    /// </summary>
    public static IReadOnlyList<ReverseLoop> ReverseLoops(LayoutPlan plan, Connectivity? connectivity = null)
    {
        if (IsThreeRail(plan)) return [];
        var conn = connectivity ?? Connectivity.Analyze(plan);
        // Knoten = (Gleis, Abschnitt); Kanten mit "umgekehrt"-Kennzeichen
        var edges = new Dictionary<(int, int), List<((int, int) To, bool Flip)>>();
        void Edge((int, int) a, (int, int) b, bool flip)
        {
            if (!edges.TryGetValue(a, out var la)) edges[a] = la = [];
            if (!edges.TryGetValue(b, out var lb)) edges[b] = lb = [];
            la.Add((b, flip));
            lb.Add((a, flip));
        }
        foreach (var p in plan.Pieces)
        {
            var paths = TrackGeometry.LocalPaths(p.Def, p.Flipped).Count;
            for (var l = 0; l < paths; l++)
                if (!edges.ContainsKey((p.Id, l))) edges[(p.Id, l)] = [];
            // Weichen: alle Stränge beginnen an Anschluss 0, links bleibt links
            if (p.Def.Kind == PieceKind.Switch)
                for (var l = 1; l < paths; l++) Edge((p.Id, 0), (p.Id, l), false);
        }
        foreach (var c in conn.All)
        {
            if (c.Link is not { } l || c.PieceId > l.PieceId || (c.PieceId == l.PieceId && c.Index > l.Index) || Isolated(plan, c)) continue;
            var a = LegOf(plan.Find(c.PieceId)!, c.Index);
            var b = LegOf(plan.Find(l.PieceId)!, l.Index);
            // Zeigen beide Anschlüsse auf dasselbe Abschnittsende (Anfang/Anfang oder Ende/Ende), kehrt sich links/rechts um
            Edge((c.PieceId, a.Leg), (l.PieceId, b.Leg), a.AtEnd == b.AtEnd);
        }
        var color = new Dictionary<(int, int), bool>();
        var from = new Dictionary<(int, int), (int, int)?>();
        var loops = new List<ReverseLoop>();
        var seen = new HashSet<string>();
        foreach (var startNode in edges.Keys.OrderBy(k => k.Item1).ThenBy(k => k.Item2))
        {
            if (color.ContainsKey(startNode)) continue;
            color[startNode] = false;
            from[startNode] = null;
            var queue = new Queue<(int, int)>();
            queue.Enqueue(startNode);
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                foreach (var (v, flip) in edges[u])
                {
                    var want = color[u] ^ flip;
                    if (!color.TryGetValue(v, out var have))
                    {
                        color[v] = want;
                        from[v] = u;
                        queue.Enqueue(v);
                    }
                    else if (have != want)
                    {
                        var pieces = Cycle(from, u, v).Select(n => n.Item1).Distinct().OrderBy(x => x).ToList();
                        if (seen.Add(string.Join(',', pieces))) loops.Add(new ReverseLoop(pieces));
                    }
                }
            }
        }
        // Größere Schleifen, die eine kleinere ganz enthalten, sind dieselbe Kehrschleife
        return loops.Where(a => !loops.Any(b => b != a && b.PieceIds.Count < a.PieceIds.Count && b.PieceIds.All(a.PieceIds.Contains))).ToList();
    }

    private static List<(int, int)> Cycle(Dictionary<(int, int), (int, int)?> from, (int, int) u, (int, int) v)
    {
        var pathU = new List<(int, int)>();
        for (var n = (ValueTuple<int, int>?)u; n is { } x; n = from[x]) pathU.Add(x);
        var pathV = new List<(int, int)>();
        for (var n = (ValueTuple<int, int>?)v; n is { } x; n = from[x]) pathV.Add(x);
        var setU = pathU.ToHashSet();
        var lca = pathV.FirstOrDefault(setU.Contains);
        var result = pathU.TakeWhile(n => n != lca).ToList();
        result.Add(lca);
        result.AddRange(pathV.TakeWhile(n => n != lca));
        return result;
    }

    /// <summary>Zu welchem Abschnitt ein Anschluss gehört und ob er an dessen Ende liegt.</summary>
    private static (int Leg, bool AtEnd) LegOf(PlacedPiece p, int index) => TrackGeometry.PathOf(p.Def, index);

    public static ElectricReport Analyze(LayoutPlan plan)
    {
        var conn = Connectivity.Analyze(plan);
        var sections = Sections(plan, conn);
        var loops = ReverseLoops(plan, conn);
        var issues = new List<PlanIssue>();
        var spacing = FeederSpacingH0 * 1000 * plan.Gauge.TrackWidth / 16.5;
        foreach (var s in sections)
        {
            var mid = plan.Find(s.PieceIds[0])!.Middle();
            if (s.Feeders.Count == 0)
                issues.Add(new(IssueLevel.Fehler, Lang.T("Einspeisung"), Lang.F($"Abschnitt {s.Number} ({Fmt.Num(s.LengthMm / 1000, 2)} m, {s.PieceIds.Count} Gleise) hat keine Einspeisung."), s.PieceIds, mid.X, mid.Y));
            else if (s.LengthMm / s.Feeders.Count > spacing * 1.2)
                issues.Add(new(IssueLevel.Hinweis, Lang.T("Einspeisung"), Lang.F($"Abschnitt {s.Number}: {Fmt.Num(s.LengthMm / 1000, 2)} m Gleis mit {s.Feeders.Count} Einspeisung(en) – empfohlen ist etwa alle {Fmt.Num(spacing / 1000, 1)} m eine."), s.PieceIds, mid.X, mid.Y));
            if (s.Circuits.Count > 1)
                issues.Add(new(IssueLevel.Fehler, Lang.T("Stromkreise"), Lang.F($"Abschnitt {s.Number} wird aus mehreren Stromkreisen gespeist ({string.Join(", ", s.Circuits)}). Dazwischen gehört eine Trennstelle."), s.PieceIds, mid.X, mid.Y));
        }
        foreach (var l in loops)
        {
            var mid = plan.Find(l.PieceIds[0])!.Middle();
            issues.Add(new(IssueLevel.Fehler, Lang.T("Kehrschleife"),
                Lang.F($"Kehrschleife über {l.PieceIds.Count} Gleise: an beiden Enden Trennstellen setzen und den Abschnitt über ein Kehrschleifenmodul speisen."), l.PieceIds, mid.X, mid.Y));
        }
        var items = SwitchingItems(plan);
        foreach (var dup in AddressConflicts(items))
            issues.Add(new(IssueLevel.Fehler, Lang.T("Adressen"), Lang.F($"Adresse {dup.Address} ist mehrfach belegt: {string.Join(", ", dup.Items.Select(i => i.Label))}."),
                dup.Items.Where(i => i.PieceId is not null).Select(i => i.PieceId!.Value).ToList(), dup.Items[0].X, dup.Items[0].Y));
        var missing = items.Where(i => i.Address is null).ToList();
        if (missing.Count > 0)
            issues.Add(new(IssueLevel.Hinweis, Lang.T("Adressen"), Lang.F($"{missing.Count} Weichen/Signale haben noch keine Digitaladresse."), missing.Where(i => i.PieceId is not null).Select(i => i.PieceId!.Value).ToList(), missing[0].X, missing[0].Y));
        return new ElectricReport(sections, loops, issues, IsThreeRail(plan));
    }

    public static IReadOnlyList<SwitchingItem> SwitchingItems(LayoutPlan plan)
    {
        var list = new List<SwitchingItem>();
        foreach (var p in plan.Pieces)
        {
            var d = p.Def;
            if (d.Drives > 0)
            {
                var m = p.Middle();
                var kind = d.Kind == PieceKind.Switch ? Lang.T("Weiche") : "DKW";
                list.Add(new SwitchingItem(kind, $"{kind} {d.Number} (Nr. {p.Id})", p.Id, null, p.Address, d.Drives, m.X, m.Y));
            }
        }
        foreach (var s in plan.Signals)
        {
            var (x, y, _) = SignalPose(plan, s);
            list.Add(new SwitchingItem("Signal", string.IsNullOrWhiteSpace(s.Name) ? $"{Signal.TypeName(s.Type)} (Nr. {s.Id})" : $"{Signal.TypeName(s.Type)} {s.Name}", null, s.Id, s.Address, s.AddressCount, x, y));
        }
        return list.OrderBy(i => i.Address ?? int.MaxValue).ThenBy(i => i.Y).ThenBy(i => i.X).ToList();
    }

    public static IReadOnlyList<(int Address, IReadOnlyList<SwitchingItem> Items)> AddressConflicts(IReadOnlyList<SwitchingItem> items)
    {
        var byAddress = new Dictionary<int, List<SwitchingItem>>();
        foreach (var i in items)
            if (i.Address is { } a)
                for (var k = 0; k < i.AddressCount; k++)
                {
                    if (!byAddress.TryGetValue(a + k, out var l)) byAddress[a + k] = l = [];
                    l.Add(i);
                }
        return byAddress.Where(kv => kv.Value.Count > 1).OrderBy(kv => kv.Key).Select(kv => (kv.Key, (IReadOnlyList<SwitchingItem>)kv.Value)).ToList();
    }

    /// <summary>Vergibt fortlaufende Adressen ab <paramref name="first"/>, von links oben nach rechts unten. Signale belegen 1 oder 2 Adressen.</summary>
    public static int AssignAddresses(LayoutPlan plan, int first, bool onlyMissing)
    {
        var items = SwitchingItems(plan).OrderBy(i => Math.Round(i.Y / 100)).ThenBy(i => i.X).ToList();
        var taken = new HashSet<int>();
        if (onlyMissing)
            foreach (var i in items.Where(i => i.Address is not null))
                for (var k = 0; k < i.AddressCount; k++) taken.Add(i.Address!.Value + k);
        var next = Math.Max(1, first);
        var n = 0;
        foreach (var i in items)
        {
            if (onlyMissing && i.Address is not null) continue;
            while (Enumerable.Range(next, i.AddressCount).Any(taken.Contains)) next++;
            if (i.PieceId is { } pid) plan.Find(pid)!.Address = next;
            else if (plan.Signals.FirstOrDefault(s => s.Id == i.SignalId) is { } sig) sig.Address = next;
            for (var k = 0; k < i.AddressCount; k++) taken.Add(next + k);
            next += i.AddressCount;
            n++;
        }
        return n;
    }

    /// <summary>Lage des Signals: kurz vor dem Gleisende, rechts neben dem Gleis, Blick gegen die Fahrtrichtung.</summary>
    public static (double X, double Y, double Heading) SignalPose(LayoutPlan plan, Signal s)
    {
        var p = plan.Find(s.PieceId);
        if (p is null) return (0, 0, 0);
        var conns = p.WorldConnectors();
        var c = conns[Math.Min(s.Index, conns.Count - 1)];
        var g = plan.Gauge.TrackWidth;
        var h = c.HeadingRad;
        double x = c.X - Math.Cos(h) * g * 1.5 - Math.Sin(h) * g * 2.2;
        double y = c.Y - Math.Sin(h) * g * 1.5 + Math.Cos(h) * g * 2.2;
        return (x, y, c.Heading);
    }

    /// <summary>Kabelplan als Text: Abschnitte mit Einspeisungen, Trennstellen, Kehrschleifen und Decoderbelegung.</summary>
    public static string WiringPlan(LayoutPlan plan)
    {
        var r = Analyze(plan);
        var sb = new StringBuilder();
        sb.AppendLine(Lang.F($"Kabelplan – {plan.Name} (Spur {plan.GaugeId}, {(r.ThreeRail ? Lang.T("Mittelleiter") : Lang.T("Zweileiter"))})"));
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();
        sb.AppendLine(Lang.T("Stromabschnitte"));
        foreach (var s in r.Sections)
        {
            sb.AppendLine(Lang.F($"  Abschnitt {s.Number}: {s.PieceIds.Count} Gleise, {Fmt.Num(s.LengthMm / 1000, 2)} m"));
            if (s.Feeders.Count == 0) sb.AppendLine(Lang.T("    keine Einspeisung!"));
            foreach (var f in s.Feeders)
                sb.AppendLine(Lang.F($"    Einspeisung Nr. {f.Id} an Gleis {plan.Find(f.PieceId)?.Def.Number} (Nr. {f.PieceId}) → {f.Circuit}"));
        }
        sb.AppendLine();
        sb.AppendLine($"Trennstellen ({plan.Isolations.Count})");
        foreach (var i in plan.Isolations)
            sb.AppendLine($"  Nr. {i.Id}: Gleis {plan.Find(i.PieceId)?.Def.Number} (Nr. {i.PieceId}), Ende {i.Index}");
        sb.AppendLine();
        sb.AppendLine(Lang.T("Kehrschleifen"));
        if (r.ThreeRail) sb.AppendLine(Lang.T("  Mittelleiter: Kehrschleifen brauchen keine Sonderschaltung."));
        else if (r.ReverseLoops.Count == 0) sb.AppendLine("  keine");
        foreach (var l in r.ReverseLoops)
            sb.AppendLine(Lang.F($"  Gleise Nr. {string.Join(", ", l.PieceIds)} – beidseitig trennen, über Kehrschleifenmodul speisen"));
        sb.AppendLine();
        sb.AppendLine(Lang.T("Decoder"));
        foreach (var g in SwitchingItems(plan).Where(i => i.Decoder is not null).GroupBy(i => i.Decoder!.Value).OrderBy(g => g.Key))
        {
            sb.AppendLine(Lang.F($"  Decoder {g.Key} (Adressen {(g.Key - 1) * 4 + 1}–{g.Key * 4})"));
            foreach (var i in g.OrderBy(i => i.Address))
                sb.AppendLine(Lang.F($"    Ausgang {i.Port}: Adresse {i.AddressText} – {i.Label}"));
        }
        var without = SwitchingItems(plan).Where(i => i.Address is null).ToList();
        if (without.Count > 0)
        {
            sb.AppendLine(Lang.T("  ohne Adresse:"));
            foreach (var i in without) sb.AppendLine($"    {i.Label}");
        }
        if (r.Issues.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Lang.T("Befunde"));
            foreach (var i in r.Issues) sb.AppendLine($"  [{i.Level}] {i.Text}");
        }
        return sb.ToString();
    }
}
