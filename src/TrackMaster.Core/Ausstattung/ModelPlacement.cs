namespace TrackMaster.Core;

/// <summary>Ergebnis beim Einfügen eines Modells: das neue Objekt und ein Hinweis für die Statuszeile.</summary>
public sealed record ModelPlacementResult(SceneryItem Item, string Message, bool Snapped);

/// <summary>
/// Setzt ein Ausstattungsmodell in den Plan: in Spur-Maßstab umgerechnet und – je nach <see cref="ModelSnap"/> –
/// am nächsten Gleis eingerastet (Tunnelportal am Tunnelanfang, Bahnsteig im Kantenabstand entlang des Gleises, Brücke darunter …).
/// </summary>
public static class ModelPlacement
{
    /// <summary>Abstand Gleismitte – Bahnsteigkante im Vorbild (m).</summary>
    public const double PlatformEdge = 1.65;

    /// <summary>Neues Objekt in Standardgröße an (x, y), ohne Gleisbezug.</summary>
    public static SceneryItem Create(LayoutPlan plan, ModelDefinition def, double x, double y)
    {
        var k = ModelLibrary.MmPerMeter(plan.Gauge);
        var (w, d, h) = def.Size;
        return new SceneryItem
        {
            Kind = SceneryKind.Building, Model = def.Id, Name = def.Name, X = Math.Round(x), Y = Math.Round(y),
            Width = Round(w * k), Depth = Round(d * k), Height = Round(h * k), Level = Math.Clamp(plan.ActiveLevel, 0, plan.Levels.Count - 1),
        };
    }

    private static double Round(double v) => Math.Max(0.5, Math.Round(v, v < 20 ? 1 : 0));

    /// <summary>Setzt das Modell an (x, y) und rastet es, wenn möglich, im Umkreis <paramref name="tolerance"/> (mm) am Gleis ein.</summary>
    public static ModelPlacementResult Place(LayoutPlan plan, ModelDefinition def, double x, double y, double tolerance)
    {
        var item = Create(plan, def, x, y);
        var g = plan.Gauge.TrackWidth;
        var k = ModelLibrary.MmPerMeter(plan.Gauge);
        var pieces = plan.Pieces.Where(p => p.Level == item.Level || plan.Levels.Count == 1).ToList();
        switch (def.Snap)
        {
            case ModelSnap.Tunnelportal:
            {
                if (Portal(plan, pieces, x, y, tolerance + item.Depth) is not { } c) break;
                var dir = c.Heading * Math.PI / 180;
                // Die Außenseite der Portalmauer steht am Tunnelanfang, das Modell reicht in den Tunnel hinein (+x)
                var bounds = def.Bounds();
                var wall = def.Bodies.FirstOrDefault(o => o.Shape == BodyShape.Bogenwand);
                var face = wall is null ? bounds.X0 : ModelMesh.Footprint(wall).Min(q => q.X);
                var shift = ((bounds.X0 + bounds.X1) / 2 - face) * item.Width / (bounds.X1 - bounds.X0);
                item.X = Math.Round(c.X + Math.Cos(dir) * shift, 2);
                item.Y = Math.Round(c.Y + Math.Sin(dir) * shift, 2);
                item.Rotation = Math.Round(Angles.Normalize(c.Heading), 2);
                return new(item, Lang.F($"{def.Name} am Tunnelanfang eingerastet."), true);
            }
            case ModelSnap.Gleisende:
            {
                var conn = Connectivity.Analyze(plan);
                var open = conn.Open.Where(o => pieces.Any(p => p.Id == o.PieceId))
                    .Select(o => (o.Pose, D: Math.Sqrt((o.Pose.X - x) * (o.Pose.X - x) + (o.Pose.Y - y) * (o.Pose.Y - y))))
                    .Where(o => o.D <= tolerance + item.Width).OrderBy(o => o.D).ToList();
                if (open.Count == 0) break;
                var pose = open[0].Pose;
                var dir = pose.Heading * Math.PI / 180;
                item.X = Math.Round(pose.X + Math.Cos(dir) * item.Width / 2, 2);
                item.Y = Math.Round(pose.Y + Math.Sin(dir) * item.Width / 2, 2);
                item.Rotation = Math.Round(Angles.Normalize(pose.Heading), 2);
                return new(item, Lang.F($"{def.Name} am Gleisende eingerastet."), true);
            }
            case ModelSnap.Neben:
            {
                if (Nearest(pieces, x, y, tolerance + item.Depth + PlatformEdge * k) is not { } hit) break;
                var edge = Math.Max(PlatformEdge * k, g * 1.15);
                // Seite: auf der Seite des Klicks
                var (nx, ny) = (-Math.Sin(hit.Pose.HeadingRad), Math.Cos(hit.Pose.HeadingRad));
                var side = (x - hit.Pose.X) * nx + (y - hit.Pose.Y) * ny >= 0 ? 1 : -1;
                var offset = side * (edge + item.Depth / 2);
                var line = Follow(pieces, hit.Pose, item.Width, g);
                var path = Offset(line, offset);
                // Modell-y (von der Gleisseite weg) muss zur Klickseite zeigen: sonst Richtung umkehren
                if (side < 0) path.Reverse();
                var length = PlacedModel.PathLength(path);
                var mid = PointAt(path, length / 2);
                item.X = Math.Round(mid.X, 2); item.Y = Math.Round(mid.Y, 2);
                item.Rotation = Math.Round(Angles.Normalize(mid.Heading), 2);
                item.Width = Math.Round(length, 1);
                if (Curved(path)) item.Points = path.Select(p => (Math.Round(p.X, 2), Math.Round(p.Y, 2))).ToList();
                return new(item, Lang.F($"{def.Name} neben dem Gleis eingerastet ({Fmt.Num(edge, 1)} mm Kantenabstand)."), true);
            }
            case ModelSnap.Unter:
            case ModelSnap.Auf:
            case ModelSnap.Quer:
            {
                if (Nearest(pieces, x, y, tolerance + Math.Max(item.Depth, g * 4)) is not { } hit) break;
                item.X = Math.Round(hit.Pose.X, 2); item.Y = Math.Round(hit.Pose.Y, 2);
                item.Rotation = Math.Round(Angles.Normalize(hit.Pose.Heading), 2);
                if (def.Snap == ModelSnap.Unter)
                {
                    var ground = TerrainField.GroundAt(plan, item.X, item.Y);
                    var top = hit.Z - ground;
                    if (top > g * 1.5) item.Height = Math.Round(top, 1);
                    return new(item, top > g * 1.5 ? Lang.F($"{def.Name} unter dem Gleis eingerastet (Höhe {Fmt.Num(item.Height, 0)} mm).")
                        : Lang.F($"{def.Name} unter dem Gleis eingerastet. Das Gleis liegt flach – für die Brücke Gleis höher legen."), true);
                }
                return new(item, Lang.F($"{def.Name} am Gleis eingerastet."), true);
            }
        }
        return new(item, def.Snap == ModelSnap.Frei ? Lang.F($"{def.Name} gesetzt. Größe, Farbe und Drehung stehen in den Eigenschaften.")
            : Lang.F($"{def.Name} gesetzt – kein passendes Gleis in der Nähe zum Einrasten."), false);
    }

    /// <summary>Nächster Punkt auf der Gleismitte im Umkreis.</summary>
    public sealed record TrackHit(PlacedPiece Piece, Pose Pose, double Distance, double Z);

    public static TrackHit? Nearest(IEnumerable<PlacedPiece> pieces, double x, double y, double tolerance)
    {
        TrackHit? best = null;
        foreach (var p in pieces)
        {
            if (p.Def.IsTable) continue;
            var legs = p.WorldLegs();
            for (var li = 0; li < legs.Count; li++)
            {
                var (t, pose, d) = Project(legs[li], x, y);
                if (d <= tolerance && (best is null || d < best.Distance)) best = new TrackHit(p, pose, d, p.HeightAt(li, t));
            }
        }
        return best;
    }

    /// <summary>Lotfußpunkt auf einem Abschnitt: Anteil t, Lage mit Richtung und Abstand.</summary>
    public static (double T, Pose Pose, double Distance) Project(Leg l, double x, double y)
    {
        double t;
        if (!l.IsArc)
        {
            var e = l.End;
            double dx = e.X - l.X, dy = e.Y - l.Y, len2 = dx * dx + dy * dy;
            t = len2 < 1e-12 ? 0 : Math.Clamp(((x - l.X) * dx + (y - l.Y) * dy) / len2, 0, 1);
        }
        else
        {
            var (cx, cy) = l.Center;
            var phi = Math.Atan2(y - cy, x - cx) * 180 / Math.PI;
            var along = Angles.Normalize((phi + l.Turn * 90 - l.Heading) * l.Turn);
            // außerhalb des Bogens: zum näheren Ende
            if (along < 0 || along > l.Angle)
                t = Math.Abs(Angles.Normalize(along - l.Angle)) < Math.Abs(along) ? 1 : 0;
            else t = l.Angle < 1e-9 ? 0 : along / l.Angle;
        }
        var p = l.At(t);
        return (t, p, Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y)));
    }

    /// <summary>
    /// Gleismitte um einen Punkt herum über <paramref name="length"/> (je die Hälfte vor und zurück), auch über mehrere Gleise
    /// und Bögen hinweg; endet das Gleis, wird gerade weitergeführt.
    /// </summary>
    public static List<Pose> Follow(IReadOnlyList<PlacedPiece> pieces, Pose start, double length, double gauge)
    {
        var step = Math.Clamp(length / 60, 0.5, Math.Max(gauge * 2, 1));
        List<Pose> Walk(double heading)
        {
            var list = new List<Pose>();
            var cur = start with { Heading = heading };
            var n = (int)Math.Ceiling(length / 2 / step);
            for (var i = 0; i < n; i++)
            {
                var h = cur.HeadingRad;
                var next = new Pose(cur.X + Math.Cos(h) * step, cur.Y + Math.Sin(h) * step, cur.Heading);
                // nur vorwärts dem Gleis folgen; am Gleisende geht es gerade weiter
                if (Nearest(pieces, next.X, next.Y, step * 1.5) is { } hit
                    && (hit.Pose.X - cur.X) * Math.Cos(h) + (hit.Pose.Y - cur.Y) * Math.Sin(h) > step * 0.5)
                {
                    var hh = hit.Pose.Heading;
                    if (Math.Abs(Angles.Normalize(hh - cur.Heading)) > 90) hh += 180;
                    next = new Pose(hit.Pose.X, hit.Pose.Y, hh);
                }
                list.Add(next);
                cur = next;
            }
            return list;
        }
        var back = Walk(start.Heading + 180);
        back.Reverse();
        var result = back.Select(p => p with { Heading = p.Heading + 180 }).ToList();
        result.Add(start);
        result.AddRange(Walk(start.Heading));
        return result;
    }

    /// <summary>Linie seitlich versetzt (positiv = rechts in Fahrtrichtung bei y nach unten).</summary>
    private static List<(double X, double Y)> Offset(List<Pose> line, double offset)
        => line.Select(p => (p.X - Math.Sin(p.HeadingRad) * offset, p.Y + Math.Cos(p.HeadingRad) * offset)).ToList();

    private static Pose PointAt(List<(double X, double Y)> path, double s)
    {
        double acc = 0;
        for (var i = 1; i < path.Count; i++)
        {
            var (a, b) = (path[i - 1], path[i]);
            var l = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            if (acc + l >= s || i == path.Count - 1)
            {
                var t = l < 1e-9 ? 0 : Math.Clamp((s - acc) / l, 0, 1);
                return new Pose(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI);
            }
            acc += l;
        }
        return new Pose(path[0].X, path[0].Y, 0);
    }

    private static bool Curved(List<(double X, double Y)> path)
    {
        if (path.Count < 3) return false;
        var h0 = Math.Atan2(path[1].Y - path[0].Y, path[1].X - path[0].X) * 180 / Math.PI;
        for (var i = 2; i < path.Count; i++)
        {
            var h = Math.Atan2(path[i].Y - path[i - 1].Y, path[i].X - path[i - 1].X) * 180 / Math.PI;
            if (Math.Abs(Angles.Normalize(h - h0)) > 0.5) return true;
        }
        return false;
    }

    /// <summary>
    /// Tunnelanfang nahe (x, y): Anschluss zwischen Tunnelgleis und offenem Gleis (oder offenes Ende eines Tunnelgleises).
    /// Ohne Tunnel: nächstes Gleisende. Die Richtung zeigt in den Tunnel hinein.
    /// </summary>
    private static Pose? Portal(LayoutPlan plan, List<PlacedPiece> pieces, double x, double y, double tolerance)
    {
        var conn = Connectivity.Analyze(plan);
        var ids = pieces.Select(p => p.Id).ToHashSet();
        Pose? best = null;
        var bestD = double.MaxValue;
        var bestScore = int.MaxValue;
        foreach (var c in conn.All.Where(c => ids.Contains(c.PieceId)))
        {
            var d = Math.Sqrt((c.Pose.X - x) * (c.Pose.X - x) + (c.Pose.Y - y) * (c.Pose.Y - y));
            if (d > tolerance) continue;
            var self = plan.Find(c.PieceId)!;
            var other = c.Link is { } l ? plan.Find(l.PieceId) : null;
            int score;
            Pose pose;
            if (self.Tunnel && other is not { Tunnel: true }) { score = 0; pose = c.Pose with { Heading = c.Pose.Heading + 180 }; }
            else if (!self.Tunnel && other is { Tunnel: true }) { score = 0; pose = c.Pose; }
            else if (c.IsOpen) { score = 1; pose = c.Pose; }
            else continue;
            if (score < bestScore || (score == bestScore && d < bestD)) { best = pose; bestD = d; bestScore = score; }
        }
        return best;
    }
}
