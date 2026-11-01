namespace TrackMaster.Core;

/// <summary>Ein Gleis in einer durchfahrenen Kette: Abschnitte in Fahrtrichtung und Höhe am Ein- und Ausgang.</summary>
public sealed record ChainStep(PlacedPiece Piece, int Entry, int Exit, IReadOnlyList<Leg> Legs, double StartHeight, double EndHeight)
{
    public double Length => Legs.Sum(l => l.TrackLength);
    /// <summary>Steigung in Fahrtrichtung (0,02 = 2 %).</summary>
    public double Grade => Length > 0 ? (EndHeight - StartHeight) / Length : 0;
}

/// <summary>
/// Gewählte Gleise als zusammenhängende Kette in Fahrtrichtung (für Höhenprofil und Parallelgleis).
/// An Weichen folgt die Kette dem Strang, der zum nächsten gewählten Gleis führt.
/// </summary>
public static class TrackChain
{
    /// <summary>Abschnitte eines Fahrwegs von Anschluss <paramref name="from"/> nach <paramref name="to"/> in Weltkoordinaten, sonst null.</summary>
    public static IReadOnlyList<Leg>? Route(PlacedPiece p, int from, int to)
    {
        if (from == to) return null;
        var conns = p.WorldConnectors();
        int Near(Pose q)
        {
            for (var i = 0; i < conns.Count; i++)
                if (Math.Abs(conns[i].X - q.X) + Math.Abs(conns[i].Y - q.Y) < 0.5) return i;
            return -1;
        }
        foreach (var path in p.WorldPaths())
        {
            if (path.Count == 0) continue;
            var s = Near(path[0].Start);
            var e = Near(path[^1].End);
            if (s == from && e == to) return path;
            if (s == to && e == from) return path.Reverse().Select(Reverse).ToList();
        }
        return null;
    }

    /// <summary>Derselbe Abschnitt in Gegenrichtung.</summary>
    public static Leg Reverse(Leg l)
    {
        var e = l.End;
        return l.IsArc
            ? new Leg(e.X, e.Y, e.Heading + 180, true, 0, l.Radius, l.Angle, -l.Turn)
            : new Leg(e.X, e.Y, e.Heading + 180, false, l.Length);
    }

    /// <summary>
    /// Ordnet die Gleise <paramref name="ids"/> zu einer Kette. Beginnt an einem Ende der Kette (bei einem Kreis irgendwo)
    /// und läuft möglichst von links nach rechts. Gleise, die nicht an der Kette hängen, bleiben außen vor.
    /// </summary>
    public static IReadOnlyList<ChainStep> Order(LayoutPlan plan, Connectivity conn, IReadOnlySet<int> ids)
    {
        var pieces = plan.Pieces.Where(p => ids.Contains(p.Id) && !p.Def.IsTable).ToList();
        if (pieces.Count == 0) return [];
        var byId = pieces.ToDictionary(p => p.Id);
        // Verbindungen innerhalb der Auswahl: (eigener Anschluss, Nachbar, dessen Anschluss)
        var links = pieces.ToDictionary(p => p.Id, _ => new List<(int Mine, int Other, int OtherIndex)>());
        foreach (var c in conn.All)
            if (c.Link is { } l && byId.ContainsKey(c.PieceId) && byId.ContainsKey(l.PieceId) && c.PieceId != l.PieceId)
                links[c.PieceId].Add((c.Index, l.PieceId, l.Index));

        var start = pieces.FirstOrDefault(p => links[p.Id].Select(x => x.Other).Distinct().Count() == 1) ?? pieces[0];
        var chain = Walk(start, links, byId);
        // Lieber von links nach rechts (bzw. von oben nach unten) laufen
        if (chain.Count > 0)
        {
            var a = chain[0].Legs[0].Start;
            var b = chain[^1].Legs[^1].End;
            var closed = Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) < 1;
            if (!closed && (b.X < a.X - 1 || (Math.Abs(b.X - a.X) <= 1 && b.Y < a.Y)))
            {
                var last = chain[^1].Piece;
                var reversed = Walk(last, links, byId, chain[^1].Exit);
                if (reversed.Count >= chain.Count) chain = reversed;
            }
        }
        return chain;
    }

    private static List<ChainStep> Walk(PlacedPiece start, Dictionary<int, List<(int Mine, int Other, int OtherIndex)>> links,
        Dictionary<int, PlacedPiece> byId, int? startEntry = null)
    {
        var result = new List<ChainStep>();
        var visited = new HashSet<int>();
        var n = start.WorldConnectors().Count;
        int entry;
        if (startEntry is { } se) entry = se;
        else
        {
            // Eingang: ein Anschluss, der nicht in die Auswahl führt und von dem ein Fahrweg weiterführt
            var linked = links[start.Id].Select(x => x.Mine).ToHashSet();
            var free = Enumerable.Range(0, n).Where(i => !linked.Contains(i)).ToList();
            entry = free.FirstOrDefault(i => linked.Any(o => Route(start, i, o) is not null), free.Count > 0 ? free[0] : 0);
        }
        var piece = start;
        while (piece is not null && visited.Add(piece.Id))
        {
            var count = piece.WorldConnectors().Count;
            var next = links[piece.Id].Where(x => x.Mine != entry && !visited.Contains(x.Other) && Route(piece, entry, x.Mine) is not null).Select(x => ((int, int, int)?)x).FirstOrDefault();
            int exit;
            if (next is { } nx) exit = nx.Item1;
            else
            {
                // Ende der Kette: geradeaus weiter, sonst irgendein Fahrweg (beim Kreis zurück zum Anfang)
                exit = Enumerable.Range(0, count).Where(i => i != entry && Route(piece, entry, i) is not null)
                    .OrderBy(i => links[piece.Id].Any(x => x.Mine == i && x.Other == start.Id) ? 0 : 1).ThenBy(i => i).DefaultIfEmpty(-1).First();
                if (exit < 0) break;
            }
            var legs = Route(piece, entry, exit)!;
            var heights = piece.ConnectorHeights();
            double H(int i) => i < heights.Count ? heights[i] : piece.Height;
            result.Add(new ChainStep(piece, entry, exit, legs, H(entry), H(exit)));
            if (next is not { } go) break;
            piece = byId[go.Item2];
            entry = go.Item3;
        }
        return result;
    }
}

/// <summary>Höhenverlauf entlang einer Gleiskette.</summary>
public sealed record ProfilePoint(double Distance, double Height, int PieceId);

public sealed class HeightProfile
{
    public IReadOnlyList<ChainStep> Steps { get; }
    /// <summary>Punkte an jedem Gleisübergang (Anfang und Ende jedes Gleises).</summary>
    public IReadOnlyList<ProfilePoint> Points { get; }
    public double Length { get; }
    public double MinHeight { get; }
    public double MaxHeight { get; }
    /// <summary>Größte Steigung bzw. größtes Gefälle in Prozent (Betrag).</summary>
    public double MaxGradePercent { get; }

    private HeightProfile(IReadOnlyList<ChainStep> steps)
    {
        Steps = steps;
        var pts = new List<ProfilePoint>();
        double d = 0;
        foreach (var s in steps)
        {
            if (pts.Count == 0) pts.Add(new ProfilePoint(0, s.StartHeight, s.Piece.Id));
            d += s.Length;
            pts.Add(new ProfilePoint(d, s.EndHeight, s.Piece.Id));
        }
        Points = pts;
        Length = d;
        MinHeight = pts.Count > 0 ? pts.Min(p => p.Height) : 0;
        MaxHeight = pts.Count > 0 ? pts.Max(p => p.Height) : 0;
        MaxGradePercent = steps.Count > 0 ? steps.Max(s => Math.Abs(s.Grade)) * 100 : 0;
    }

    /// <summary>Höhenprofil der gewählten Gleise; null, wenn keine zusammenhängende Kette gewählt ist.</summary>
    public static HeightProfile? Build(LayoutPlan plan, IReadOnlySet<int> ids, Connectivity? conn = null)
    {
        var steps = TrackChain.Order(plan, conn ?? Connectivity.Analyze(plan), ids);
        return steps.Count == 0 ? null : new HeightProfile(steps);
    }

    public string Summary()
        => Lang.F($"{Steps.Count} Gleise, {Fmt.Num(Length / 1000, 2)} m, Höhe {Fmt.Num(MinHeight, 0)} bis {Fmt.Num(MaxHeight, 0)} mm, größte Steigung {Fmt.Num(MaxGradePercent, 1)} %");

    /// <summary>Linie, Raster und Beschriftungen für ein Diagramm der Größe <paramref name="width"/> × <paramref name="height"/> (Pixel, y nach unten).</summary>
    public ProfileChart Chart(double width, double height)
    {
        const double left = 48, right = 12, top = 14, bottom = 28;
        var w = Math.Max(10, width - left - right);
        var h = Math.Max(10, height - top - bottom);
        var lo = Math.Floor(Math.Min(0, MinHeight) / 10) * 10;
        var hi = Math.Ceiling(Math.Max(MaxHeight, lo + 20) / 10) * 10;
        double X(double d) => left + (Length > 0 ? d / Length : 0) * w;
        double Y(double z) => top + (1 - (z - lo) / (hi - lo)) * h;
        var line = Points.Select(p => (X(p.Distance), Y(p.Height))).ToList();
        var yTicks = Ticks(lo, hi, 5).Select(v => (Y(v), Lang.F($"{Fmt.Num(v, 0)}"))).ToList();
        var xTicks = Ticks(0, Length / 1000, 6).Select(v => (X(v * 1000), Lang.F($"{Fmt.Num(v, v < 10 && Length < 10000 ? 1 : 0)} m"))).ToList();
        var grades = new List<(double X, double Y, string Text, bool Steep)>();
        double d0 = 0;
        foreach (var s in Steps)
        {
            var pct = s.Grade * 100;
            if (Math.Abs(pct) >= 0.05 && s.Length / Math.Max(Length, 1) * w > 34)
                grades.Add((X(d0 + s.Length / 2), Y((s.StartHeight + s.EndHeight) / 2) - 8, $"{Fmt.Num(pct, 1)} %", Math.Abs(pct) > PlanCheck.WarnGradePercent + 1e-6));
            d0 += s.Length;
        }
        return new ProfileChart(left, top, left + w, top + h, line, yTicks, xTicks, grades);
    }

    private static IEnumerable<double> Ticks(double lo, double hi, int count)
    {
        var span = Math.Max(hi - lo, 1e-9);
        var raw = span / count;
        var mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var step = new[] { 1, 2, 2.5, 5, 10 }.Select(f => f * mag).First(s => s >= raw);
        for (var v = Math.Ceiling(lo / step) * step; v <= hi + step * 1e-6; v += step) yield return Math.Round(v, 6);
    }
}

/// <summary>Diagramm des Höhenprofils in Pixeln: Zeichenbereich, Linie, Achsenmarken und Steigungen je Gleis.</summary>
public sealed record ProfileChart(double Left, double Top, double Right, double Bottom,
    IReadOnlyList<(double X, double Y)> Line,
    IReadOnlyList<(double Y, string Text)> HeightTicks,
    IReadOnlyList<(double X, string Text)> DistanceTicks,
    IReadOnlyList<(double X, double Y, string Text, bool Steep)> Grades);

/// <summary>Parallelgleis: Flexgleis im festen Abstand neben einer Gleiskette.</summary>
public static class ParallelTrack
{
    /// <summary>
    /// Baut Flexgleise im Abstand <paramref name="spacing"/> links (<paramref name="left"/>) bzw. rechts der Kette, in Fahrtrichtung gesehen.
    /// Höhe und Steigung folgen der Kette. Liefert die neuen Gleise (noch nicht im Plan) oder eine Fehlermeldung.
    /// </summary>
    public static (IReadOnlyList<PlacedPiece> Pieces, string? Error) Build(LayoutPlan plan, IReadOnlyList<ChainStep> chain, double spacing, bool left, string flexSystemId)
    {
        if (chain.Count == 0) return ([], Lang.T("Bitte zuerst zusammenhängende Gleise wählen."));
        var offset = left ? -spacing : spacing;
        // Abschnitte gleicher Steigung zu einem Flexgleis zusammenfassen
        var groups = new List<List<ChainStep>>();
        foreach (var s in chain)
        {
            var g = groups.Count > 0 ? groups[^1] : null;
            if (g is not null && Math.Abs(g[^1].Grade - s.Grade) < 1e-6 && g.Sum(x => x.Legs.Count) + s.Legs.Count <= 16 && g.Sum(x => x.Length) + s.Length < FlexTrack.MaxLength * 0.95)
                g.Add(s);
            else groups.Add([s]);
        }
        var result = new List<PlacedPiece>();
        foreach (var g in groups)
        {
            var legs = g.SelectMany(s => s.Legs).ToList();
            var segments = new List<Segment>();
            foreach (var l in legs)
            {
                if (!l.IsArc) { segments.Add(Segment.Line(l.Length)); continue; }
                var r = l.Radius - l.Turn * offset;
                if (r < FlexTrack.MinRadius) return ([], Lang.F($"Auf dieser Seite würde der Radius zu klein ({Fmt.Num(r, 0)} mm) – andere Seite oder kleineren Abstand wählen."));
                segments.Add(new Segment(0, r, l.Angle, l.Turn));
            }
            // Gerade Stücke zusammenfassen
            var merged = new List<Segment>();
            foreach (var sg in segments)
                if (sg.Turn == 0 && merged.Count > 0 && merged[^1].Turn == 0) merged[^1] = Segment.Line(merged[^1].Length + sg.Length);
                else merged.Add(sg);
            var (x0, y0, _, _, _) = legs[0].Offset(offset);
            var (key, flipped) = FlexTrack.Key(plan.GaugeId, flexSystemId, merged);
            if (!FlexTrack.IsValid(merged) || Catalog.Find(key) is not { } def) return ([], Lang.T("Das Parallelgleis lässt sich hier nicht als Flexgleis anlegen."));
            var first = g[0];
            var piece = new PlacedPiece
            {
                Key = key, Flipped = flipped, Height = first.StartHeight, Level = first.Piece.Level, Tunnel = first.Piece.Tunnel,
            };
            PlanEditing.PlaceAt(piece, 0, x0, y0, legs[0].Heading);
            piece.Grade = def.TrackLength > 0 ? (g[^1].EndHeight - first.StartHeight) / def.TrackLength : 0;
            result.Add(piece);
        }
        return (result, null);
    }
}

/// <summary>Gleisabschnitt ohne Weichen: Nutzlänge zwischen Weichen, Kreuzungen und Gleisenden.</summary>
public sealed record TrackRun(IReadOnlyList<int> PieceIds, double Length, bool EndsAtBuffer, bool Closed);

public static class UsableLength
{
    private static bool Plain(PlacedPiece p) => p.Def.Kind is PieceKind.Straight or PieceKind.Curve or PieceKind.Buffer && !p.Def.IsTable;

    /// <summary>Alle Abschnitte aus Geraden, Bögen und Prellböcken, die ohne Weiche zusammenhängen.</summary>
    public static IReadOnlyList<TrackRun> Runs(LayoutPlan plan, Connectivity? conn = null)
    {
        conn ??= Connectivity.Analyze(plan);
        var plain = plan.Pieces.Where(Plain).ToDictionary(p => p.Id);
        var adj = plain.Keys.ToDictionary(id => id, _ => new List<int>());
        foreach (var c in conn.All)
            if (c.Link is { } l && plain.ContainsKey(c.PieceId) && plain.ContainsKey(l.PieceId) && c.PieceId != l.PieceId)
                adj[c.PieceId].Add(l.PieceId);
        var seen = new HashSet<int>();
        var runs = new List<TrackRun>();
        foreach (var id in plain.Keys)
        {
            if (!seen.Add(id)) continue;
            var ids = new List<int>();
            var stack = new Stack<int>([id]);
            while (stack.Count > 0)
            {
                var x = stack.Pop();
                ids.Add(x);
                foreach (var y in adj[x]) if (seen.Add(y)) stack.Push(y);
            }
            var pieces = ids.Select(i => plain[i]).ToList();
            var length = pieces.Sum(p => p.Def.TrackLength);
            // Geschlossen: jedes Gleis hat an allen Enden einen Nachbarn im Abschnitt
            var closed = pieces.All(p => conn.All.Where(c => c.PieceId == p.Id).All(c => c.Link is { } l && plain.ContainsKey(l.PieceId)));
            runs.Add(new TrackRun(ids, length, pieces.Any(p => p.Def.Kind == PieceKind.Buffer), closed));
        }
        return runs;
    }

    /// <summary>Abschnitt, zu dem das Gleis gehört (null bei Weichen, Kreuzungen und Drehscheiben).</summary>
    public static TrackRun? For(LayoutPlan plan, int pieceId, Connectivity? conn = null)
        => Runs(plan, conn).FirstOrDefault(r => r.PieceIds.Contains(pieceId));

    /// <summary>Text für die Eigenschaften: „Nutzlänge 1.234 mm (5 Gleise bis zur nächsten Weiche)“.</summary>
    public static string Describe(LayoutPlan plan, TrackRun run)
    {
        var text = run.Closed
            ? Lang.F($"{Fmt.Num(run.Length, 0)} mm im Kreis ohne Weiche")
            : Lang.F($"{Fmt.Num(run.Length, 0)} mm ({run.PieceIds.Count} Gleis(e) zwischen Weichen bzw. Gleisenden)");
        if (plan.TrainLength > 0 && !run.Closed)
            text += run.Length >= plan.TrainLength ? Lang.T(" – Zug passt") : Lang.F($" – {Fmt.Num(plan.TrainLength - run.Length, 0)} mm zu kurz für den Zug");
        return text;
    }
}

/// <summary>Gleise sperren, gruppieren und ausrichten.</summary>
public static class Arrange
{
    /// <summary>Zusammenhängende Gleisgruppen innerhalb der Auswahl (verbundene Gleise bleiben beim Ausrichten zusammen).</summary>
    public static List<List<PlacedPiece>> Clusters(LayoutPlan plan, Connectivity conn, IReadOnlyCollection<PlacedPiece> pieces)
    {
        var byId = pieces.ToDictionary(p => p.Id);
        var adj = byId.Keys.ToDictionary(id => id, _ => new List<int>());
        foreach (var c in conn.All)
            if (c.Link is { } l && byId.ContainsKey(c.PieceId) && byId.ContainsKey(l.PieceId)) adj[c.PieceId].Add(l.PieceId);
        // Gruppen gehören zusammen, auch wenn sie nicht verbunden sind
        foreach (var g in pieces.Where(p => p.Group > 0).GroupBy(p => p.Group))
        {
            var ids = g.Select(p => p.Id).ToList();
            for (var i = 1; i < ids.Count; i++) { adj[ids[0]].Add(ids[i]); adj[ids[i]].Add(ids[0]); }
        }
        var seen = new HashSet<int>();
        var result = new List<List<PlacedPiece>>();
        foreach (var p in pieces)
        {
            if (!seen.Add(p.Id)) continue;
            var list = new List<PlacedPiece>();
            var stack = new Stack<int>([p.Id]);
            while (stack.Count > 0)
            {
                var x = stack.Pop();
                list.Add(byId[x]);
                foreach (var y in adj[x]) if (seen.Add(y)) stack.Push(y);
            }
            result.Add(list);
        }
        return result;
    }

    public static (double X0, double Y0, double X1, double Y1) Box(IEnumerable<PlacedPiece> pieces)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var p in pieces)
            foreach (var l in p.WorldLegs())
                for (var i = 0; i <= 8; i++)
                {
                    var q = l.At(i / 8.0);
                    x0 = Math.Min(x0, q.X); y0 = Math.Min(y0, q.Y); x1 = Math.Max(x1, q.X); y1 = Math.Max(y1, q.Y);
                }
        return (x0, y0, x1, y1);
    }

    /// <summary>Richtet die Gruppen an der gemeinsamen Kante bzw. Mitte aus. Liefert die Zahl der verschobenen Gruppen.</summary>
    public static int Align(LayoutPlan plan, Connectivity conn, IReadOnlyCollection<PlacedPiece> pieces, AlignMode mode)
    {
        var clusters = Clusters(plan, conn, pieces);
        if (clusters.Count < 2) return 0;
        var boxes = clusters.Select(c => (Pieces: c, Box: Box(c))).ToList();
        double target = mode switch
        {
            AlignMode.Left => boxes.Min(b => b.Box.X0),
            AlignMode.Right => boxes.Max(b => b.Box.X1),
            AlignMode.Top => boxes.Min(b => b.Box.Y0),
            AlignMode.Bottom => boxes.Max(b => b.Box.Y1),
            AlignMode.CenterX => boxes.Average(b => (b.Box.X0 + b.Box.X1) / 2),
            _ => boxes.Average(b => (b.Box.Y0 + b.Box.Y1) / 2),
        };
        var moved = 0;
        foreach (var (list, b) in boxes)
        {
            var (dx, dy) = mode switch
            {
                AlignMode.Left => (target - b.X0, 0.0),
                AlignMode.Right => (target - b.X1, 0.0),
                AlignMode.Top => (0.0, target - b.Y0),
                AlignMode.Bottom => (0.0, target - b.Y1),
                AlignMode.CenterX => (target - (b.X0 + b.X1) / 2, 0.0),
                _ => (0.0, target - (b.Y0 + b.Y1) / 2),
            };
            if (Math.Abs(dx) + Math.Abs(dy) < 0.01) continue;
            foreach (var p in list) { p.X += dx; p.Y += dy; }
            moved++;
        }
        return moved;
    }
}
