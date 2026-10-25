namespace TrackMaster.Core;

/// <summary>
/// Kollisions- und Planprüfung: zu enge Gleisabstände, zu geringe Durchfahrtshöhe, Gleise außerhalb der Platte,
/// zu starke Steigungen und Gleise in Gebäuden, Gewässern oder unter dem Gelände.
/// </summary>
public static class PlanCheck
{
    /// <summary>Kleinster Gleisachsabstand als Vielfaches der Spurweite (H0: etwa 38 mm, NEM-Lichtraum).</summary>
    public const double MinSpacingFactor = 2.3;
    public const double WarnGradePercent = 3.0;
    public const double MaxGradePercent = 5.0;

    private readonly record struct Sample(int PieceId, double X, double Y, double Z);

    public static IReadOnlyList<PlanIssue> Run(LayoutPlan plan)
    {
        var issues = new List<PlanIssue>();
        var conn = Connectivity.Analyze(plan);
        var gauge = plan.Gauge;
        var minSpacing = gauge.TrackWidth * MinSpacingFactor;
        var step = Math.Max(gauge.TrackWidth * 0.75, 2);

        // Abtastpunkte auf allen Gleisachsen
        var samples = new List<Sample>();
        foreach (var p in plan.Pieces)
        {
            var legs = p.WorldLegs();
            for (var li = 0; li < legs.Count; li++)
            {
                var n = Math.Max(2, (int)Math.Ceiling(legs[li].TrackLength / step));
                for (var i = 0; i <= n; i++)
                {
                    var t = (double)i / n;
                    var q = legs[li].At(t);
                    samples.Add(new Sample(p.Id, q.X, q.Y, p.HeightAt(li, t)));
                }
            }
        }

        // Nachbarn bis zwei Schritte weit (Weichenbereich) nicht als Kollision werten
        var neighbours = plan.Pieces.ToDictionary(p => p.Id, _ => new HashSet<int>());
        foreach (var c in conn.All)
            if (c.Link is { } l) { neighbours[c.PieceId].Add(l.PieceId); neighbours[l.PieceId].Add(c.PieceId); }
        bool Near(int a, int b) => a == b || neighbours[a].Contains(b) || neighbours[a].Any(x => neighbours[x].Contains(b));

        var cells = new Dictionary<(int, int), List<Sample>>();
        (int, int) Cell(double x, double y) => ((int)Math.Floor(x / minSpacing), (int)Math.Floor(y / minSpacing));
        foreach (var s in samples)
        {
            var k = Cell(s.X, s.Y);
            if (!cells.TryGetValue(k, out var list)) cells[k] = list = [];
            list.Add(s);
        }
        var worst = new Dictionary<(int, int), (double D, double Dz, double X, double Y)>();
        foreach (var s in samples)
        {
            var (cx, cy) = Cell(s.X, s.Y);
            for (var i = -1; i <= 1; i++)
                for (var j = -1; j <= 1; j++)
                {
                    if (!cells.TryGetValue((cx + i, cy + j), out var list)) continue;
                    foreach (var o in list)
                    {
                        if (o.PieceId <= s.PieceId || Near(s.PieceId, o.PieceId)) continue;
                        var d = Math.Sqrt((s.X - o.X) * (s.X - o.X) + (s.Y - o.Y) * (s.Y - o.Y));
                        if (d >= minSpacing) continue;
                        var dz = Math.Abs(s.Z - o.Z);
                        if (dz >= gauge.ClearanceHeight) continue;
                        var key = (s.PieceId, o.PieceId);
                        if (!worst.TryGetValue(key, out var w) || d < w.D)
                            worst[key] = (d, dz, (s.X + o.X) / 2, (s.Y + o.Y) / 2);
                    }
                }
        }
        // Zusammenhängende Paare zu einer Meldung je Stelle zusammenfassen
        var reported = new List<(double X, double Y)>();
        foreach (var ((a, b), w) in worst.OrderBy(kv => kv.Value.D))
        {
            if (reported.Any(r => Math.Abs(r.X - w.X) < minSpacing * 2 && Math.Abs(r.Y - w.Y) < minSpacing * 2)) continue;
            reported.Add((w.X, w.Y));
            var pa = plan.Find(a)!.Def.Number;
            var pb = plan.Find(b)!.Def.Number;
            issues.Add(w.Dz > gauge.TrackWidth
                ? new PlanIssue(IssueLevel.Fehler, "Durchfahrtshöhe",
                    $"Gleise {pa} (Nr. {a}) und {pb} (Nr. {b}) kreuzen mit nur {Fmt.Num(w.Dz, 0)} mm Höhenunterschied – nötig sind mindestens {Fmt.Num(gauge.ClearanceHeight, 0)} mm.", [a, b], w.X, w.Y)
                : new PlanIssue(IssueLevel.Fehler, "Kollision",
                    $"Gleise {pa} (Nr. {a}) und {pb} (Nr. {b}) kommen sich auf {Fmt.Num(w.D, 0)} mm nahe – Mindestabstand {Fmt.Num(minSpacing, 0)} mm.", [a, b], w.X, w.Y));
        }

        // Außerhalb der Platte
        var outside = samples.Where(s => !BoardGeometry.Contains(plan, s.X, s.Y)).GroupBy(s => s.PieceId).ToList();
        if (outside.Count > 0)
        {
            var first = outside[0].First();
            issues.Add(new PlanIssue(IssueLevel.Warnung, "Plattenrand", $"{outside.Count} Gleis(e) ragen über die Anlagenplatte hinaus.", outside.Select(g => g.Key).ToList(), first.X, first.Y));
        }

        // Steigungen
        foreach (var p in plan.Pieces)
        {
            var pct = Math.Abs(p.EffectiveGrade) * 100;
            if (pct <= WarnGradePercent + 1e-6) continue;
            var m = p.Middle();
            issues.Add(new PlanIssue(pct > MaxGradePercent + 1e-6 ? IssueLevel.Fehler : IssueLevel.Warnung, "Steigung",
                $"Gleis {p.Def.Number} (Nr. {p.Id}) hat {Fmt.Num(pct, 1)} % Steigung – empfohlen sind höchstens {Fmt.Num(WarnGradePercent, 0)} %, in Bögen weniger.", [p.Id], m.X, m.Y));
        }
        if (plan.Pieces.Any(p => p.Height < -0.5))
        {
            var p = plan.Pieces.First(p => p.Height < -0.5);
            var m = p.Middle();
            issues.Add(new PlanIssue(IssueLevel.Hinweis, "Höhe", "Gleise liegen unter der Plattenoberkante – dafür muss die Platte ausgeschnitten werden.", plan.Pieces.Where(q => q.Height < -0.5).Select(q => q.Id).ToList(), m.X, m.Y));
        }

        // Gelände und Gebäude
        foreach (var s in plan.Scenery)
        {
            var outline = s.Outline();
            var hits = samples.Where(q => Geometry2D.PointInPolygon(outline, q.X, q.Y)).ToList();
            if (hits.Count == 0) continue;
            var ids = hits.Select(q => q.PieceId).Distinct().ToList();
            var (cx, cy) = s.Center();
            switch (s.Kind)
            {
                case SceneryKind.Building when hits.Any(q => q.Z < s.Height):
                    issues.Add(new PlanIssue(IssueLevel.Warnung, "Gebäude", $"Gleise führen durch das Gebäude „{s.Name}“.", ids, cx, cy));
                    break;
                case SceneryKind.Water when hits.Any(q => q.Z < 5):
                    issues.Add(new PlanIssue(IssueLevel.Warnung, "Gewässer", $"Gleise liegen im Gewässer „{s.Name}“ – hier fehlt eine Brücke (Gleis höher legen).", ids, cx, cy));
                    break;
                case SceneryKind.Terrain:
                    var buried = hits.Where(q => q.Z < s.Height - gauge.TrackWidth && plan.Find(q.PieceId) is { Tunnel: false }).Select(q => q.PieceId).Distinct().ToList();
                    if (buried.Count > 0)
                        issues.Add(new PlanIssue(IssueLevel.Hinweis, "Tunnel", $"{buried.Count} Gleis(e) liegen unter der Höhenfläche „{s.Name}“ – als Tunnel markieren?", buried, cx, cy));
                    break;
            }
        }

        var open = conn.Open.Count;
        if (open > 0 && plan.Pieces.Count > 0)
        {
            var c = conn.Open[0];
            issues.Add(new PlanIssue(IssueLevel.Hinweis, "Gleisenden", $"{open} offene Gleisende(n) ohne Prellbock.", conn.Open.Select(o => o.PieceId).Distinct().ToList(), c.Pose.X, c.Pose.Y));
        }
        return issues.OrderByDescending(i => i.Level).ToList();
    }
}
