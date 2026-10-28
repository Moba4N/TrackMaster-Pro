namespace TrackMaster.Core;

/// <summary>Punkt mit Fahrtrichtung. Koordinaten in mm, y nach unten; Richtung in Grad, 0 = nach rechts, positiv im Uhrzeigersinn.</summary>
public readonly record struct Pose(double X, double Y, double Heading)
{
    public double HeadingRad => Heading * Math.PI / 180;
}

/// <summary>
/// Ein Abschnitt der Gleismitte: gerade (Länge) oder Bogen (Radius, Winkel, Drehsinn Turn = +1 rechts / −1 links).
/// <paramref name="From"/> ist der Weg von Anschluss 0 bis zum Anfang des Abschnitts (bei Weichensträngen aus mehreren Abschnitten).
/// </summary>
public sealed record Leg(double X, double Y, double Heading, bool IsArc, double Length, double Radius = 0, double Angle = 0, int Turn = 1, double From = 0)
{
    public Pose Start => new(X, Y, Heading);
    public Pose End => At(1);

    /// <summary>Punkt bei Anteil t (0…1) der Strecke.</summary>
    public Pose At(double t)
    {
        var h = Heading * Math.PI / 180;
        if (!IsArc)
            return new Pose(X + Length * t * Math.Cos(h), Y + Length * t * Math.Sin(h), Heading);
        var (cx, cy) = Center;
        var he = Heading + Turn * Angle * t;
        var h2 = he * Math.PI / 180;
        return new Pose(cx + Turn * Radius * Math.Sin(h2), cy - Turn * Radius * Math.Cos(h2), he);
    }

    public (double X, double Y) Center
    {
        get
        {
            var h = Heading * Math.PI / 180;
            return (X - Turn * Radius * Math.Sin(h), Y + Turn * Radius * Math.Cos(h));
        }
    }

    public double TrackLength => IsArc ? Radius * Angle * Math.PI / 180 : Length;

    /// <summary>Anfang, Ende und Radius der um <paramref name="offset"/> seitlich versetzten Linie (für die Schienen).</summary>
    public (double X0, double Y0, double X1, double Y1, double Radius) Offset(double offset)
    {
        var a = Start;
        var e = End;
        var x0 = a.X - offset * Math.Sin(a.HeadingRad);
        var y0 = a.Y + offset * Math.Cos(a.HeadingRad);
        var x1 = e.X - offset * Math.Sin(e.HeadingRad);
        var y1 = e.Y + offset * Math.Cos(e.HeadingRad);
        return (x0, y0, x1, y1, IsArc ? Radius - Turn * offset : 0);
    }

    /// <summary>Kürzester Abstand eines Punkts zur Gleismitte dieses Abschnitts.</summary>
    public double DistanceTo(double px, double py)
    {
        if (!IsArc)
        {
            var e = End;
            double dx = e.X - X, dy = e.Y - Y;
            var len2 = dx * dx + dy * dy;
            var t = len2 == 0 ? 0 : Math.Clamp(((px - X) * dx + (py - Y) * dy) / len2, 0, 1);
            return Math.Sqrt(Math.Pow(px - (X + t * dx), 2) + Math.Pow(py - (Y + t * dy), 2));
        }
        var (cx, cy) = Center;
        // Winkel des Punkts auf dem Kreis, umgerechnet in eine Fahrtrichtung, und prüfen ob er im Bogen liegt
        var phi = Math.Atan2(py - cy, px - cx) * 180 / Math.PI;
        var headingAtPoint = phi + Turn * 90;
        var along = Angles.Normalize((headingAtPoint - Heading) * Turn);
        if (along >= -0.001 && along <= Angle + 0.001)
            return Math.Abs(Math.Sqrt(Math.Pow(px - cx, 2) + Math.Pow(py - cy, 2)) - Radius);
        var s = Start; var en = End;
        return Math.Min(Math.Sqrt(Math.Pow(px - s.X, 2) + Math.Pow(py - s.Y, 2)), Math.Sqrt(Math.Pow(px - en.X, 2) + Math.Pow(py - en.Y, 2)));
    }

    public Leg Transform(double ox, double oy, double rotation)
    {
        var r = rotation * Math.PI / 180;
        double c = Math.Cos(r), s = Math.Sin(r);
        return this with { X = ox + X * c - Y * s, Y = oy + X * s + Y * c, Heading = Heading + rotation };
    }
}

public static class Angles
{
    /// <summary>Winkel auf −180…180° bringen.</summary>
    public static double Normalize(double a) => ((a % 360) + 540) % 360 - 180;
}

public static class TrackGeometry
{
    /// <summary>Abschnitte eines Gleisstücks in eigenen Koordinaten: Anschluss 0 liegt im Ursprung, Fahrtrichtung nach rechts.
    /// Enthält auch die nur gezeichneten Abschnitte (Bogenstränge einer Kreuzungsweiche) hinter den Fahrwegen.</summary>
    public static IReadOnlyList<Leg> LocalLegs(TrackPieceDef d, bool flipped)
    {
        var f = flipped ? -1 : 1;
        return [.. LocalPaths(d, flipped).SelectMany(p => p), .. (d.Decor ?? []).SelectMany(r => Chain(r, f))];
    }

    /// <summary>
    /// Fahrwege eines Gleisstücks, jeweils als Folge von Abschnitten. Gerade, Bogen und Prellbock haben einen Fahrweg,
    /// Weichen einen je Strang (alle beginnen an Anschluss 0), Kreuzungen zwei sich kreuzende Fahrwege.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<Leg>> LocalPaths(TrackPieceDef d, bool flipped)
    {
        var f = flipped ? -1 : 1;
        var turn = (d.RightHand ? 1 : -1) * f;
        // Drehscheibe, Schiebebühne: je Anschluss ein kurzes Gleis zur Bühne
        if (d.Ports is { } ports)
            return ports.Select(p => (IReadOnlyList<Leg>)[new Leg(p.X, p.Y, p.Heading + 180, false, d.Length)]).ToList();
        if (d.Routes is { } routes)
            return routes.Select(r => Chain(r, f)).ToList();
        switch (d.Kind)
        {
            case PieceKind.Straight:
            case PieceKind.Buffer:
                return [[new Leg(0, 0, 0, false, d.Length)]];
            case PieceKind.Curve:
                return [[new Leg(0, 0, 0, true, 0, d.Radius, d.Angle, turn)]];
            case PieceKind.Switch when d.Paths is { } paths:
                return paths.Select(p => Chain(p, f)).ToList();
            case PieceKind.Switch:
                return [[new Leg(0, 0, 0, false, d.Length)], [new Leg(0, 0, 0, true, 0, d.Radius, d.Angle, turn)]];
            case PieceKind.Crossing:
                var c = d.Length / 2;
                var a = d.Angle * f;
                var ar = a * Math.PI / 180;
                return [[new Leg(0, 0, 0, false, d.Length)], [new Leg(c - c * Math.Cos(ar), -c * Math.Sin(ar), a, false, d.Length)]];
            default:
                return [];
        }
    }

    /// <summary>Abschnitte eines Weichenstrangs aneinanderhängen; <paramref name="f"/> = −1 spiegelt.</summary>
    private static IReadOnlyList<Leg> Chain(IReadOnlyList<Segment> segments, int f) => Chain(new Route(0, 0, 0, segments), f);

    /// <summary>Fahrweg ab seinem Anfangspunkt aneinanderhängen; <paramref name="f"/> = −1 spiegelt an der x-Achse.</summary>
    private static IReadOnlyList<Leg> Chain(Route route, int f)
    {
        var segments = route.Segments;
        var legs = new List<Leg>();
        var pose = new Pose(route.X, route.Y * f, route.Heading * f);
        double from = 0;
        foreach (var s in segments)
        {
            var leg = s.Turn == 0
                ? new Leg(pose.X, pose.Y, pose.Heading, false, s.Length, From: from)
                : new Leg(pose.X, pose.Y, pose.Heading, true, 0, s.Radius, s.Angle, s.Turn * f, from);
            legs.Add(leg);
            pose = leg.End;
            from += leg.TrackLength;
        }
        return legs;
    }

    /// <summary>Weglänge von Anschluss 0 bis zum Ende des Fahrwegs <paramref name="path"/>.</summary>
    public static double PathLength(TrackPieceDef d, int path) => LocalPaths(d, false)[path].Sum(l => l.TrackLength);

    /// <summary>Anschlüsse in eigenen Koordinaten; die Richtung zeigt jeweils aus dem Gleis heraus.</summary>
    public static IReadOnlyList<Pose> LocalConnectors(TrackPieceDef d, bool flipped)
    {
        var paths = LocalPaths(d, flipped);
        var origin = new Pose(0, 0, 180);
        static Pose Back(Leg l) => new(l.X, l.Y, l.Heading + 180);
        if (d.Ports is { } ports) return ports;
        return d.Kind switch
        {
            PieceKind.Straight or PieceKind.Curve => [origin, paths[0][^1].End],
            PieceKind.Buffer => [origin],
            PieceKind.Switch => [origin, .. paths.Select(p => p[^1].End)],
            PieceKind.Crossing => [Back(paths[0][0]), paths[0][^1].End, Back(paths[1][0]), paths[1][^1].End],
            _ => [origin],
        };
    }

    /// <summary>Zu welchem Fahrweg ein Anschluss gehört und ob er an dessen Ende liegt.</summary>
    public static (int Path, bool AtEnd) PathOf(TrackPieceDef d, int index) => d.Kind switch
    {
        PieceKind.Switch => index == 0 ? (0, false) : (index - 1, true),
        PieceKind.Crossing => (index / 2, index % 2 == 1),
        PieceKind.Turntable or PieceKind.TransferTable => (index, false),
        _ => (0, index == 1),
    };
}
