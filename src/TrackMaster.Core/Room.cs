namespace TrackMaster.Core;

/// <summary>
/// Raum um die Anlage: Wände als Vieleck (Innenkante, <see cref="SceneryItem.Width"/> = Wandstärke) und Raumelemente
/// wie Türen, Fenster, Säulen und Heizkörper als Rechtecke. Die Planprüfung meldet Platten außerhalb des Raums,
/// zugestellte Türen und zu schmale Gänge.
/// </summary>
public static class RoomPlan
{
    public const double DefaultWall = 120;
    /// <summary>Gänge zwischen Platte und Wand ab dieser Breite gelten als begehbar, darunter als zu eng.</summary>
    public const double MinAisle = 600;
    /// <summary>Spalten unter dieser Breite gelten als „Platte steht an der Wand“, nicht als Gang.</summary>
    public const double WallGap = 150;

    /// <summary>Rechteckiger Raum mit der Innenecke links oben bei (x, y).</summary>
    public static SceneryItem Rectangle(double x, double y, double width, double depth, double wall = DefaultWall)
        => new()
        {
            Kind = SceneryKind.Room, Name = Lang.T("Raum"), Width = wall, Height = 2500,
            Points = [(x, y), (x + width, y), (x + width, y + depth), (x, y + depth)],
        };

    /// <summary>Raumelement mit üblichen Maßen um (x, y).</summary>
    public static SceneryItem Element(ObstacleType type, double x, double y)
    {
        var (w, d, h) = SceneryItem.ObstacleSize(type);
        return new SceneryItem { Kind = SceneryKind.Obstacle, Variant = (int)type, X = x, Y = y, Width = w, Depth = d, Height = h, Name = SceneryItem.ObstacleName(type) };
    }

    /// <summary>Abstand eines Punkts zu einer Strecke.</summary>
    public static double Distance((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
        var t = l2 < 1e-9 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2, 0, 1);
        double x = a.X + t * dx - p.X, y = a.Y + t * dy - p.Y;
        return Math.Sqrt(x * x + y * y);
    }

    /// <summary>Liegt der Punkt höchstens <paramref name="tolerance"/> von einer Wand des Raums entfernt (zum Anklicken)?</summary>
    public static bool HitsWall(SceneryItem room, double x, double y, double tolerance)
    {
        var pts = room.Points;
        for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
            if (Distance((x, y), pts[j], pts[i]) <= Math.Max(tolerance, room.Width / 2 + tolerance)) return true;
        return false;
    }

    private static bool SegmentsCross((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d)
    {
        static double Cross((double X, double Y) o, (double X, double Y) p, (double X, double Y) q) => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);
        double d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    /// <summary>Überschneiden sich zwei Vielecke (eine Ecke im anderen oder Kanten kreuzen sich)?</summary>
    public static bool Overlaps(IReadOnlyList<(double X, double Y)> a, IReadOnlyList<(double X, double Y)> b)
    {
        if (a.Any(p => Geometry2D.PointInPolygon(b, p.X, p.Y)) || b.Any(p => Geometry2D.PointInPolygon(a, p.X, p.Y))) return true;
        for (int i = 0, j = a.Count - 1; i < a.Count; j = i++)
            for (int k = 0, l = b.Count - 1; k < b.Count; l = k++)
                if (SegmentsCross(a[j], a[i], b[l], b[k])) return true;
        return false;
    }

    /// <summary>Befunde zu Raum und Raumelementen für die Planprüfung.</summary>
    public static IEnumerable<PlanIssue> Check(LayoutPlan plan)
    {
        var boards = BoardGeometry.Outlines(plan);
        if (boards.Count == 0) yield break;
        foreach (var room in plan.Scenery.Where(s => s.Kind == SceneryKind.Room && s.Points.Count >= 3))
        {
            var outside = boards.SelectMany(o => o).Where(p => !Geometry2D.PointInPolygon(room.Points, p.X, p.Y)
                && !room.Points.Select((q, i) => Distance(p, q, room.Points[(i + 1) % room.Points.Count])).Any(d => d < 1)).ToList();
            if (outside.Count > 0)
            {
                yield return new PlanIssue(IssueLevel.Fehler, Lang.T("Raum"), Lang.F($"Die Anlagenplatte ragt über die Wände von „{room.Name}“ hinaus."), [], outside[0].X, outside[0].Y);
                continue;
            }
            // Gänge: je Wand der kleinste Abstand zur Platte
            var pts = room.Points;
            for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
            {
                var near = boards.SelectMany(o => o).Select(p => (P: p, D: Distance(p, pts[j], pts[i]))).MinBy(x => x.D);
                if (near.D > WallGap && near.D < MinAisle - 0.5)
                    yield return new PlanIssue(IssueLevel.Hinweis, Lang.T("Gang"),
                        Lang.F($"Zwischen Platte und Wand bleiben nur {Fmt.Num(near.D / 10, 0)} cm – zum Bauen und Bedienen sind {Fmt.Num(MinAisle / 10, 0)} cm angenehm."), [], near.P.X, near.P.Y);
            }
        }
        foreach (var o in plan.Scenery.Where(s => s.Kind == SceneryKind.Obstacle && !SceneryItem.BoardMayCover(s.Obstacle)))
        {
            var outline = o.Outline();
            if (!boards.Any(b => Overlaps(outline, b))) continue;
            var (cx, cy) = o.Center();
            yield return new PlanIssue(o.Obstacle == ObstacleType.Tuer ? IssueLevel.Fehler : IssueLevel.Warnung, Lang.T("Raum"),
                o.Obstacle == ObstacleType.Tuer
                    ? Lang.F($"Die Tür „{o.Name}“ lässt sich nicht öffnen: Ihr Schwenkbereich liegt unter der Anlagenplatte.")
                    : Lang.F($"„{o.Name}“ ({SceneryItem.ObstacleName(o.Obstacle)}) liegt unter bzw. an der Anlagenplatte."), [], cx, cy);
        }
    }
}

/// <summary>Breite und Höhe eines PNG-, JPEG-, GIF- oder WebP-Bildes aus dem Dateikopf.</summary>
public static class ImageInfo
{
    public static (int Width, int Height, string Mime)? Read(ReadOnlySpan<byte> b)
    {
        if (b.Length > 24 && b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G')
            return (BigEndian(b, 16), BigEndian(b, 20), "image/png");
        if (b.Length > 10 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F')
            return (b[6] | b[7] << 8, b[8] | b[9] << 8, "image/gif");
        if (b.Length > 30 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P')
        {
            if (b[12] == 'V' && b[13] == 'P' && b[14] == '8' && b[15] == ' ') return ((b[26] | b[27] << 8) & 0x3FFF, (b[28] | b[29] << 8) & 0x3FFF, "image/webp");
            if (b[12] == 'V' && b[13] == 'P' && b[14] == '8' && b[15] == 'L')
            {
                var bits = b[21] | b[22] << 8 | b[23] << 16 | b[24] << 24;
                return ((bits & 0x3FFF) + 1, ((bits >> 14) & 0x3FFF) + 1, "image/webp");
            }
            if (b[12] == 'V' && b[13] == 'P' && b[14] == '8' && b[15] == 'X')
                return (1 + (b[24] | b[25] << 8 | b[26] << 16), 1 + (b[27] | b[28] << 8 | b[29] << 16), "image/webp");
        }
        if (b.Length > 4 && b[0] == 0xFF && b[1] == 0xD8)
        {
            var i = 2;
            while (i + 9 < b.Length)
            {
                if (b[i] != 0xFF) { i++; continue; }
                var marker = b[i + 1];
                if (marker is 0xD8 or 0x01 || marker is >= 0xD0 and <= 0xD7) { i += 2; continue; }
                var len = b[i + 2] << 8 | b[i + 3];
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                    return (b[i + 7] << 8 | b[i + 8], b[i + 5] << 8 | b[i + 6], "image/jpeg");
                i += 2 + len;
            }
        }
        return null;
    }

    private static int BigEndian(ReadOnlySpan<byte> b, int i) => b[i] << 24 | b[i + 1] << 16 | b[i + 2] << 8 | b[i + 3];

    /// <summary>Hintergrundbild aus Dateiinhalt; Breite in mm vorerst so, dass es die Platte überdeckt. Null bei unbekanntem Format.</summary>
    public static PlanBackground? Background(byte[] bytes, LayoutPlan plan)
    {
        if (Read(bytes) is not { Width: > 0, Height: > 0 } info) return null;
        return new PlanBackground
        {
            Data = Convert.ToBase64String(bytes), Mime = info.Mime, PixelWidth = info.Width, PixelHeight = info.Height,
            X = 0, Y = 0, Width = plan.BoardWidth, Opacity = 0.5,
        };
    }
}
