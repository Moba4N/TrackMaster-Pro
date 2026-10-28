namespace TrackMaster.Core;

/// <summary>Maßlinie in Weltkoordinaten: Maßlinie A–B (um den Abstand versetzt), Hilfslinien von den Messpunkten und Beschriftung.</summary>
public sealed record DimensionShape((double X, double Y) P1, (double X, double Y) P2, (double X, double Y) A, (double X, double Y) B,
                                    (double X, double Y) TextAt, double TextAngle, double Length, string Text, double TextSize);

/// <summary>
/// Beschriftungen und Maßlinien im Plan. Beide sind <see cref="SceneryItem"/>: Beschriftung mit Mittelpunkt (X, Y), Text im Namen,
/// Schrifthöhe in <see cref="SceneryItem.Height"/> und Drehung; Maßlinie mit zwei Messpunkten und dem Abstand der Maßlinie in Width.
/// </summary>
public static class Annotation
{
    /// <summary>Schrifthöhe in mm, passend zur Spur (im verkleinerten Druck gut lesbar).</summary>
    public static double DefaultTextSize(Gauge gauge) => Math.Round(Math.Clamp(gauge.TrackWidth * 1.6, 12, 60));

    /// <summary>Ungefähre Textbreite: 0,58 × Schrifthöhe je Zeichen.</summary>
    public static double TextWidth(string text, double size) => Math.Max(1, text.Length) * size * 0.58;

    public static IReadOnlyList<(double X, double Y)> LabelOutline(SceneryItem s)
    {
        var r = s.Rotation * Math.PI / 180;
        double c = Math.Cos(r), n = Math.Sin(r), hw = TextWidth(s.Name, s.Height) / 2 + s.Height * 0.2, hh = s.Height * 0.7;
        return new[] { (-hw, -hh), (hw, -hh), (hw, hh), (-hw, hh) }.Select(p => (s.X + p.Item1 * c - p.Item2 * n, s.Y + p.Item1 * n + p.Item2 * c)).ToList();
    }

    public static DimensionShape Dimension(SceneryItem s, double textSize)
    {
        var (p1, p2) = (s.Points[0], s.Points[1]);
        double dx = p2.X - p1.X, dy = p2.Y - p1.Y, len = Math.Sqrt(dx * dx + dy * dy);
        double ux = len < 1e-9 ? 1 : dx / len, uy = len < 1e-9 ? 0 : dy / len;
        double nx = -uy, ny = ux, off = s.Width;
        var a = (p1.X + nx * off, p1.Y + ny * off);
        var b = (p2.X + nx * off, p2.Y + ny * off);
        // Text über der Maßlinie, immer von unten bzw. rechts lesbar
        var angle = Math.Atan2(dy, dx) * 180 / Math.PI;
        var side = 1.0;
        if (angle > 90.01 || angle <= -90) { angle = Angles.Normalize(angle + 180); side = -1; }
        var lift = textSize * 0.75 * side;
        var text = (a.Item1 + b.Item1) / 2 + nx * -lift;
        var texty = (a.Item2 + b.Item2) / 2 + ny * -lift;
        var label = string.IsNullOrWhiteSpace(s.Name) || s.Name.StartsWith("Maß", StringComparison.Ordinal) || s.Name.StartsWith(Lang.T("Maß"), StringComparison.Ordinal) ? $"{Fmt.Num(len, 0)} mm" : $"{s.Name}: {Fmt.Num(len, 0)} mm";
        return new DimensionShape(p1, p2, a, b, (text, texty), angle, len, label, textSize);
    }

    public static IReadOnlyList<(double X, double Y)> DimensionOutline(SceneryItem s)
    {
        if (s.Points.Count < 2) return [(s.X, s.Y), (s.X + 1, s.Y), (s.X + 1, s.Y + 1)];
        var d = Dimension(s, s.Height > 0 ? s.Height : 12);
        double dx = d.B.X - d.A.X, dy = d.B.Y - d.A.Y, len = Math.Max(Math.Sqrt(dx * dx + dy * dy), 1e-9);
        double nx = -dy / len * d.TextSize * 1.4, ny = dx / len * d.TextSize * 1.4;
        return [(d.A.X - nx, d.A.Y - ny), (d.B.X - nx, d.B.Y - ny), (d.B.X + nx, d.B.Y + ny), (d.A.X + nx, d.A.Y + ny)];
    }

    /// <summary>Fangpunkt für Maßlinien: nächstes Gleisende oder Plattenecke im Umkreis <paramref name="tolerance"/>, sonst der Punkt selbst.</summary>
    public static (double X, double Y) Snap(LayoutPlan plan, double x, double y, double tolerance)
    {
        var candidates = plan.Pieces.SelectMany(p => p.WorldConnectors()).Select(c => (c.X, c.Y))
            .Concat(BoardGeometry.Outlines(plan).SelectMany(o => o));
        var best = (x, y);
        var bestD = tolerance;
        foreach (var (cx, cy) in candidates)
        {
            var d = Math.Sqrt((cx - x) * (cx - x) + (cy - y) * (cy - y));
            if (d < bestD) { bestD = d; best = (cx, cy); }
        }
        return (Math.Round(best.Item1, 1), Math.Round(best.Item2, 1));
    }
}
