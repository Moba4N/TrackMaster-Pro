using System.Globalization;
using System.Text;

namespace TrackMaster.Core;

/// <summary>Ein Stromabschnitt: Gleise, die ohne Trennstelle miteinander verbunden sind.</summary>
public sealed record PowerSection(int Number, IReadOnlyList<int> PieceIds, double LengthMm, IReadOnlyList<Feeder> Feeders)
{
    /// <summary>Einspeisungen mit Bahnstrom (ohne die Masseanschlüsse).</summary>
    public IReadOnlyList<Feeder> Supply => Feeders.Where(f => !f.Ground).ToList();
    public IReadOnlyList<string> Circuits => Supply.Select(f => f.Circuit).Distinct().ToList();
}

/// <summary>Eine Kehrschleife: Gleise, auf denen ein Zug gewendet ankommt und so einen Kurzschluss verursachen würde.</summary>
public sealed record ReverseLoop(IReadOnlyList<int> PieceIds);

public enum IssueLevel { Hinweis, Warnung, Fehler }

public static class IssueLevels
{
    /// <summary>Name der Stufe in der Sprache der Oberfläche.</summary>
    public static string Text(this IssueLevel level) => level switch
    {
        IssueLevel.Fehler => Lang.T("Fehler"),
        IssueLevel.Warnung => Lang.T("Warnung"),
        _ => Lang.T("Hinweis"),
    };
}

/// <summary>Ein Befund aus Plan- oder Elektrikprüfung, mit Ort zum Hinspringen.</summary>
public sealed record PlanIssue(IssueLevel Level, string Category, string Text, IReadOnlyList<int> PieceIds, double X, double Y, int? ObjectId = null);

/// <summary>Weiche, DKW oder Signal mit Digitaladresse.</summary>
public sealed record SwitchingItem(string Kind, string Label, int? PieceId, int? SignalId, int? Address, int AddressCount, double X, double Y)
{
    public int? Decoder => Address is { } a && a > 0 ? (a - 1) / 4 + 1 : null;
    public int? Port => Address is { } a && a > 0 ? (a - 1) % 4 + 1 : null;
    public string AddressText => Address is not { } a ? "–" : AddressCount > 1 ? $"{a}–{a + AddressCount - 1}" : a.ToString(CultureInfo.InvariantCulture);
}

public sealed record ElectricReport(IReadOnlyList<PowerSection> Sections, IReadOnlyList<ReverseLoop> ReverseLoops, IReadOnlyList<PlanIssue> Issues, bool ThreeRail, CurrentKind Current = CurrentKind.Gleichstrom);

/// <summary>Stromabschnitte, Kehrschleifen, Einspeisungen und Digitaladressen.</summary>
public static class Electrics
{
    /// <summary>Empfohlener Abstand zwischen Einspeisungen in Metern Gleis (H0); skaliert mit der Spurweite.</summary>
    public const double FeederSpacingH0 = 3.0;

    public static bool IsThreeRail(LayoutPlan plan)
        => plan.Pieces.Any(IsThreeRail);

    public static bool IsThreeRail(PlacedPiece p) => Catalog.GetSystem(p.Def.GaugeId, p.Def.SystemId)?.ThreeRail == true;

    /// <summary>
    /// Stromart, mit der der Plan geprüft wird: die eingestellte, sonst nach dem Gleis (Mittelleiter → Wechselstrom, Zweileiter → Gleichstrom).
    /// </summary>
    public static CurrentKind Current(LayoutPlan plan)
        => plan.Current != CurrentKind.Auto ? plan.Current : IsThreeRail(plan) ? CurrentKind.Wechselstrom : CurrentKind.Gleichstrom;

    public static string CurrentName(CurrentKind k) => k switch
    {
        CurrentKind.Gleichstrom => Lang.T("Gleichstrom"),
        CurrentKind.Wechselstrom => Lang.T("Wechselstrom"),
        _ => Lang.T("Automatisch (nach Gleis)"),
    };

    /// <summary>Kurzbeschreibung der Stromart mit Leitersystem und Aderfarben.</summary>
    public static string CurrentHint(LayoutPlan plan) => Current(plan) == CurrentKind.Wechselstrom
        ? Lang.T("Wechselstrom (Mittelleiter): Fahrstrom über den Mittelleiter (rotes Kabel), Masse über beide Schienen (braunes Kabel). Polung und Kehrschleifen spielen keine Rolle.")
        : Lang.T("Gleichstrom (Zweileiter): je eine Schiene an Plus und Minus (z. B. rotes und blaues Kabel). Bahnstrom (B) an eine Schiene, Masse (0) an die andere – in einem Abschnitt immer an dieselbe Seite; Kehrschleifen brauchen eine Umpolung.");

    /// <summary>
    /// Lage des Gleisanschlusses in der Gleismitte (Länge): bei Wechselstrom auf dem Mittelleiter, bei Gleichstrom
    /// auf einer Schiene – links in Richtung Anschluss 0 → 1, bei getauschter Polung rechts.
    /// </summary>
    public static (double X, double Y) FeederPoint(PlacedPiece p, double gauge, CurrentKind current, bool reversed = false, bool ground = false, double at = 0.5)
    {
        var m = Math.Abs(at - 0.5) < 1e-9 ? p.Middle() : p.WorldLegs()[0].At(Math.Clamp(at, 0, 1));
        var side = current == CurrentKind.Wechselstrom && !ground ? 0 : (reversed ? -0.5 : 0.5) * gauge;
        return (m.X - Math.Sin(m.HeadingRad) * side, m.Y + Math.Cos(m.HeadingRad) * side);
    }

    /// <summary>Lage dieser Einspeisung nach Stromart, Schiene und Lage am Gleis.</summary>
    public static (double X, double Y) FeederPoint(LayoutPlan plan, PlacedPiece p, Feeder? f)
        => FeederPoint(p, plan.Gauge.TrackWidth, Current(plan), f?.Reversed ?? false, f?.Ground ?? false, f?.At ?? 0.5);

    /// <summary>
    /// Trifft ein Punkt ein Gleis (Gleismitte des ersten Fahrwegs, höchstens eine Spurweite daneben)? Liefert das Gleis,
    /// die Lage entlang des Gleises und die Schiene (true = rechts in Richtung Anschluss 0 → 1).
    /// </summary>
    public static (PlacedPiece Piece, double At, bool Right, double Offset)? TrackHit(LayoutPlan plan, double x, double y, IReadOnlyCollection<int>? hidden = null)
    {
        var g = plan.Gauge.TrackWidth;
        (PlacedPiece, double, bool, double)? best = null;
        var bestD = g * 1.1;
        foreach (var p in plan.Pieces)
        {
            if (hidden is not null && hidden.Contains(p.Id)) continue;
            var leg = p.WorldLegs()[0];
            for (var i = 0; i <= 40; i++)
            {
                var t = i / 40.0;
                var q = leg.At(t);
                double dx = x - q.X, dy = y - q.Y;
                var d = Math.Sqrt(dx * dx + dy * dy);
                if (d >= bestD) continue;
                bestD = d;
                // seitlicher Abstand zur Gleismitte: positiv = linke Schiene (wie FeederPoint ohne Tausch)
                var s = -Math.Sin(q.HeadingRad) * dx + Math.Cos(q.HeadingRad) * dy;
                best = (p, Math.Clamp(t, 0.02, 0.98), s < 0, Math.Abs(s));
            }
        }
        return best;
    }

    /// <summary>Gleisenden mit Trennstelle, einmal je Auswertung gesammelt.</summary>
    private static HashSet<(int PieceId, int Index)> IsolationSet(LayoutPlan plan)
    {
        var set = new HashSet<(int, int)>();
        foreach (var i in plan.Isolations) set.Add((i.PieceId, i.Index));
        return set;
    }

    private static bool Isolated(HashSet<(int PieceId, int Index)> isolations, ConnectorRef c)
        => isolations.Count > 0 && (isolations.Contains((c.PieceId, c.Index)) || (c.Link is { } l && isolations.Contains((l.PieceId, l.Index))));

    public static IReadOnlyList<PowerSection> Sections(LayoutPlan plan, Connectivity? connectivity = null)
    {
        var conn = connectivity ?? Connectivity.Analyze(plan);
        var parent = plan.Pieces.ToDictionary(p => p.Id, p => p.Id);
        int Root(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        var isolations = IsolationSet(plan);
        foreach (var c in conn.All)
            if (c.Link is { } l && !Isolated(isolations, c) && parent.ContainsKey(l.PieceId))
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
            result.Add(new PowerSection(i + 1, ids, groups[i].Sum(p => p.Def.TrackLength), plan.Feeders.Concat(DetectorFeeds(plan)).Where(f => set.Contains(f.PieceId)).ToList()));
        }
        return result;
    }

    /// <summary>
    /// Fahrstrom über Melder (digital): Ein Gleisbesetztmelder mit <see cref="Detector.Source"/> sitzt zwischen Zentrale bzw. Booster und
    /// Gleis; sein Gleis ist damit gespeist. Liefert dafür Einspeisungen, die nicht im Plan gespeichert sind (Kennung = minus Melder-Kennung).
    /// </summary>
    public static IEnumerable<Feeder> DetectorFeeds(LayoutPlan plan)
    {
        if (plan.Operation == PowerMode.Analog || plan.Detectors.Count == 0) yield break;
        var sources = PowerSupply.Sources(plan).ToList();
        foreach (var d in plan.Detectors)
            if (d.Source is { Length: > 0 } src && plan.Find(d.PieceId) is not null)
            {
                var name = d.Name.Length > 0 ? d.Name : Lang.F($"Rückmelder Nr. {d.Id}");
                // verkabelt: das Kabel zeigt die Verbindung, keine Linie zur Quelle
                int? wire = plan.DetectorWiring && d.Address is not null ? 0 : null;
                yield return new Feeder { Id = -d.Id, PieceId = d.PieceId, Circuit = src, Name = name, WireId = wire };
                // Masse (0) von Booster bzw. Zentrale über den Besetztmelder an die Schiene
                if (sources.FirstOrDefault(s => s.Name == src) is { } item && PowerSupply.HasGround(PowerSupply.Type(item)))
                    yield return new Feeder { Id = -d.Id, PieceId = d.PieceId, Circuit = src, Name = name, Ground = true, WireId = wire };
            }
    }

    /// <summary>Einspeisung, die über einen Melder kommt (siehe <see cref="DetectorFeeds"/>).</summary>
    public static bool IsDetectorFeed(Feeder f) => f.Id < 0;

    /// <summary>
    /// Sucht Kehrschleifen im Zweileiter-System: Jeder Gleisabschnitt hat eine linke und rechte Schiene. Über eine Verbindung
    /// geht links in links über; schließt sich ein Weg so, dass links auf rechts trifft, ist es eine Kehrschleife.
    /// Trennstellen unterbrechen die Verbindung. Beim Mittelleiter (Märklin) gibt es das Problem nicht.
    /// </summary>
    public static IReadOnlyList<ReverseLoop> ReverseLoops(LayoutPlan plan, Connectivity? connectivity = null)
    {
        if (IsThreeRail(plan)) return [];
        var edges = RailGraph(plan, connectivity ?? Connectivity.Analyze(plan));
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

    /// <summary>
    /// Schienengraph: Knoten = (Gleis, Fahrweg); eine Kante mit „umgekehrt“ verbindet zwei Fahrwege, bei denen die linke Schiene
    /// in die rechte übergeht. Trennstellen unterbrechen die Verbindung.
    /// </summary>
    private static Dictionary<(int, int), List<((int, int) To, bool Flip)>> RailGraph(LayoutPlan plan, Connectivity conn)
    {
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
        var isolations = IsolationSet(plan);
        var index = plan.PieceIndex();
        foreach (var c in conn.All)
        {
            if (c.Link is not { } l || c.PieceId > l.PieceId || (c.PieceId == l.PieceId && c.Index > l.Index) || Isolated(isolations, c)) continue;
            if (!index.TryGetValue(c.PieceId, out var pa) || !index.TryGetValue(l.PieceId, out var pb)) continue;
            var a = LegOf(pa, c.Index);
            var b = LegOf(pb, l.Index);
            // Zeigen beide Anschlüsse auf dasselbe Abschnittsende (Anfang/Anfang oder Ende/Ende), kehrt sich links/rechts um
            Edge((c.PieceId, a.Leg), (l.PieceId, b.Leg), a.AtEnd == b.AtEnd);
        }
        return edges;
    }

    /// <summary>
    /// Gleichstrom: Liegt die linke Schiene jedes Gleises (Fahrweg 0) auf „Schiene A“ (false) oder „Schiene B“ (true) seines Abschnitts?
    /// Ausgangspunkt je Abschnitt ist das Gleis mit der kleinsten Kennung. In einer Kehrschleife gilt der erste gefundene Weg.
    /// </summary>
    public static IReadOnlyDictionary<int, bool> RailSides(LayoutPlan plan, Connectivity? connectivity = null)
    {
        var edges = RailGraph(plan, connectivity ?? Connectivity.Analyze(plan));
        var color = new Dictionary<(int, int), bool>();
        foreach (var start in edges.Keys.OrderBy(k => k.Item1).ThenBy(k => k.Item2))
        {
            if (color.ContainsKey(start)) continue;
            color[start] = false;
            var queue = new Queue<(int, int)>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                foreach (var (v, flip) in edges[u])
                    if (!color.ContainsKey(v))
                    {
                        color[v] = color[u] ^ flip;
                        queue.Enqueue(v);
                    }
            }
        }
        return plan.Pieces.ToDictionary(p => p.Id, p => color.TryGetValue((p.Id, 0), out var c) && c);
    }

    /// <summary>Gleichstrom: Einspeisungen eines Abschnitts, deren Plus auf verschiedenen Schienen liegt (Kurzschluss).</summary>
    public static IReadOnlyList<(PowerSection Section, IReadOnlyList<Feeder> Feeders)> PolarityConflicts(LayoutPlan plan, IReadOnlyList<PowerSection> sections, Connectivity? connectivity = null)
    {
        var sides = RailSides(plan, connectivity);
        var result = new List<(PowerSection, IReadOnlyList<Feeder>)>();
        sections = sections.Select(s => s with { Feeders = s.Feeders.Where(f => !IsDetectorFeed(f)).ToList() }).ToList();
        foreach (var s in sections.Where(s => s.Feeders.Count > 1))
        {
            // Plus an Schiene B? (rechte Schiene = Gegenseite der linken; vertauscht dreht es um, ein Masseanschluss ebenso)
            var groups = s.Feeders.GroupBy(f => !(sides.TryGetValue(f.PieceId, out var left) && left) ^ f.Reversed ^ f.Ground).ToList();
            if (groups.Count > 1) result.Add((s, groups.OrderBy(g => g.Count()).First().ToList()));
        }
        return result;
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
            var supply = s.Supply.Count;
            if (supply == 0)
                issues.Add(new(IssueLevel.Fehler, Lang.T("Einspeisung"), Lang.F($"Abschnitt {s.Number} ({Fmt.Num(s.LengthMm / 1000, 2)} m, {s.PieceIds.Count} Gleise) hat keine Einspeisung – er bekommt keinen Fahrstrom. Zentrale, Booster bzw. Trafo wählen und mit „Kabel von hier verlegen“ ein Bahnstrom-Kabel auf ein Gleis des Abschnitts legen."), s.PieceIds, mid.X, mid.Y));
            else if (s.LengthMm / supply > spacing * 1.2)
                issues.Add(new(IssueLevel.Hinweis, Lang.T("Einspeisung"), Lang.F($"Abschnitt {s.Number}: {Fmt.Num(s.LengthMm / 1000, 2)} m Gleis mit {supply} Einspeisung(en) – empfohlen ist etwa alle {Fmt.Num(spacing / 1000, 1)} m eine."), s.PieceIds, mid.X, mid.Y));
            if (s.Circuits.Count > 1)
                issues.Add(new(IssueLevel.Fehler, Lang.T("Stromkreise"), Lang.F($"Abschnitt {s.Number} wird aus mehreren Stromkreisen gespeist ({string.Join(", ", s.Circuits)}). Dazwischen gehört eine Trennstelle."), s.PieceIds, mid.X, mid.Y));
        }
        var current = Current(plan);
        var threeRail = plan.Pieces.Where(IsThreeRail).Select(p => p.Id).ToList();
        var twoRail = plan.Pieces.Where(p => !IsThreeRail(p)).Select(p => p.Id).ToList();
        var threeRailSet = threeRail.ToHashSet();
        if (current == CurrentKind.Wechselstrom && twoRail.Count > 0)
        {
            var m = plan.Find(twoRail[0])!.Middle();
            issues.Add(new(IssueLevel.Fehler, Lang.T("Stromart"), Lang.F($"Wechselstrom ist eingestellt, aber {twoRail.Count} Gleise sind Zweileiter-Gleise ohne Mittelleiter. Wechselstrom braucht Mittelleiter-Gleis (z. B. Märklin H0); sonst auf Gleichstrom umstellen."), twoRail, m.X, m.Y));
        }
        else if (current == CurrentKind.Gleichstrom && threeRail.Count > 0)
        {
            var m = plan.Find(threeRail[0])!.Middle();
            issues.Add(new(IssueLevel.Warnung, Lang.T("Stromart"), Lang.F($"Gleichstrom ist eingestellt, aber {threeRail.Count} Gleise sind Mittelleiter-Gleise. Sie werden üblicherweise mit Wechselstrom bzw. Märklin-Digital betrieben."), threeRail, m.X, m.Y));
        }
        foreach (var s in sections)
        {
            if (s.PieceIds.Any(threeRailSet.Contains) && s.PieceIds.Any(id => !threeRailSet.Contains(id)))
            {
                var mixed = s.PieceIds.Where(threeRailSet.Contains).ToList();
                var m = plan.Find(mixed[0])!.Middle();
                issues.Add(new(IssueLevel.Fehler, Lang.T("Stromart"), Lang.F($"Abschnitt {s.Number} verbindet Mittelleiter- und Zweileiter-Gleise ohne Trennstelle. Am Übergang beide Schienen und den Mittelleiter trennen."), mixed, m.X, m.Y));
            }
        }
        if (current == CurrentKind.Gleichstrom && threeRail.Count == 0)
        {
            var inLoops = loops.SelectMany(l => l.PieceIds).ToHashSet();
            foreach (var (s, wrong) in PolarityConflicts(plan, sections.Where(x => !x.PieceIds.Any(inLoops.Contains)).ToList(), conn))
            {
                var p = plan.Find(wrong[0].PieceId)!.Middle();
                issues.Add(new(IssueLevel.Fehler, Lang.T("Polung"),
                    Lang.F($"Abschnitt {s.Number}: Einspeisung(en) {string.Join(", ", wrong.Select(f => f.Label))} liegen auf der falschen Schiene – Bahnstrom und Masse treffen sich auf derselben Schiene, das gibt einen Kurzschluss. Das Kabel auf die andere Schiene legen."),
                    wrong.Select(f => f.PieceId).Distinct().ToList(), p.X, p.Y));
            }
        }
        GroundIssues(plan, sections, current, issues);
        PowerIssues(plan, issues);
        foreach (var w in plan.Scenery.Where(x => x.IsWire && x.Points.Count >= 2))
        {
            if (WireLayout.EndAnchor(plan, w, false) is null && WireLayout.EndAnchor(plan, w, true) is null)
            {
                var (mx, my) = WireLayout.Middle(w);
                issues.Add(new(IssueLevel.Hinweis, Lang.T("Kabel"), Lang.F($"{w.Name} ({Fmt.Num(WireLayout.Length(w) / 1000, 2)} m) ist an keinem Ende an ein Gleis, eine Zentrale bzw. einen Trafo, einen Rückmelder, ein Signal oder ein anderes Kabel angeschlossen."), [], mx, my, w.Id));
            }
        }
        foreach (var l in loops)
        {
            var mid = plan.Find(l.PieceIds[0])!.Middle();
            issues.Add(new(IssueLevel.Fehler, Lang.T("Kehrschleife"),
                current == CurrentKind.Wechselstrom
                    ? Lang.F($"Kehrschleife über {l.PieceIds.Count} Gleise im Zweileiter-Gleis: an beiden Enden Trennstellen setzen und den Abschnitt über ein Kehrschleifenmodul speisen.")
                    : Lang.F($"Kehrschleife über {l.PieceIds.Count} Gleise: an beiden Enden Trennstellen setzen und den Abschnitt über ein Kehrschleifenmodul (analog: Polwendeschalter) speisen."), l.PieceIds, mid.X, mid.Y));
        }
        var items = SwitchingItems(plan);
        foreach (var dup in AddressConflicts(items))
            issues.Add(new(IssueLevel.Fehler, Lang.T("Adressen"), Lang.F($"Adresse {dup.Address} ist mehrfach belegt: {string.Join(", ", dup.Items.Select(i => i.Label))}."),
                dup.Items.Where(i => i.PieceId is not null).Select(i => i.PieceId!.Value).ToList(), dup.Items[0].X, dup.Items[0].Y));
        var missing = items.Where(i => i.Address is null).ToList();
        if (missing.Count > 0)
            issues.Add(new(IssueLevel.Hinweis, Lang.T("Adressen"), Lang.F($"{missing.Count} Weichen/Signale haben noch keine Digitaladresse."), missing.Where(i => i.PieceId is not null).Select(i => i.PieceId!.Value).ToList(), missing[0].X, missing[0].Y));
        return new ElectricReport(sections, loops, issues, IsThreeRail(plan), current);
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
                list.Add(new SwitchingItem(kind, string.IsNullOrWhiteSpace(p.Name) ? Lang.F($"{kind} {d.Number} (Nr. {p.Id})") : $"{kind} {p.Name}", p.Id, null, p.Address, d.Drives, m.X, m.Y));
            }
        }
        foreach (var s in plan.Signals)
        {
            var (x, y, _) = SignalPose(plan, s);
            list.Add(new SwitchingItem("Signal", string.IsNullOrWhiteSpace(s.Name) ? Lang.F($"{Signal.TypeName(s.Type)} (Nr. {s.Id})") : $"{Signal.TypeName(s.Type)} {s.Name}", null, s.Id, s.Address, s.AddressCount, x, y));
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

    /// <summary>Lage des Rückmelder-Symbols: neben der Gleismitte, gegenüber der Einspeisung.</summary>
    public static (double X, double Y) DetectorPoint(PlacedPiece p, double gauge)
    {
        var m = p.Middle();
        return (m.X + Math.Sin(m.HeadingRad) * gauge * 2.2, m.Y - Math.Cos(m.HeadingRad) * gauge * 2.2);
    }

    /// <summary>Kabelplan als Text: Abschnitte mit Einspeisungen, Trennstellen, Kehrschleifen und Decoderbelegung.</summary>
    /// <summary>
    /// Masse bzw. Rückleiter am Gleis: Gleichstrom braucht in jedem gespeisten Abschnitt einen Masseanschluss an der anderen Schiene,
    /// Wechselstrom mindestens einen Masseanschluss an den Schienen. Ältere Pläne: ein Massekabel von der Quelle zum Gleisanschluss zählt auch.
    /// </summary>
    private static void GroundIssues(LayoutPlan plan, IReadOnlyList<PowerSection> sections, CurrentKind current, List<PlanIssue> issues)
    {
        var fed = sections.Where(s => s.Supply.Count > 0).ToList();
        if (fed.Count == 0) return;
        var nets = WireLayout.Nets(plan);
        var groundNets = PowerSupply.Sources(plan).Select(PowerSupply.GroundTerminal).OfType<(double X, double Y)>().Select(g => nets(g)).OfType<int>().ToHashSet();
        bool LegacyGround(Feeder f) => plan.Find(f.PieceId) is { } p && nets(FeederPoint(plan, p, f)) is { } n && groundNets.Contains(n);
        bool Grounded(PowerSection s) => s.Feeders.Any(f => f.Ground) || s.Supply.Any(LegacyGround);
        if (current == CurrentKind.Wechselstrom)
        {
            if (!fed.Any(Grounded))
            {
                var m = plan.Find(fed[0].PieceIds[0])!.Middle();
                issues.Add(new(IssueLevel.Warnung, Lang.T("Masse"), Lang.T("Am Gleis liegt noch keine Masse. Von der Masse (0) der Zentrale bzw. des Trafos ein Kabel auf eine Schiene legen („Masse-Kabel von hier verlegen“)."), fed[0].PieceIds, m.X, m.Y));
            }
            return;
        }
        foreach (var s in fed.Where(s => !Grounded(s)))
        {
            var m = plan.Find(s.PieceIds[0])!.Middle();
            issues.Add(new(IssueLevel.Fehler, Lang.T("Masse"), Lang.F($"Abschnitt {s.Number} hat Bahnstrom, aber keinen Rückleiter: die zweite Schiene ist nicht an die Masse angeschlossen. Ein Masse-Kabel von Zentrale, Booster bzw. Trafo auf die andere Schiene legen."), s.PieceIds, m.X, m.Y));
        }
    }

    /// <summary>Stromquellen: fehlende Zentrale bzw. Trafo, unpassende Quelle zur Betriebsart, Zubehörtrafo am Gleis, Quelle ohne Gleisanschluss.</summary>
    private static void PowerIssues(LayoutPlan plan, List<PlanIssue> issues)
    {
        var mode = plan.Operation;
        var sources = PowerSupply.Sources(plan).ToList();
        foreach (var g in plan.Feeders.Concat(DetectorFeeds(plan)).Where(f => !f.Ground && plan.Find(f.PieceId) is not null).GroupBy(f => f.Circuit))
        {
            var src = sources.FirstOrDefault(s => s.Name == g.Key);
            var pieces = g.Select(f => f.PieceId).Distinct().ToList();
            var m = plan.Find(pieces[0])!.Middle();
            if (src is null)
                issues.Add(new(IssueLevel.Warnung, Lang.T("Stromversorgung"),
                    Lang.F($"{g.Count()} Einspeisung(en) hängen an „{g.Key}“, dafür steht aber keine Zentrale bzw. kein Trafo im Plan. Elektrik › Trafo / Zentrale setzen und so benennen."), pieces, m.X, m.Y));
            else if (!PowerSupply.FeedsTrack(PowerSupply.Type(src)))
                issues.Add(new(IssueLevel.Fehler, Lang.T("Stromversorgung"),
                    Lang.F($"„{src.Name}“ ist ein Zubehörtrafo für Licht und Weichen, an ihm hängen aber {g.Count()} Einspeisung(en). Fahrstrom kommt von der Zentrale, einem Booster oder einem Fahrtrafo."), pieces, src.X, src.Y, src.Id));
        }
        foreach (var src in sources)
        {
            var type = PowerSupply.Type(src);
            var count = PowerSupply.FeedersOf(plan, src).Count();
            if (PowerSupply.FeedsTrack(type) && !PowerSupply.Fits(type, mode))
                issues.Add(new(IssueLevel.Warnung, Lang.T("Stromversorgung"), mode == PowerMode.Analog
                    ? Lang.F($"„{src.Name}“ ist eine {PowerSupply.TypeName(type)}, die Anlage ist aber auf Analogbetrieb gestellt. Betrieb auf digital stellen oder die Quelle zum Fahrtrafo machen.")
                    : Lang.F($"„{src.Name}“ ist ein Fahrtrafo, die Anlage ist aber auf Digitalbetrieb gestellt. Im Digitalbetrieb kommt der Fahrstrom von Zentrale und Boostern; der Trafo darf nicht zusätzlich ans Gleis."), [], src.X, src.Y, src.Id));
            else if (PowerSupply.FeedsTrack(type) && count == 0 && plan.Pieces.Count > 0)
                issues.Add(new(IssueLevel.Hinweis, Lang.T("Stromversorgung"),
                    Lang.F($"An „{src.Name}“ ({PowerSupply.TypeName(type)}) hängt noch keine Einspeisung. Die Quelle wählen, „Kabel von hier verlegen“ und das Kabel auf dem Gleis enden lassen."), [], src.X, src.Y, src.Id));
        }
        // Masse (0) von Zentrale und Booster: angeschlossen und untereinander verbunden?
        var nets = WireLayout.Nets(plan);
        var current = Current(plan);
        var grounds = new List<(SceneryItem Src, int Net)>();
        foreach (var src in sources.Where(s => PowerSupply.HasGround(PowerSupply.Type(s))))
        {
            var gt = PowerSupply.GroundTerminal(src)!.Value;
            if (nets(gt) is { } net)
            {
                grounds.Add((src, net));
                if (nets(PowerSupply.Terminal(src)) == net)
                    issues.Add(new(IssueLevel.Fehler, Lang.T("Stromversorgung"),
                        Lang.F($"Bahnstrom (B) und Masse (0) von „{src.Name}“ sind über Kabel direkt miteinander verbunden – das ist ein Kurzschluss. Das Masse-Kabel an einer eigenen Stelle der anderen Schiene enden lassen."), [], gt.X, gt.Y, src.Id));
            }
            else if (plan.Pieces.Count > 0)
                issues.Add(new(IssueLevel.Warnung, Lang.T("Stromversorgung"),
                    Lang.F($"Die Masse (0) von „{src.Name}“ ist nicht angeschlossen – ohne Masse fließt kein Fahrstrom. Ein braunes Kabel von der Masse-Klemme auf eine Schiene oder zu einem Masse-Verteiler legen („Masse-Kabel von hier verlegen“)."), [], gt.X, gt.Y, src.Id));
        }
        if (grounds.Select(g => g.Net).Distinct().Count() > 1)
        {
            var first = grounds[0];
            foreach (var other in grounds.Where(g => g.Net != first.Net).GroupBy(g => g.Net).Select(g => g.First()))
                issues.Add(new(IssueLevel.Warnung, Lang.T("Stromversorgung"),
                    Lang.F($"Die Masse von „{other.Src.Name}“ ist nicht mit der Masse von „{first.Src.Name}“ verbunden. Zentrale und Booster brauchen eine gemeinsame Masse, z. B. über einen braunen Verteiler."), [], other.Src.X, other.Src.Y, other.Src.Id));
        }
        // Verteiler: Kabel in anderer Farbe, leere Leisten
        foreach (var d in PowerSupply.Distributors(plan))
        {
            var color = PowerSupply.DistributorColor(d);
            var wires = PowerSupply.ClampPoints(d).SelectMany(c => WireLayout.WiresAt(plan, c)).DistinctBy(w => w.Id).ToList();
            if (wires.Count == 0 && plan.Pieces.Count > 0)
                issues.Add(new(IssueLevel.Hinweis, Lang.T("Stromversorgung"), Lang.F($"Am Verteiler „{d.Name}“ hängt noch kein Kabel."), [], d.X, d.Y, d.Id));
            foreach (var w in wires.Where(w => !string.Equals(WireLayout.ColorOf(w, current), color, StringComparison.OrdinalIgnoreCase)))
            {
                var (mx, my) = WireLayout.Middle(w);
                issues.Add(new(IssueLevel.Hinweis, Lang.T("Stromversorgung"),
                    Lang.F($"{w.Name} ({WireLayout.ColorName(WireLayout.ColorOf(w, current))}) hängt am Verteiler „{d.Name}“ ({WireLayout.ColorName(color)}) – Farben besser gleich halten, sonst verwechselt man die Leitungen."), [], mx, my, w.Id));
            }
        }
        // Decoder-Bausteine: überlappende Adressbereiche, Adressen ohne Baustein
        var modules = PowerSupply.Modules(plan).Where(m => Decoders.Range(m) is not null).ToList();
        for (var i = 0; i < modules.Count; i++)
            for (var j = i + 1; j < modules.Count; j++)
            {
                var (a, b) = (modules[i], modules[j]);
                if (Decoders.ModelOf(a)!.Role != Decoders.ModelOf(b)!.Role) continue;
                var (ra, rb) = (Decoders.Range(a)!.Value, Decoders.Range(b)!.Value);
                if (ra.First <= rb.Last && rb.First <= ra.Last)
                    issues.Add(new(IssueLevel.Warnung, Lang.T("Decoder"),
                        Lang.F($"„{a.Name}“ ({ra.First}–{ra.Last}) und „{b.Name}“ ({rb.First}–{rb.Last}) benutzen dieselben Adressen. In den Eigenschaften eine andere Startadresse wählen."), [], b.X, b.Y, b.Id));
            }
        if (modules.Any(m => Decoders.ModelOf(m)!.Role == ModuleRole.Schalten))
        {
            var loose = SwitchingItems(plan).Where(i => i.Address is { } a && Decoders.ModuleFor(plan, a, ModuleRole.Schalten) is null).ToList();
            if (loose.Count > 0)
                issues.Add(new(IssueLevel.Hinweis, Lang.T("Decoder"),
                    Lang.F($"{loose.Count} Weichen/Signale haben eine Adresse, die kein Decoder-Baustein im Plan abdeckt (z. B. {loose[0].Label}, Adresse {loose[0].Address})."),
                    loose.Where(i => i.PieceId is not null).Select(i => i.PieceId!.Value).ToList(), loose[0].X, loose[0].Y));
        }
        if (modules.Any(m => Decoders.ModelOf(m)!.Role == ModuleRole.Melden))
        {
            var loose = plan.Detectors.Where(d => d.Address is { } a && Decoders.ModuleFor(plan, a, ModuleRole.Melden) is null).ToList();
            if (loose.Count > 0 && plan.Find(loose[0].PieceId) is { } lp)
                issues.Add(new(IssueLevel.Hinweis, Lang.T("Decoder"),
                    Lang.F($"{loose.Count} Rückmelder haben einen Kontakt, den kein Rückmelde-Baustein im Plan abdeckt (z. B. {loose[0].Name}, Kontakt {loose[0].Address})."),
                    loose.Select(d => d.PieceId).ToList(), lp.Middle().X, lp.Middle().Y));
        }
        if (mode == PowerMode.Digital && sources.Count(s => PowerSupply.Type(s) == PowerSourceType.Zentrale) > 1)
        {
            var second = sources.Where(s => PowerSupply.Type(s) == PowerSourceType.Zentrale).Skip(1).First();
            issues.Add(new(IssueLevel.Hinweis, Lang.T("Stromversorgung"), Lang.T("Im Plan stehen mehrere Digitalzentralen. Üblich ist eine Zentrale; weitere Stromkreise speisen Booster."), [], second.X, second.Y, second.Id));
        }
    }

    public static string WiringPlan(LayoutPlan plan)
    {
        var r = Analyze(plan);
        var sb = new StringBuilder();
        sb.AppendLine(Lang.F($"Kabelplan – {plan.Name} (Spur {plan.GaugeId}, {(r.ThreeRail ? Lang.T("Mittelleiter") : Lang.T("Zweileiter"))}, {CurrentName(r.Current)})"));
        sb.AppendLine(new string('=', 60));
        sb.AppendLine(CurrentHint(plan));
        sb.AppendLine();
        sb.AppendLine(Lang.F($"Stromversorgung ({PowerSupply.ModeName(plan.Operation)})"));
        var sources = PowerSupply.Sources(plan).ToList();
        if (sources.Count == 0) sb.AppendLine(Lang.T("  noch keine Zentrale bzw. kein Trafo im Plan"));
        foreach (var src in sources)
        {
            var fs = PowerSupply.FeedersOf(plan, src).ToList();
            sb.AppendLine(Lang.F($"  {src.Name}: {PowerSupply.TypeName(PowerSupply.Type(src))}") +
                          (PowerSupply.FeedsTrack(PowerSupply.Type(src)) ? Lang.F($", {fs.Count} Einspeisung(en)") : "") +
                          (PowerSupply.GroundTerminal(src) is { } gt ? (WireLayout.WiresAt(plan, gt).Any() ? Lang.T(", Masse angeschlossen") : Lang.T(", Masse fehlt!")) : ""));
        }
        foreach (var d in PowerSupply.Distributors(plan))
        {
            var n = PowerSupply.ClampPoints(d).SelectMany(c => WireLayout.WiresAt(plan, c)).DistinctBy(w => w.Id).Count();
            sb.AppendLine(Lang.F($"  {d.Name}: Verteiler {WireLayout.ColorName(PowerSupply.DistributorColor(d))}, {n} Kabel"));
        }
        var mods = PowerSupply.Modules(plan).ToList();
        if (mods.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Lang.T("Decoder-Bausteine"));
            foreach (var mod in mods)
            {
                sb.AppendLine($"  {mod.Name}: {Decoders.ModelOf(mod)?.Title ?? mod.Model}{(Decoders.Range(mod) is { } mr ? $" ({mr.First}–{mr.Last})" : "")}");
                foreach (var a in Decoders.Assigned(plan, mod)) sb.AppendLine($"    {a.Port,2}: {a.Label} [{a.Address}]");
            }
        }
        sb.AppendLine();
        sb.AppendLine(Lang.T("Stromabschnitte"));
        foreach (var s in r.Sections)
        {
            sb.AppendLine(Lang.F($"  Abschnitt {s.Number}: {s.PieceIds.Count} Gleise, {Fmt.Num(s.LengthMm / 1000, 2)} m"));
            if (s.Feeders.Count == 0) sb.AppendLine(Lang.T("    keine Einspeisung!"));
            foreach (var f in s.Feeders)
                sb.AppendLine((f.Ground && IsDetectorFeed(f)
                    ? Lang.F($"    Masse über {f.Label} an Gleis {plan.Find(f.PieceId)?.Def.Number} (Nr. {f.PieceId}) → {f.Circuit}")
                    : f.Ground
                    ? Lang.F($"    Masse {f.Label} an Gleis {plan.Find(f.PieceId)?.Def.Number} (Nr. {f.PieceId}) → {f.Circuit}")
                    : IsDetectorFeed(f) ? Lang.F($"    Fahrstrom über {f.Label} an Gleis {plan.Find(f.PieceId)?.Def.Number} (Nr. {f.PieceId}) → {f.Circuit}")
                    : Lang.F($"    Einspeisung {f.Label} an Gleis {plan.Find(f.PieceId)?.Def.Number} (Nr. {f.PieceId}) → {f.Circuit}"))
                    + (!IsDetectorFeed(f) && (r.Current == CurrentKind.Gleichstrom || f.Ground) ? (f.Reversed ? Lang.T(" (rechte Schiene)") : Lang.T(" (linke Schiene)")) : ""));
        }
        sb.AppendLine();
        sb.AppendLine(Lang.F($"Trennstellen ({plan.Isolations.Count})"));
        foreach (var i in plan.Isolations)
            sb.AppendLine(Lang.F($"  Nr. {i.Id}: Gleis {plan.Find(i.PieceId)?.Def.Number} (Nr. {i.PieceId}), Ende {i.Index}"));
        if (plan.Detectors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Lang.F($"Rückmelder ({plan.Detectors.Count})"));
            foreach (var d in plan.Detectors.OrderBy(d => d.Address ?? int.MaxValue))
                sb.AppendLine(Lang.F($"  {(d.Name.Length > 0 ? d.Name : $"Nr. {d.Id}")}: {Detector.TypeName(d.Type)} an Gleis {plan.Find(d.PieceId)?.Def.Number} (Nr. {d.PieceId})") +
                              (d.Address is { } a ? Lang.F($", Kontakt {a} (Modul {(a - 1) / Detector.InputsPerModule + 1}, Eingang {(a - 1) % Detector.InputsPerModule + 1})") : Lang.T(", ohne Adresse")));
        }
        var wires = plan.Scenery.Where(x => x.IsWire && x.Points.Count >= 2).ToList();
        if (wires.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Lang.F($"Kabel ({wires.Count})"));
            foreach (var w in wires)
            {
                var from = WireLayout.EndAnchor(plan, w, false)?.Text ?? Lang.T("frei");
                var to = WireLayout.EndAnchor(plan, w, true)?.Text ?? Lang.T("frei");
                sb.AppendLine(Lang.F($"  {w.Name}: {WireLayout.UseName(WireLayout.Use(w))}, {WireLayout.ColorName(WireLayout.ColorOf(w, r.Current))}, {WireLayout.SectionText(WireLayout.Section(w, plan.Gauge))}, {Fmt.Num(WireLayout.Length(w) / 1000, 2)} m – {from} → {to}"));
            }
            sb.AppendLine(Lang.F($"  Einkauf (mit {Fmt.Num(WireLayout.Reserve * 100, 0)} % Zugabe):"));
            foreach (var (color, section, meters) in WireLayout.Totals(plan))
                sb.AppendLine(Lang.F($"    {WireLayout.ColorName(color)} {WireLayout.SectionText(section)}: {Fmt.Num(meters, 1)} m"));
        }
        sb.AppendLine();
        sb.AppendLine(Lang.T("Kehrschleifen"));
        if (r.ThreeRail) sb.AppendLine(Lang.T("  Mittelleiter: Kehrschleifen brauchen keine Sonderschaltung."));
        else if (r.ReverseLoops.Count == 0) sb.AppendLine("  keine");
        foreach (var l in r.ReverseLoops)
            sb.AppendLine(Lang.F($"  Gleise Nr. {string.Join(", ", l.PieceIds)} – beidseitig trennen, über Kehrschleifenmodul speisen"));
        sb.AppendLine();
        sb.AppendLine(Lang.T("Weichen und Signale"));
        foreach (var i in SwitchingItems(plan).Where(i => i.Address is not null).OrderBy(i => i.Address))
            sb.AppendLine(Lang.F($"  Adresse {i.AddressText} – {i.Label}") + (Decoders.Location(plan, i.Address) is { } loc ? $" → {loc}" : ""));
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
            foreach (var i in r.Issues) sb.AppendLine($"  [{i.Level.Text()}] {i.Text}");
        }
        sb.AppendLine();
        sb.Append(ConnectionList.ToText(plan));
        return sb.ToString();
    }
}
