namespace TrackMaster.Core;

/// <summary>
/// Holzleisten des Unterbaus, frei gezogen (z. B. eine Diagonale unter versetzten Modulen und einzelne Streben dazu).
/// Eine Leiste ist ein <see cref="SceneryItem"/> der Art <see cref="SceneryKind.Beam"/>: Mittellinie von Punkt 0 zu Punkt 1,
/// Breite von oben (<see cref="SceneryItem.Width"/>), Höhe der Latte (<see cref="SceneryItem.Depth"/>) und Oberkante
/// (<see cref="SceneryItem.Height"/>, über der Plattenoberkante; unter der Platte negativ).
/// </summary>
public static class BeamLayout
{
    public const double MinSection = 5, MaxWidth = 200, MaxDepth = 300, MinTop = -3000, MaxTop = 1000, MinLength = 10;

    /// <summary>Querschnitt und Lage einer neuen Leiste: wie die Zarge des Unterbaus, direkt unter der Platte.</summary>
    public static (double Width, double Depth, double Top) Defaults(LayoutPlan plan)
    {
        var b = plan.Benchwork ?? BenchworkSpec.Defaults(plan.Gauge);
        return (b.FrameThickness, b.FrameHeight, -b.TopThickness);
    }

    public static double Length(SceneryItem s) => s.Points.Count == 2
        ? Math.Sqrt(Math.Pow(s.Points[1].X - s.Points[0].X, 2) + Math.Pow(s.Points[1].Y - s.Points[0].Y, 2)) : 0;

    /// <summary>Winkel der Leiste im Plan (Grad, 0 = nach rechts, im Uhrzeigersinn bei y nach unten).</summary>
    public static double Angle(SceneryItem s) => s.Points.Count == 2
        ? Angles.Normalize(Math.Atan2(s.Points[1].Y - s.Points[0].Y, s.Points[1].X - s.Points[0].X) * 180 / Math.PI) : 0;

    /// <summary>Querschnitt als Text, z. B. „18 × 70“.</summary>
    public static string Section(SceneryItem s) => $"{Fmt.Num(s.Width, 0)} × {Fmt.Num(s.Depth, 0)}";

    /// <summary>Umriss von oben: Rechteck um die Mittellinie.</summary>
    public static IReadOnlyList<(double X, double Y)> Outline(SceneryItem s)
    {
        if (s.Points.Count != 2) return [];
        var (a, b) = (s.Points[0], s.Points[1]);
        var len = Math.Max(1e-9, Length(s));
        double nx = -(b.Y - a.Y) / len * s.Width / 2, ny = (b.X - a.X) / len * s.Width / 2;
        return [(a.X + nx, a.Y + ny), (b.X + nx, b.Y + ny), (b.X - nx, b.Y - ny), (a.X - nx, a.Y - ny)];
    }

    public static bool Valid(SceneryItem s) => s.Points.Count == 2 && Length(s) >= MinLength
        && s.Width is >= MinSection and <= MaxWidth && s.Depth is >= MinSection and <= MaxDepth && s.Height is >= MinTop and <= MaxTop;

    /// <summary>Unbrauchbare Maße aus einer Datei auf zulässige Werte setzen.</summary>
    public static void Repair(SceneryItem s)
    {
        s.Width = double.IsFinite(s.Width) ? Math.Clamp(s.Width, MinSection, MaxWidth) : 18;
        s.Depth = double.IsFinite(s.Depth) ? Math.Clamp(s.Depth, MinSection, MaxDepth) : 70;
        s.Height = double.IsFinite(s.Height) ? Math.Clamp(s.Height, MinTop, MaxTop) : -10;
    }

    /// <summary>
    /// Fangpunkt für eine Leiste: Plattenecken, Enden anderer Leisten und – vom Anfang <paramref name="from"/> aus senkrecht
    /// oder waagrecht – der Schnitt mit einer anderen Leiste; sonst der nächste Punkt auf einer Plattenkante oder Leiste.
    /// So steht eine Strebe von einer Modulkante genau rechtwinklig auf der Diagonale.
    /// </summary>
    public static (double X, double Y) Snap(LayoutPlan plan, double x, double y, double tolerance, (double X, double Y)? from = null, int skipId = -1)
    {
        var beams = plan.Scenery.Where(s => s.IsBeam && s.Points.Count == 2 && s.Id != skipId).ToList();
        var points = BoardGeometry.Outlines(plan).SelectMany(o => o).Concat(beams.SelectMany(s => s.Points)).ToList();
        if (from is { } f)
            foreach (var s in beams)
            {
                var (a, b) = (s.Points[0], s.Points[1]);
                if (Cross(a, b, (f.X, f.Y), (f.X, f.Y + 1)) is { } v) points.Add(v);
                if (Cross(a, b, (f.X, f.Y), (f.X + 1, f.Y)) is { } h) points.Add(h);
            }
        if (Nearest(points, x, y, tolerance) is { } p) return Round(p);
        // Kanten: Plattenumriss und Mittellinien der Leisten
        var edges = new List<((double X, double Y) A, (double X, double Y) B)>();
        foreach (var o in BoardGeometry.Outlines(plan))
            for (int i = 0, j = o.Count - 1; i < o.Count; j = i++) edges.Add((o[j], o[i]));
        edges.AddRange(beams.Select(s => (s.Points[0], s.Points[1])));
        var foot = edges.Select(e => Foot(e.A, e.B, x, y)).ToList();
        if (Nearest(foot, x, y, tolerance) is { } q) return Round(q);
        // frei: vom Anfang aus waagrecht bzw. senkrecht, wenn fast
        if (from is { } g)
        {
            if (Math.Abs(x - g.X) < tolerance / 2) x = g.X;
            if (Math.Abs(y - g.Y) < tolerance / 2) y = g.Y;
        }
        return Round((x, y));
    }

    private static (double X, double Y) Round((double X, double Y) p) => (Math.Round(p.X, 1), Math.Round(p.Y, 1));

    private static (double X, double Y)? Nearest(IEnumerable<(double X, double Y)> pts, double x, double y, double tolerance)
    {
        (double X, double Y)? best = null;
        var bestD = tolerance;
        foreach (var p in pts)
        {
            var d = Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y));
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    /// <summary>Abstand eines Punkts von der Mittellinie der Leiste.</summary>
    public static double Distance(SceneryItem s, double x, double y)
    {
        if (s.Points.Count != 2) return double.MaxValue;
        var f = Foot(s.Points[0], s.Points[1], x, y);
        return Math.Sqrt((f.X - x) * (f.X - x) + (f.Y - y) * (f.Y - y));
    }

    /// <summary>Fußpunkt von (x, y) auf der Strecke a–b.</summary>
    private static (double X, double Y) Foot((double X, double Y) a, (double X, double Y) b, double x, double y)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
        var t = l2 < 1e-9 ? 0 : Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / l2, 0, 1);
        return (a.X + t * dx, a.Y + t * dy);
    }

    /// <summary>Schnitt der Strecke a–b mit der Geraden durch p und q (null, wenn er außerhalb der Strecke liegt).</summary>
    private static (double X, double Y)? Cross((double X, double Y) a, (double X, double Y) b, (double X, double Y) p, (double X, double Y) q)
    {
        double rx = b.X - a.X, ry = b.Y - a.Y, sx = q.X - p.X, sy = q.Y - p.Y;
        var den = rx * sy - ry * sx;
        if (Math.Abs(den) < 1e-9) return null;
        var t = ((p.X - a.X) * sy - (p.Y - a.Y) * sx) / den;
        return t is < -1e-9 or > 1 + 1e-9 ? null : (a.X + t * rx, a.Y + t * ry);
    }

    /// <summary>Alle Leisten als Teile für Zuschnitt und Materialliste des Unterbaus (Länge = Mittellinie, gerundet).</summary>
    public static IEnumerable<(string Section, double Length)> Cuts(LayoutPlan plan)
        => plan.Scenery.Where(s => s.IsBeam && s.Points.Count == 2).Select(s => (Section(s), Math.Round(Length(s))));
}

/// <summary>
/// Deckplatte, frei auf den Unterbau gelegt (z. B. auf einen offenen oder selbst gezeichneten Rahmen).
/// Eine Deckplatte ist ein <see cref="SceneryItem"/> der Art <see cref="SceneryKind.Plate"/>: linke obere Ecke
/// (<see cref="SceneryItem.X"/>, <see cref="SceneryItem.Y"/>), Breite und Tiefe (<see cref="SceneryItem.Width"/>,
/// <see cref="SceneryItem.Depth"/>), Oberkante (<see cref="SceneryItem.Height"/>) und Stärke in mm (<see cref="SceneryItem.Variant"/>).
/// </summary>
public static class PlateLayout
{
    public const double MinSize = 20, MaxSize = 20000;
    public const int MinThickness = 3, MaxThickness = 50;

    public static double Thickness(SceneryItem s) => Math.Clamp(s.Variant, MinThickness, MaxThickness);

    public static IReadOnlyList<(double X, double Y)> Outline(SceneryItem s)
        => [(s.X, s.Y), (s.X + s.Width, s.Y), (s.X + s.Width, s.Y + s.Depth), (s.X, s.Y + s.Depth)];

    public static string Size(SceneryItem s) => $"{Fmt.Num(s.Width, 0)} × {Fmt.Num(s.Depth, 0)}";

    public static bool Valid(SceneryItem s) => s.Width is >= MinSize and <= MaxSize && s.Depth is >= MinSize and <= MaxSize
        && s.Variant is >= MinThickness and <= MaxThickness && s.Height is >= BeamLayout.MinTop and <= BeamLayout.MaxTop
        && double.IsFinite(s.X) && double.IsFinite(s.Y);

    public static void Repair(SceneryItem s)
    {
        s.X = double.IsFinite(s.X) ? s.X : 0;
        s.Y = double.IsFinite(s.Y) ? s.Y : 0;
        s.Width = double.IsFinite(s.Width) ? Math.Clamp(s.Width, MinSize, MaxSize) : 600;
        s.Depth = double.IsFinite(s.Depth) ? Math.Clamp(s.Depth, MinSize, MaxSize) : 600;
        s.Variant = Math.Clamp(s.Variant, MinThickness, MaxThickness);
        s.Height = double.IsFinite(s.Height) ? Math.Clamp(s.Height, BeamLayout.MinTop, BeamLayout.MaxTop) : 0;
    }

    /// <summary>
    /// Neue Deckplatte nach den Maßen des Rahmens: über allen gezeichneten Leisten (außen gemessen) bzw. ohne Leisten über der Platte des Plans;
    /// sie liegt auf der höchsten Leiste bzw. auf dem Rahmen des Unterbaus. Stärke wie im Unterbau, sonst 10 mm.
    /// </summary>
    public static (double X, double Y, double Width, double Depth, int Thickness, double Top) FromFrame(LayoutPlan plan)
    {
        var bench = plan.Benchwork;
        var t = (int)Math.Round(bench is { TopThickness: >= MinThickness } ? Math.Min(bench.TopThickness, MaxThickness) : 10);
        var beams = plan.Scenery.Where(s => s.IsBeam && s.Points.Count == 2).ToList();
        double x0, y0, x1, y1, frameTop;
        if (beams.Count > 0)
        {
            var pts = beams.SelectMany(BeamLayout.Outline).ToList();
            (x0, y0, x1, y1) = (pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y));
            frameTop = beams.Max(b => b.Height);
        }
        else
        {
            var rects = BoardGeometry.Rects(plan);
            (x0, y0, x1, y1) = rects.Count > 0 ? (rects.Min(r => r.X0), rects.Min(r => r.Y0), rects.Max(r => r.X1), rects.Max(r => r.Y1)) : (0, 0, plan.BoardWidth, plan.BoardHeight);
            frameTop = -(bench?.TopThickness ?? t);
        }
        return (Math.Round(x0, 1), Math.Round(y0, 1), Math.Round(Math.Max(MinSize, x1 - x0), 1), Math.Round(Math.Max(MinSize, y1 - y0), 1), t, Math.Round(frameTop + t, 1));
    }
}
