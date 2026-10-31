namespace TrackMaster.Core;

/// <summary>Grundform der Anlagenplatte. Breite und Tiefe sind immer die äußeren Maße; bei <see cref="Module"/> ergeben sie sich aus den Modulen.</summary>
public enum BoardShape { Rechteck, L, U, T, Z, Ring, G, E, Module }

/// <summary>Angaben für einen neuen Plan: Spur und Anlagenplatte.</summary>
/// <param name="LegLeft">U-Form: Länge des linken Schenkels in Grundstellung (0 = volle Tiefe).</param>
/// <param name="LegRight">U-Form: Länge des rechten Schenkels in Grundstellung (0 = volle Tiefe).</param>
public sealed record BoardSetup(string GaugeId, BoardShape Shape, double Width, double Height, double Depth, int Turn, double LegLeft = 0, double LegRight = 0)
{
    /// <summary>Vorschlag für eine Spur: Rechteck in Standardgröße, Formen mit Schenkeln größer.</summary>
    public static BoardSetup Default(string gaugeId, BoardShape shape = BoardShape.Rechteck)
    {
        var g = Catalog.GetGauge(gaugeId);
        static double R(double v) => Math.Round(v / 50) * 50;
        var depth = R(g.DefaultBoardHeight * 0.48);
        // Modulanlage: Vorschlag für das erste Modul
        if (shape == BoardShape.Module) return new(g.Id, shape, R(g.DefaultBoardWidth * 0.5), R(g.DefaultBoardHeight * 0.4), depth, 0);
        if (shape == BoardShape.Rechteck) return new(g.Id, shape, g.DefaultBoardWidth, g.DefaultBoardHeight, depth, 0);
        // E und G haben drei Arme übereinander: höher und mit schmaleren Schenkeln, damit Lücken zwischen den Armen bleiben
        return shape is BoardShape.E or BoardShape.G
            ? new(g.Id, shape, R(g.DefaultBoardWidth * 1.5), R(g.DefaultBoardHeight * 2.4), R(g.DefaultBoardHeight * 0.4), 0)
            : new(g.Id, shape, R(g.DefaultBoardWidth * 1.5), R(g.DefaultBoardHeight * 1.6), depth, 0);
    }
}

/// <summary>Rechteck in Plankoordinaten (mm).</summary>
public readonly record struct BoardRect(double X0, double Y0, double X1, double Y1)
{
    public bool Contains(double x, double y, double tolerance = 0)
        => x >= X0 - tolerance && x <= X1 + tolerance && y >= Y0 - tolerance && y <= Y1 + tolerance;
}

/// <summary>Eigenes Maß eines Schenkels: Tiefe (Breite des Schenkels) und Länge in mm; 0 = gemeinsame Schenkeltiefe bzw. volle Länge.</summary>
public sealed record BoardLeg(double Depth, double Length);

/// <summary>Griff an einem Schenkel: <see cref="Length"/> = Länge (freies Ende), sonst Tiefe (Innenkante); Wert ändert sich um Axis · Verschiebung.</summary>
public sealed record LegGrip(int Leg, bool Length, double X, double Y, double AxisX, double AxisY, double Value);

/// <summary>
/// Geometrie der Anlagenplatte: Die Form wird aus achsparallelen Rechtecken zusammengesetzt
/// (für Füllung, 3D-Ansicht und Plattenrand-Prüfung) und als Umriss gezeichnet.
/// Schenkeltiefe = Breite der Schenkel bei L, U, T, Z und Ring. Drehung in Vierteldrehungen im Uhrzeigersinn.
/// </summary>
public static class BoardGeometry
{
    public static string Name(BoardShape s) => s switch
    {
        BoardShape.Rechteck => Lang.T("Rechteck"),
        BoardShape.L => "L-Form",
        BoardShape.U => "U-Form",
        BoardShape.T => "T-Form",
        BoardShape.Z => "Z-Form",
        BoardShape.Ring => Lang.T("Ring (rundum, Mitte offen)"),
        BoardShape.G => "G-Form",
        BoardShape.E => "E-Form",
        BoardShape.Module => Lang.T("Module (zusammengesetzt)"),
        _ => s.ToString(),
    };

    public static IReadOnlyList<BoardShape> All { get; } = Enum.GetValues<BoardShape>();

    /// <summary>Hat die Form Schenkel (dann sind Schenkeltiefe und Ausrichtung einstellbar)?</summary>
    public static bool HasLegs(BoardShape s) => s is not (BoardShape.Rechteck or BoardShape.Module);

    /// <summary>Schenkeltiefe, auf sinnvolle Werte für Form und Maße begrenzt.</summary>
    public static double Depth(BoardShape shape, double width, double height, double depth, int turn)
    {
        var (w, h) = turn % 2 == 0 ? (width, height) : (height, width);
        var max = MaxDepth(shape, w, h);
        // Obergrenze knapp unter dem Maximum, Untergrenze nie darüber (auch bei sehr kleinen oder unfertig eingegebenen Maßen)
        var hi = Math.Max(0, max * 0.98);
        var lo = Math.Min(100, hi);
        return double.IsFinite(depth) ? Math.Clamp(depth, lo, hi) : hi;
    }

    /// <summary>Länge eines U-Schenkels: 0 = volle Tiefe, sonst zwischen Schenkeltiefe und Tiefe.</summary>
    public static double Leg(double leg, double depth, double fullLength)
        => leg <= 0 || !double.IsFinite(leg) ? fullLength : Math.Clamp(leg, Math.Min(depth, fullLength), fullLength);

    public static IReadOnlyList<BoardRect> Rects(LayoutPlan p)
        => p.BoardShape == BoardShape.Module && p.Modules.Count > 0 ? p.Modules.Select(m => m.Rect).ToList() : Rects(p.BoardShape, p.BoardWidth, p.BoardHeight, p.BoardDepth, p.BoardTurn, p.BoardLegLeft, p.BoardLegRight, p.BoardLegs);

    public static IReadOnlyList<BoardRect> Rects(BoardShape shape, double width, double height, double depth, int turn, double legLeft = 0, double legRight = 0, IReadOnlyList<BoardLeg>? legs = null)
    {
        turn = ((turn % 4) + 4) % 4;
        var (w, h) = turn % 2 == 0 ? (width, height) : (height, width);
        var (local, _) = Layout(shape, w, h, Depth(shape, width, height, depth, turn), legLeft, legRight, legs);
        return local.Select(r =>
        {
            var (ax, ay) = Turn(r.X0, r.Y0, w, h, turn);
            var (bx, by) = Turn(r.X1, r.Y1, w, h, turn);
            return new BoardRect(Math.Min(ax, bx), Math.Min(ay, by), Math.Max(ax, bx), Math.Max(ay, by));
        }).ToList();
    }

    /// <summary>Namen der Schenkel einer Form in der Reihenfolge von <see cref="LayoutPlan.BoardLegs"/>.</summary>
    public static IReadOnlyList<string> LegNames(BoardShape shape) => shape switch
    {
        BoardShape.L => [Lang.T("Linker Schenkel"), Lang.T("Unterer Schenkel")],
        BoardShape.U => [Lang.T("Linker Schenkel"), Lang.T("Rechter Schenkel"), Lang.T("Verbindung unten")],
        BoardShape.T => [Lang.T("Querbalken oben"), Lang.T("Stamm")],
        BoardShape.Z => [Lang.T("Oberer Arm"), Lang.T("Mittelstück"), Lang.T("Unterer Arm")],
        BoardShape.Ring => [Lang.T("Oben"), Lang.T("Unten"), Lang.T("Links"), Lang.T("Rechts")],
        BoardShape.E => [Lang.T("Rücken"), Lang.T("Oberer Arm"), Lang.T("Mittlerer Arm"), Lang.T("Unterer Arm")],
        BoardShape.G => [Lang.T("Oben"), Lang.T("Links"), Lang.T("Unten"), Lang.T("Rechts hoch"), Lang.T("Haken")],
        _ => [],
    };

    /// <summary>Tatsächliche Tiefe und Länge jedes Schenkels (nach Begrenzung); Länge 0 = fest durch die Außenmaße.</summary>
    public static IReadOnlyList<BoardLeg> ResolvedLegs(LayoutPlan p)
    {
        var turn = ((p.BoardTurn % 4) + 4) % 4;
        var (w, h) = turn % 2 == 0 ? (p.BoardWidth, p.BoardHeight) : (p.BoardHeight, p.BoardWidth);
        var (_, handles) = Layout(p.BoardShape, w, h, Depth(p.BoardShape, p.BoardWidth, p.BoardHeight, p.BoardDepth, turn), p.BoardLegLeft, p.BoardLegRight, p.BoardLegs);
        return LegNames(p.BoardShape).Select((_, i) => new BoardLeg(
            handles.FirstOrDefault(g => g.Leg == i && !g.Length)?.Value ?? 0,
            handles.FirstOrDefault(g => g.Leg == i && g.Length)?.Value ?? 0)).ToList();
    }

    /// <summary>Hat Schenkel <paramref name="leg"/> eine einstellbare Länge?</summary>
    public static bool LegHasLength(BoardShape shape, int leg) => shape switch
    {
        BoardShape.L => true,
        BoardShape.U => leg < 2,
        BoardShape.T => leg == 1,
        BoardShape.Z => leg != 1,
        BoardShape.E => leg > 0,
        BoardShape.G => leg is 0 or 3 or 4,
        _ => false,
    };

    /// <summary>
    /// Griffe zum Ziehen mit der Maus: je Schenkel einer für die Tiefe (Innenkante) und, wo einstellbar, einer für die Länge (freies Ende).
    /// <see cref="LegGrip.Axis"/> sagt, wie sich der Wert ändert, wenn der Griff um 1 mm verschoben wird (in Plankoordinaten).
    /// </summary>
    public static IReadOnlyList<LegGrip> LegGrips(LayoutPlan p)
    {
        if (!HasLegs(p.BoardShape)) return [];
        var turn = ((p.BoardTurn % 4) + 4) % 4;
        var (w, h) = turn % 2 == 0 ? (p.BoardWidth, p.BoardHeight) : (p.BoardHeight, p.BoardWidth);
        var (_, handles) = Layout(p.BoardShape, w, h, Depth(p.BoardShape, p.BoardWidth, p.BoardHeight, p.BoardDepth, turn), p.BoardLegLeft, p.BoardLegRight, p.BoardLegs);
        return handles.Select(g =>
        {
            var (x, y) = Turn(g.X, g.Y, w, h, turn);
            var (ax, ay) = Turn(g.X + g.AxisX, g.Y + g.AxisY, w, h, turn);
            return g with { X = x, Y = y, AxisX = ax - x, AxisY = ay - y };
        }).ToList();
    }

    /// <summary>Setzt Tiefe oder Länge eines Schenkels (bei der U-Form landen die Längen der Seitenschenkel wie bisher in BoardLegLeft/-Right).</summary>
    public static void SetLeg(LayoutPlan p, int leg, double? depth = null, double? length = null)
    {
        while (p.BoardLegs.Count <= leg) p.BoardLegs.Add(new BoardLeg(0, 0));
        var cur = p.BoardLegs[leg];
        if (depth is { } d) cur = cur with { Depth = Math.Max(0, Math.Round(d)) };
        if (length is { } l)
        {
            l = Math.Max(0, Math.Round(l));
            if (p.BoardShape == BoardShape.U && leg == 0) p.BoardLegLeft = l;
            else if (p.BoardShape == BoardShape.U && leg == 1) p.BoardLegRight = l;
            else cur = cur with { Length = l };
        }
        p.BoardLegs[leg] = cur;
    }

    /// <summary>Alle Schenkel wieder auf die gemeinsame Schenkeltiefe und volle Länge.</summary>
    public static void ResetLegs(LayoutPlan p)
    {
        p.BoardLegs.Clear();
        p.BoardLegLeft = p.BoardLegRight = 0;
    }

    /// <summary>
    /// Rechtecke und Griffe einer Form in Grundstellung (Breite <paramref name="w"/>, Tiefe <paramref name="h"/>).
    /// Jeder Schenkel hat eine eigene Tiefe (sonst <paramref name="d"/>) und, wo es geht, eine eigene Länge (sonst die volle).
    /// </summary>
    private static (List<BoardRect> Rects, List<LegGrip> Grips) Layout(BoardShape shape, double w, double h, double d, double legLeft, double legRight, IReadOnlyList<BoardLeg>? legs)
    {
        var n = LegNames(shape).Count;
        var hi = Math.Max(0, d <= 0 ? 0 : MaxDepth(shape, w, h) * 0.98);
        var lo = Math.Min(50, hi);
        var D = new double[n];
        for (var i = 0; i < n; i++)
            D[i] = legs is not null && i < legs.Count && legs[i].Depth > 0 && double.IsFinite(legs[i].Depth) ? Math.Clamp(legs[i].Depth, lo, hi) : d;
        double L(int i, double fallback, double min, double max)
        {
            var v = legs is not null && i < legs.Count && legs[i].Length > 0 && double.IsFinite(legs[i].Length) ? legs[i].Length : fallback;
            return max < min ? max : Math.Clamp(v, min, max);
        }
        var rects = new List<BoardRect>();
        var grips = new List<LegGrip>();
        void Depth(int i, double x, double y, double ax, double ay) => grips.Add(new LegGrip(i, false, x, y, ax, ay, D[i]));
        void Length(int i, double value, double x, double y, double ax, double ay) => grips.Add(new LegGrip(i, true, x, y, ax, ay, value));
        switch (shape)
        {
            case BoardShape.L:
            {
                var l0 = L(0, h, D[1] + 50, h);
                var l1 = L(1, w, D[0] + 50, w);
                rects.Add(new(0, h - l0, D[0], h));
                rects.Add(new(D[0], h - D[1], l1, h));
                Depth(0, D[0], (h - l0 + h - D[1]) / 2, 1, 0); Length(0, l0, D[0] / 2, h - l0, 0, -1);
                Depth(1, (D[0] + l1) / 2, h - D[1], 0, -1); Length(1, l1, l1, h - D[1] / 2, 1, 0);
                break;
            }
            case BoardShape.U:
            {
                var l0 = L(0, legLeft > 0 ? legLeft : h, D[2] + 50, h);
                var l1 = L(1, legRight > 0 ? legRight : h, D[2] + 50, h);
                rects.Add(new(0, h - l0, D[0], h));
                rects.Add(new(w - D[1], h - l1, w, h));
                rects.Add(new(D[0], h - D[2], w - D[1], h));
                Depth(0, D[0], (h - l0 + h - D[2]) / 2, 1, 0); Length(0, l0, D[0] / 2, h - l0, 0, -1);
                Depth(1, w - D[1], (h - l1 + h - D[2]) / 2, -1, 0); Length(1, l1, w - D[1] / 2, h - l1, 0, -1);
                Depth(2, w / 2, h - D[2], 0, -1);
                break;
            }
            case BoardShape.T:
            {
                var l1 = L(1, h, D[0] + 50, h);
                rects.Add(new(0, 0, w, D[0]));
                rects.Add(new((w - D[1]) / 2, D[0], (w + D[1]) / 2, l1));
                Depth(0, w / 4, D[0], 0, 1);
                Depth(1, (w + D[1]) / 2, (D[0] + l1) / 2, 2, 0); Length(1, l1, w / 2, l1, 0, 1);
                break;
            }
            case BoardShape.Z:
            {
                var l0 = L(0, (w + D[1]) / 2, (w - D[1]) / 2 + 50, w);
                var l2 = L(2, (w + D[1]) / 2, (w - D[1]) / 2 + 50, w);
                rects.Add(new(0, 0, l0, D[0]));
                rects.Add(new((w - D[1]) / 2, D[0], (w + D[1]) / 2, h - D[2]));
                rects.Add(new(w - l2, h - D[2], w, h));
                Depth(0, (w - D[1]) / 4, D[0], 0, 1); Length(0, l0, l0, D[0] / 2, 1, 0);
                Depth(1, (w + D[1]) / 2, h / 2, 2, 0);
                Depth(2, w - (w - D[1]) / 4, h - D[2], 0, -1); Length(2, l2, w - l2, h - D[2] / 2, -1, 0);
                break;
            }
            case BoardShape.Ring:
                rects.Add(new(0, 0, w, D[0]));
                rects.Add(new(0, h - D[1], w, h));
                rects.Add(new(0, D[0], D[2], h - D[1]));
                rects.Add(new(w - D[3], D[0], w, h - D[1]));
                Depth(0, w / 2, D[0], 0, 1);
                Depth(1, w / 2, h - D[1], 0, -1);
                Depth(2, D[2], h / 2, 1, 0);
                Depth(3, w - D[3], h / 2, -1, 0);
                break;
            case BoardShape.E:
            {
                // Rücken links, drei Arme nach rechts
                var l1 = L(1, w, D[0] + 50, w);
                var l2 = L(2, w, D[0] + 50, w);
                var l3 = L(3, w, D[0] + 50, w);
                rects.Add(new(0, 0, D[0], h));
                rects.Add(new(D[0], 0, l1, D[1]));
                rects.Add(new(D[0], (h - D[2]) / 2, l2, (h + D[2]) / 2));
                rects.Add(new(D[0], h - D[3], l3, h));
                Depth(0, D[0], (D[1] + (h - D[2]) / 2) / 2, 1, 0);
                Depth(1, (D[0] + l1) / 2, D[1], 0, 1); Length(1, l1, l1, D[1] / 2, 1, 0);
                Depth(2, (D[0] + l2) / 2, (h + D[2]) / 2, 0, 2); Length(2, l2, l2, h / 2, 1, 0);
                Depth(3, (D[0] + l3) / 2, h - D[3], 0, -1); Length(3, l3, l3, h - D[3] / 2, 1, 0);
                break;
            }
            case BoardShape.G:
            {
                // oben, links und unten wie ein C, rechts unten ein Stück hoch und ein Haken nach innen
                var l0 = L(0, w, D[1] + 50, w);
                var l3 = L(3, h / 2 - D[2], D[4], h - D[2] - D[0] - 50);
                var l4 = L(4, w / 2 - D[3], 50, w - D[3] - D[1] - 50);
                var top = h - D[2] - l3;
                rects.Add(new(0, 0, l0, D[0]));
                rects.Add(new(0, D[0], D[1], h - D[2]));
                rects.Add(new(0, h - D[2], w, h));
                rects.Add(new(w - D[3], top, w, h - D[2]));
                rects.Add(new(w - D[3] - l4, top, w - D[3], top + D[4]));
                Depth(0, l0 / 2, D[0], 0, 1); Length(0, l0, l0, D[0] / 2, 1, 0);
                Depth(1, D[1], (D[0] + h - D[2]) / 2, 1, 0);
                Depth(2, w / 4, h - D[2], 0, -1);
                Depth(3, w - D[3], (top + D[4] + h - D[2]) / 2, -1, 0); Length(3, l3, w - D[3] / 2, top, 0, -1);
                Depth(4, w - D[3] - l4 / 2, top + D[4], 0, 1); Length(4, l4, w - D[3] - l4, top + D[4] / 2, -1, 0);
                break;
            }
            default:
                rects.Add(new(0, 0, w, h));
                break;
        }
        return (rects, grips);
    }

    private static double MaxDepth(BoardShape shape, double w, double h) => shape switch
    {
        BoardShape.U => Math.Min(w / 2, h),
        BoardShape.Ring => Math.Min(w, h) / 2,
        BoardShape.Z => Math.Min(w, h / 2),
        BoardShape.E => Math.Min(w, h / 4),
        BoardShape.G => Math.Min(w / 2, h / 5),
        _ => Math.Min(w, h),
    };

    /// <summary>Umrisse der Platte (beim Ring zwei: außen und die offene Mitte).</summary>
    public static IReadOnlyList<IReadOnlyList<(double X, double Y)>> Outlines(LayoutPlan p)
        => p.BoardShape == BoardShape.Module && p.Modules.Count > 0
            ? p.Modules.Select(m => (IReadOnlyList<(double X, double Y)>)[(m.X, m.Y), (m.X + m.Width, m.Y), (m.X + m.Width, m.Y + m.Depth), (m.X, m.Y + m.Depth)]).ToList()
            : Outlines(p.BoardShape, p.BoardWidth, p.BoardHeight, p.BoardDepth, p.BoardTurn, p.BoardLegLeft, p.BoardLegRight, p.BoardLegs);

    public static IReadOnlyList<IReadOnlyList<(double X, double Y)>> Outlines(BoardShape shape, double width, double height, double depth, int turn, double legLeft = 0, double legRight = 0, IReadOnlyList<BoardLeg>? legs = null)
        => Union(Rects(shape, width, height, depth, turn, legLeft, legRight, legs));

    /// <summary>
    /// Umriss einer Vereinigung achsparalleler Rechtecke: außen im Uhrzeigersinn (Bildschirm, y nach unten), Löcher gegenläufig,
    /// der größte Umriss zuerst.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<(double X, double Y)>> Union(IReadOnlyList<BoardRect> rects)
    {
        var good = rects.Where(r => r.X1 - r.X0 > 1e-6 && r.Y1 - r.Y0 > 1e-6).ToList();
        if (good.Count == 0) return [];
        var xs = good.SelectMany(r => new[] { r.X0, r.X1 }).Distinct().Order().ToArray();
        var ys = good.SelectMany(r => new[] { r.Y0, r.Y1 }).Distinct().Order().ToArray();
        int nx = xs.Length - 1, ny = ys.Length - 1;
        var full = new bool[nx, ny];
        for (var i = 0; i < nx; i++)
            for (var j = 0; j < ny; j++)
            {
                double cx = (xs[i] + xs[i + 1]) / 2, cy = (ys[j] + ys[j + 1]) / 2;
                full[i, j] = good.Any(r => cx > r.X0 && cx < r.X1 && cy > r.Y0 && cy < r.Y1);
            }
        bool Full(int i, int j) => i >= 0 && j >= 0 && i < nx && j < ny && full[i, j];
        // Kanten als Gitterpunkte (i, j); jede Kante mit der Fläche rechts davon
        var edges = new Dictionary<(int, int), List<(int, int)>>();
        void Edge((int, int) a, (int, int) b)
        {
            if (!edges.TryGetValue(a, out var l)) edges[a] = l = [];
            l.Add(b);
        }
        for (var i = 0; i < nx; i++)
            for (var j = 0; j < ny; j++)
            {
                if (!full[i, j]) continue;
                if (!Full(i, j - 1)) Edge((i, j), (i + 1, j));
                if (!Full(i + 1, j)) Edge((i + 1, j), (i + 1, j + 1));
                if (!Full(i, j + 1)) Edge((i + 1, j + 1), (i, j + 1));
                if (!Full(i - 1, j)) Edge((i, j + 1), (i, j));
            }
        var loops = new List<IReadOnlyList<(double X, double Y)>>();
        while (edges.Count > 0)
        {
            var start = edges.Keys.First();
            var pts = new List<(int I, int J)> { start };
            var cur = start;
            while (true)
            {
                var list = edges[cur];
                var next = list[^1];
                list.RemoveAt(list.Count - 1);
                if (list.Count == 0) edges.Remove(cur);
                if (next == start) break;
                pts.Add(next);
                cur = next;
                if (!edges.ContainsKey(cur)) break;
            }
            // Punkte auf geraden Strecken weglassen
            var corners = new List<(double X, double Y)>();
            for (var k = 0; k < pts.Count; k++)
            {
                var (a, b, c) = (pts[(k + pts.Count - 1) % pts.Count], pts[k], pts[(k + 1) % pts.Count]);
                if ((a.I == b.I && b.I == c.I) || (a.J == b.J && b.J == c.J)) continue;
                corners.Add((xs[b.I], ys[b.J]));
            }
            if (corners.Count >= 3) loops.Add(corners);
        }
        return loops.OrderByDescending(l => Math.Abs(l.Select((q, k) => q.X * l[(k + 1) % l.Count].Y - l[(k + 1) % l.Count].X * q.Y).Sum())).ToList();
    }

    /// <summary>Liegt der Punkt auf der Platte?</summary>
    public static bool Contains(LayoutPlan p, double x, double y, double tolerance = 0.5)
        => Rects(p).Any(r => r.Contains(x, y, tolerance));

    /// <summary>Kurzbeschreibung, z. B. „U-Form 3000 × 2000 mm, Schenkel 600 mm“.</summary>
    public static string Describe(LayoutPlan p)
    {
        var size = $"{Fmt.Num(p.BoardWidth, 0)} × {Fmt.Num(p.BoardHeight, 0)} mm";
        if (p.BoardShape == BoardShape.Module) return Lang.F($"{p.Modules.Count} Module, zusammen {size}");
        if (!HasLegs(p.BoardShape)) return size;
        var d = Depth(p.BoardShape, p.BoardWidth, p.BoardHeight, p.BoardDepth, p.BoardTurn);
        var text = Lang.F($"{Name(p.BoardShape).Split(' ')[0]} {size}, Schenkel {Fmt.Num(d, 0)} mm");
        if (p.BoardShape == BoardShape.U)
        {
            var full = p.BoardTurn % 2 == 0 ? p.BoardHeight : p.BoardWidth;
            double l1 = Leg(p.BoardLegLeft, d, full), l2 = Leg(p.BoardLegRight, d, full);
            if (Math.Abs(l1 - l2) > 0.5) text += Lang.F($" · Schenkellängen {Fmt.Num(l1, 0)} / {Fmt.Num(l2, 0)} mm");
        }
        return text;
    }

    /// <summary>Fläche der Platte in m².</summary>
    public static double Area(LayoutPlan p) => Rects(p).Sum(r => (r.X1 - r.X0) * (r.Y1 - r.Y0)) / 1e6;

    /// <summary>Punkt der Form in Grundlage (Breite <paramref name="w"/>, Tiefe <paramref name="h"/>) um Vierteldrehungen im Uhrzeigersinn drehen.</summary>
    private static (double X, double Y) Turn(double x, double y, double w, double h, int turn) => turn switch
    {
        1 => (h - y, x),
        2 => (w - x, h - y),
        3 => (y, w - x),
        _ => (x, y),
    };
}
