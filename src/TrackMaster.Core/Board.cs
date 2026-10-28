namespace TrackMaster.Core;

/// <summary>Grundform der Anlagenplatte. Breite und Tiefe sind immer die äußeren Maße.</summary>
public enum BoardShape { Rechteck, L, U, T, Z, Ring }

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
        return shape == BoardShape.Rechteck
            ? new(g.Id, shape, g.DefaultBoardWidth, g.DefaultBoardHeight, depth, 0)
            : new(g.Id, shape, R(g.DefaultBoardWidth * 1.5), R(g.DefaultBoardHeight * 1.6), depth, 0);
    }
}

/// <summary>Rechteck in Plankoordinaten (mm).</summary>
public readonly record struct BoardRect(double X0, double Y0, double X1, double Y1)
{
    public bool Contains(double x, double y, double tolerance = 0)
        => x >= X0 - tolerance && x <= X1 + tolerance && y >= Y0 - tolerance && y <= Y1 + tolerance;
}

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
        _ => s.ToString(),
    };

    public static IReadOnlyList<BoardShape> All { get; } = Enum.GetValues<BoardShape>();

    /// <summary>Hat die Form Schenkel (dann sind Schenkeltiefe und Ausrichtung einstellbar)?</summary>
    public static bool HasLegs(BoardShape s) => s != BoardShape.Rechteck;

    /// <summary>Schenkeltiefe, auf sinnvolle Werte für Form und Maße begrenzt.</summary>
    public static double Depth(BoardShape shape, double width, double height, double depth, int turn)
    {
        var (w, h) = turn % 2 == 0 ? (width, height) : (height, width);
        var max = shape switch
        {
            BoardShape.U => Math.Min(w / 2, h),
            BoardShape.Ring => Math.Min(w, h) / 2,
            BoardShape.Z => Math.Min(w, h / 2),
            _ => Math.Min(w, h),
        };
        // Obergrenze knapp unter dem Maximum, Untergrenze nie darüber (auch bei sehr kleinen oder unfertig eingegebenen Maßen)
        var hi = Math.Max(0, max * 0.98);
        var lo = Math.Min(100, hi);
        return double.IsFinite(depth) ? Math.Clamp(depth, lo, hi) : hi;
    }

    /// <summary>Länge eines U-Schenkels: 0 = volle Tiefe, sonst zwischen Schenkeltiefe und Tiefe.</summary>
    public static double Leg(double leg, double depth, double fullLength)
        => leg <= 0 || !double.IsFinite(leg) ? fullLength : Math.Clamp(leg, Math.Min(depth, fullLength), fullLength);

    public static IReadOnlyList<BoardRect> Rects(LayoutPlan p)
        => Rects(p.BoardShape, p.BoardWidth, p.BoardHeight, p.BoardDepth, p.BoardTurn, p.BoardLegLeft, p.BoardLegRight);

    public static IReadOnlyList<BoardRect> Rects(BoardShape shape, double width, double height, double depth, int turn, double legLeft = 0, double legRight = 0)
    {
        turn = ((turn % 4) + 4) % 4;
        var (w, h) = turn % 2 == 0 ? (width, height) : (height, width);
        var d = Depth(shape, width, height, depth, turn);
        double l1 = Leg(legLeft, d, h), l2 = Leg(legRight, d, h);
        BoardRect[] local = shape switch
        {
            BoardShape.L => [new(0, 0, d, h), new(d, h - d, w, h)],
            BoardShape.U => [new(0, h - l1, d, h), new(w - d, h - l2, w, h), new(d, h - d, w - d, h)],
            BoardShape.T => [new(0, 0, w, d), new((w - d) / 2, d, (w + d) / 2, h)],
            BoardShape.Z => [new(0, 0, (w + d) / 2, d), new((w - d) / 2, d, (w + d) / 2, h - d), new((w - d) / 2, h - d, w, h)],
            BoardShape.Ring => [new(0, 0, w, d), new(0, h - d, w, h), new(0, d, d, h - d), new(w - d, d, w, h - d)],
            _ => [new(0, 0, w, h)],
        };
        return local.Select(r =>
        {
            var (ax, ay) = Turn(r.X0, r.Y0, w, h, turn);
            var (bx, by) = Turn(r.X1, r.Y1, w, h, turn);
            return new BoardRect(Math.Min(ax, bx), Math.Min(ay, by), Math.Max(ax, bx), Math.Max(ay, by));
        }).ToList();
    }

    /// <summary>Umrisse der Platte (beim Ring zwei: außen und die offene Mitte).</summary>
    public static IReadOnlyList<IReadOnlyList<(double X, double Y)>> Outlines(LayoutPlan p)
        => Outlines(p.BoardShape, p.BoardWidth, p.BoardHeight, p.BoardDepth, p.BoardTurn, p.BoardLegLeft, p.BoardLegRight);

    public static IReadOnlyList<IReadOnlyList<(double X, double Y)>> Outlines(BoardShape shape, double width, double height, double depth, int turn, double legLeft = 0, double legRight = 0)
    {
        turn = ((turn % 4) + 4) % 4;
        var (w, h) = turn % 2 == 0 ? (width, height) : (height, width);
        var d = Depth(shape, width, height, depth, turn);
        double l1 = Leg(legLeft, d, h), l2 = Leg(legRight, d, h);
        (double, double)[][] local = shape switch
        {
            BoardShape.L => [[(0, 0), (d, 0), (d, h - d), (w, h - d), (w, h), (0, h)]],
            BoardShape.U => [[(0, h - l1), (d, h - l1), (d, h - d), (w - d, h - d), (w - d, h - l2), (w, h - l2), (w, h), (0, h)]],
            BoardShape.T => [[(0, 0), (w, 0), (w, d), ((w + d) / 2, d), ((w + d) / 2, h), ((w - d) / 2, h), ((w - d) / 2, d), (0, d)]],
            BoardShape.Z => [[(0, 0), ((w + d) / 2, 0), ((w + d) / 2, h - d), (w, h - d), (w, h), ((w - d) / 2, h), ((w - d) / 2, d), (0, d)]],
            BoardShape.Ring => [[(0, 0), (w, 0), (w, h), (0, h)], [(d, d), (w - d, d), (w - d, h - d), (d, h - d)]],
            _ => [[(0, 0), (w, 0), (w, h), (0, h)]],
        };
        return local.Select(poly => (IReadOnlyList<(double X, double Y)>)poly.Select(q => Turn(q.Item1, q.Item2, w, h, turn)).ToList()).ToList();
    }

    /// <summary>Liegt der Punkt auf der Platte?</summary>
    public static bool Contains(LayoutPlan p, double x, double y, double tolerance = 0.5)
        => Rects(p).Any(r => r.Contains(x, y, tolerance));

    /// <summary>Kurzbeschreibung, z. B. „U-Form 3000 × 2000 mm, Schenkel 600 mm“.</summary>
    public static string Describe(LayoutPlan p)
    {
        var size = $"{Fmt.Num(p.BoardWidth, 0)} × {Fmt.Num(p.BoardHeight, 0)} mm";
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
