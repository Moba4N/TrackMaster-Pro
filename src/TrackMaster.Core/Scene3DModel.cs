namespace TrackMaster.Core;

/// <summary>Punkt im 3D-Raum: X = Plan-x, Y = Höhe (oben), Z = Plan-y. Von oben gesehen liegt der Plan wie auf dem Bildschirm.</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, double f) => new(a.X * f, a.Y * f, a.Z * f);
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public Vec3 Normalized() { var l = Length; return l < 1e-12 ? this : new Vec3(X / l, Y / l, Z / l); }
    public static Vec3 Cross(Vec3 a, Vec3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb Mix(Rgb a, Rgb b, double t)
        => new((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    public Rgb Scale(double f)
        => new((byte)Math.Min(255, R * f), (byte)Math.Min(255, G * f), (byte)Math.Min(255, B * f));
}

/// <summary>Oberflächenart: bestimmt Glanz und Leuchten in der Darstellung.</summary>
public enum SurfaceKind { Matte, Water, Rail, Window, Lamp }

/// <summary>Dreiecke einer Farbe. Je drei aufeinanderfolgende Punkte bilden ein Dreieck; dargestellt wird beidseitig.</summary>
public sealed class SceneMesh
{
    public List<Vec3> Points { get; } = [];
    public bool IsEmpty => Points.Count == 0;
    public int TriangleCount => Points.Count / 3;

    public void Triangle(Vec3 a, Vec3 b, Vec3 c)
    {
        Points.Add(a); Points.Add(b); Points.Add(c);
    }

    public void Quad(Vec3 a, Vec3 b, Vec3 c, Vec3 d)
    {
        Triangle(a, b, c);
        Triangle(a, c, d);
    }

    public void Box(Vec3 min, Vec3 max)
    {
        double x0 = Math.Min(min.X, max.X), x1 = Math.Max(min.X, max.X);
        double y0 = Math.Min(min.Y, max.Y), y1 = Math.Max(min.Y, max.Y);
        double z0 = Math.Min(min.Z, max.Z), z1 = Math.Max(min.Z, max.Z);
        static Vec3 P(double x, double y, double z) => new(x, y, z);
        Quad(P(x0, y1, z0), P(x0, y1, z1), P(x1, y1, z1), P(x1, y1, z0)); // oben
        Quad(P(x0, y0, z0), P(x1, y0, z0), P(x1, y0, z1), P(x0, y0, z1)); // unten
        Quad(P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1)); // vorne
        Quad(P(x1, y0, z0), P(x0, y0, z0), P(x0, y1, z0), P(x1, y1, z0)); // hinten
        Quad(P(x1, y0, z1), P(x1, y0, z0), P(x1, y1, z0), P(x1, y1, z1)); // rechts
        Quad(P(x0, y0, z0), P(x0, y0, z1), P(x0, y1, z1), P(x0, y1, z0)); // links
    }
}

/// <summary>
/// Baut aus dem Plan ein 3D-Modell, unabhängig von der Darstellung (WPF unter Windows, OpenGL unter Android):
/// Platte, Gelände, Gewässer, Gebäude, Bäume, Schotter, Schienen, Schwellen, Pfeiler, Tunnel, Prellböcke und Signale.
/// </summary>
public sealed class Scene3DModel
{
    public static readonly Rgb WaterColor = new(0x3F, 0x86, 0xC0);
    public static readonly Rgb RailColor = new(0x6A, 0x6E, 0x74);
    public static readonly Rgb WindowColor = new(0x30, 0x34, 0x3A);

    private readonly LayoutPlan _plan;
    private readonly double _g;

    public Dictionary<(Rgb Color, SurfaceKind Kind), SceneMesh> Meshes { get; } = [];

    public Scene3DModel(LayoutPlan plan)
    {
        _plan = plan;
        _g = plan.Gauge.TrackWidth;
        Build();
    }

    public int TriangleCount => Meshes.Values.Sum(m => m.TriangleCount);

    /// <summary>Signallampen (oben, unten) je Signaltyp.</summary>
    public static (Rgb Top, Rgb Bottom) SignalLamps(SignalType t) => t switch
    {
        SignalType.Vorsignal => (new Rgb(0xFF, 0xD7, 0x00), new Rgb(0x32, 0xCD, 0x32)),
        SignalType.Sperrsignal => (new Rgb(0xFF, 0xFF, 0xFF), new Rgb(0xFF, 0x00, 0x00)),
        SignalType.Blocksignal => (new Rgb(0x32, 0xCD, 0x32), new Rgb(0xFF, 0x00, 0x00)),
        _ => (new Rgb(0xFF, 0x00, 0x00), new Rgb(0x32, 0xCD, 0x32)),
    };

    private SceneMesh Mesh(Rgb c, SurfaceKind kind = SurfaceKind.Matte)
    {
        if (!Meshes.TryGetValue((c, kind), out var m)) Meshes[(c, kind)] = m = new SceneMesh();
        return m;
    }

    private static Vec3 P(double x, double y, double z) => new(x, z, y);

    private void Build()
    {
        var plan = _plan;
        var g = _g;
        // Platte
        var thickness = Math.Max(g * 1.5, 15);
        foreach (var r in BoardGeometry.Rects(plan))
        {
            Mesh(new Rgb(0x8E, 0xA8, 0x6E)).Box(P(r.X0, r.Y0, -thickness), P(r.X1, r.Y1, 0));
            Mesh(new Rgb(0x8A, 0x6B, 0x4A)).Box(P(r.X0 - 2, r.Y0 - 2, -thickness * 1.6), P(r.X1 + 2, r.Y1 + 2, -thickness));
        }

        foreach (var s in plan.Scenery)
            switch (s.Kind)
            {
                case SceneryKind.Terrain: Prism(s.Points, 0, s.Height, new Rgb(0x7F, 0x9E, 0x55), new Rgb(0x8C, 0x74, 0x50), SurfaceKind.Matte); break;
                case SceneryKind.Water: Prism(s.Points, 0.3, 0.8, WaterColor, WaterColor, SurfaceKind.Water); break;
                case SceneryKind.Building: Building(s); break;
                case SceneryKind.Tree: Tree(s); break;
            }

        var totalLength = plan.Pieces.Sum(p => p.Def.TrackLength);
        var sleepers = totalLength / g < 4000;
        foreach (var p in plan.Pieces) Track(p, sleepers);
        foreach (var sig in plan.Signals) SignalModel(sig);
    }

    /// <summary>Senkrechtes Prisma über einem Vieleck (Gelände, Wasser).</summary>
    private void Prism(IReadOnlyList<(double X, double Y)> pts, double z0, double z1, Rgb top, Rgb side, SurfaceKind kind)
    {
        if (pts.Count < 3) return;
        var m = Mesh(top, kind);
        foreach (var (a, b, c) in Geometry2D.Triangulate(pts))
            m.Triangle(P(pts[a].X, pts[a].Y, z1), P(pts[c].X, pts[c].Y, z1), P(pts[b].X, pts[b].Y, z1));
        var sm = Mesh(side, kind);
        for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
            sm.Quad(P(pts[j].X, pts[j].Y, z0), P(pts[i].X, pts[i].Y, z0), P(pts[i].X, pts[i].Y, z1), P(pts[j].X, pts[j].Y, z1));
    }

    private void Building(SceneryItem s)
    {
        var o = s.Outline();
        var wall = s.Height * 0.68;
        var walls = Mesh(new Rgb(0xE4, 0xD6, 0xBE));
        // Wände
        for (int i = 0, j = 3; i < 4; j = i++)
            walls.Quad(P(o[j].X, o[j].Y, 0), P(o[i].X, o[i].Y, 0), P(o[i].X, o[i].Y, wall), P(o[j].X, o[j].Y, wall));
        // Fenster als schmale Bänder vor den Wänden
        var win = Mesh(WindowColor, SurfaceKind.Window);
        for (int i = 0, j = 3; i < 4; j = i++)
        {
            var (ax, ay) = o[j]; var (bx, by) = o[i];
            var len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
            var n = Math.Max(1, (int)(len / (_g * 2.2)));
            // nach außen versetzt (Umlauf im Uhrzeigersinn: außen = links von der Kante bei y nach unten)
            double nx = (by - ay) / len * 0.4, ny = -(bx - ax) / len * 0.4;
            for (var k = 0; k < n; k++)
            {
                double t0 = (k + 0.3) / n, t1 = (k + 0.7) / n;
                foreach (var (z0, z1) in new[] { (wall * 0.25, wall * 0.45), (wall * 0.6, wall * 0.8) })
                    win.Quad(P(ax + (bx - ax) * t0 + nx, ay + (by - ay) * t0 + ny, z0), P(ax + (bx - ax) * t1 + nx, ay + (by - ay) * t1 + ny, z0),
                             P(ax + (bx - ax) * t1 + nx, ay + (by - ay) * t1 + ny, z1), P(ax + (bx - ax) * t0 + nx, ay + (by - ay) * t0 + ny, z1));
            }
        }
        // Satteldach entlang der längeren Seite
        var roof = Mesh(new Rgb(0xA8, 0x4A, 0x36));
        var top = s.Height;
        static (double X, double Y) Mid((double X, double Y) a, (double X, double Y) b) => ((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        var (r0, r1) = s.Width >= s.Depth ? (Mid(o[0], o[3]), Mid(o[1], o[2])) : (Mid(o[0], o[1]), Mid(o[3], o[2]));
        if (s.Width >= s.Depth)
        {
            roof.Quad(P(o[0].X, o[0].Y, wall), P(o[1].X, o[1].Y, wall), P(r1.X, r1.Y, top), P(r0.X, r0.Y, top));
            roof.Quad(P(o[2].X, o[2].Y, wall), P(o[3].X, o[3].Y, wall), P(r0.X, r0.Y, top), P(r1.X, r1.Y, top));
            walls.Triangle(P(o[0].X, o[0].Y, wall), P(r0.X, r0.Y, top), P(o[3].X, o[3].Y, wall));
            walls.Triangle(P(o[1].X, o[1].Y, wall), P(r1.X, r1.Y, top), P(o[2].X, o[2].Y, wall));
        }
        else
        {
            roof.Quad(P(o[1].X, o[1].Y, wall), P(o[2].X, o[2].Y, wall), P(r1.X, r1.Y, top), P(r0.X, r0.Y, top));
            roof.Quad(P(o[3].X, o[3].Y, wall), P(o[0].X, o[0].Y, wall), P(r0.X, r0.Y, top), P(r1.X, r1.Y, top));
            walls.Triangle(P(o[0].X, o[0].Y, wall), P(r0.X, r0.Y, top), P(o[1].X, o[1].Y, wall));
            walls.Triangle(P(o[3].X, o[3].Y, wall), P(r1.X, r1.Y, top), P(o[2].X, o[2].Y, wall));
        }
    }

    private void Tree(SceneryItem t)
    {
        var r = t.Width / 2;
        var trunkH = t.Height * 0.3;
        Mesh(new Rgb(0x6B, 0x4A, 0x2F)).Box(P(t.X - r * 0.12, t.Y - r * 0.12, 0), P(t.X + r * 0.12, t.Y + r * 0.12, trunkH));
        var crown = Mesh(new Rgb(0x3E, 0x7A, 0x32));
        const int n = 10;
        var apex = P(t.X, t.Y, t.Height);
        for (var i = 0; i < n; i++)
        {
            double a0 = i * 2 * Math.PI / n, a1 = (i + 1) * 2 * Math.PI / n;
            var b0 = P(t.X + r * Math.Cos(a0), t.Y + r * Math.Sin(a0), trunkH * 0.8);
            var b1 = P(t.X + r * Math.Cos(a1), t.Y + r * Math.Sin(a1), trunkH * 0.8);
            crown.Triangle(b0, apex, b1);
            crown.Triangle(b1, P(t.X, t.Y, trunkH * 0.8), b0);
        }
    }

    /// <summary>Punkte entlang eines Abschnitts mit Höhe.</summary>
    private static List<(double X, double Y, double Z, double H)> Samples(PlacedPiece p, int legIndex, Leg leg, double step)
    {
        var n = leg.IsArc ? Math.Max(2, (int)Math.Ceiling(leg.TrackLength / step)) : 1;
        var list = new List<(double, double, double, double)>();
        for (var i = 0; i <= n; i++)
        {
            var t = (double)i / n;
            var q = leg.At(t);
            list.Add((q.X, q.Y, p.HeightAt(legIndex, t), q.HeadingRad));
        }
        return list;
    }

    private void Track(PlacedPiece p, bool sleepers)
    {
        var g = _g;
        var legs = p.WorldLegs();
        var ballast = Mesh(new Rgb(0x9C, 0x92, 0x84));
        var sleeper = Mesh(new Rgb(0x5A, 0x48, 0x38));
        var rail = Mesh(RailColor, SurfaceKind.Rail);
        var pier = Mesh(new Rgb(0xA8, 0xA4, 0x9C));
        var tunnel = Mesh(new Rgb(0x7A, 0x72, 0x66));
        for (var li = 0; li < legs.Count; li++)
        {
            var pts = Samples(p, li, legs[li], g * 1.5);
            for (var i = 0; i + 1 < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                static (double X, double Y) N(double h) => (-Math.Sin(h), Math.Cos(h));
                var na = N(a.H);
                var nb = N(b.H);
                double bt = 0.22 * g, bb = 0.5 * g;   // Schotter oben/unten unter Schienenoberkante
                double wt = 1.05 * g, wb = 1.45 * g;  // halbe Breite oben/unten
                static Vec3 At((double X, double Y, double Z, double H) s, (double X, double Y) n, double off, double dz) => P(s.X + n.X * off, s.Y + n.Y * off, s.Z - dz);
                ballast.Quad(At(a, na, -wt, bt), At(b, nb, -wt, bt), At(b, nb, wt, bt), At(a, na, wt, bt));
                ballast.Quad(At(a, na, wt, bt), At(b, nb, wt, bt), At(b, nb, wb, bb), At(a, na, wb, bb));
                ballast.Quad(At(a, na, -wb, bb), At(b, nb, -wb, bb), At(b, nb, -wt, bt), At(a, na, -wt, bt));
                if (a.Z > bb + 1)
                    ballast.Quad(At(a, na, -wb, bb), At(a, na, wb, bb), At(b, nb, wb, bb), At(b, nb, -wb, bb));
                foreach (var side in new[] { -0.5, 0.5 })
                {
                    double hw = 0.07 * g, rh = 0.16 * g;
                    rail.Quad(At(a, na, side * g - hw, 0), At(b, nb, side * g - hw, 0), At(b, nb, side * g + hw, 0), At(a, na, side * g + hw, 0));
                    rail.Quad(At(a, na, side * g - hw, rh), At(b, nb, side * g - hw, rh), At(b, nb, side * g - hw, 0), At(a, na, side * g - hw, 0));
                    rail.Quad(At(a, na, side * g + hw, 0), At(b, nb, side * g + hw, 0), At(b, nb, side * g + hw, rh), At(a, na, side * g + hw, rh));
                }
                if (p.Tunnel)
                {
                    // Tunnelröhre als Halbkreis-Gewölbe
                    const int k = 8;
                    var r = g * 2.2;
                    Vec3 Arc((double X, double Y, double Z, double H) s, (double X, double Y) n, double t) => P(s.X + n.X * r * Math.Cos(t), s.Y + n.Y * r * Math.Cos(t), s.Z - bb + r * Math.Sin(t) * 1.15);
                    for (var j = 0; j < k; j++)
                    {
                        double t0 = Math.PI * j / k, t1 = Math.PI * (j + 1) / k;
                        tunnel.Quad(Arc(a, na, t0), Arc(b, nb, t0), Arc(b, nb, t1), Arc(a, na, t1));
                    }
                }
            }
            if (sleepers)
            {
                var len = legs[li].TrackLength;
                var n = (int)(len / (g * 0.45));
                for (var i = 0; i < n; i++)
                {
                    var t = (i + 0.5) / n;
                    var q = legs[li].At(t);
                    var z = p.HeightAt(li, t);
                    double cx = Math.Cos(q.HeadingRad), cy = Math.Sin(q.HeadingRad), nx = -cy, ny = cx;
                    double hl = 0.12 * g, hw = 0.95 * g;
                    var c = new[] { (-hl, -hw), (hl, -hw), (hl, hw), (-hl, hw) }.Select(o => (q.X + cx * o.Item1 + nx * o.Item2, q.Y + cy * o.Item1 + ny * o.Item2)).ToArray();
                    sleeper.Quad(P(c[0].Item1, c[0].Item2, z - 0.16 * g), P(c[1].Item1, c[1].Item2, z - 0.16 * g), P(c[2].Item1, c[2].Item2, z - 0.16 * g), P(c[3].Item1, c[3].Item2, z - 0.16 * g));
                }
            }
            // Pfeiler unter hochliegenden Gleisen
            var mid = legs[li].At(0.5);
            var zm = p.HeightAt(li, 0.5);
            if (zm > g * 1.5 && !p.Tunnel)
            {
                var w = g * 0.6;
                pier.Box(P(mid.X - w, mid.Y - w, 0), P(mid.X + w, mid.Y + w, zm - 0.5 * g));
            }
        }
        if (p.Def.Kind == PieceKind.Buffer)
        {
            var e = legs[0].End;
            var z = p.Height;
            Mesh(new Rgb(0xC4, 0x2B, 0x1C)).Box(P(e.X - g * 0.5, e.Y - g * 0.5, z), P(e.X + g * 0.5, e.Y + g * 0.5, z + g * 0.8));
        }
    }

    private void SignalModel(Signal s)
    {
        var (x, y, _) = Electrics.SignalPose(_plan, s);
        var z = _plan.Find(s.PieceId)?.Height ?? 0;
        var g = _g;
        var height = s.Type == SignalType.Sperrsignal ? g * 1.2 : g * 4;
        Mesh(new Rgb(0x50, 0x50, 0x50)).Box(P(x - g * 0.08, y - g * 0.08, z), P(x + g * 0.08, y + g * 0.08, z + height));
        Mesh(new Rgb(0x20, 0x20, 0x20)).Box(P(x - g * 0.3, y - g * 0.3, z + height - g * 0.2), P(x + g * 0.3, y + g * 0.3, z + height + g * 0.9));
        var (top, bottom) = SignalLamps(s.Type);
        Mesh(top, SurfaceKind.Lamp).Box(P(x - g * 0.32, y - g * 0.32, z + height + g * 0.45), P(x + g * 0.32, y + g * 0.32, z + height + g * 0.7));
        Mesh(bottom, SurfaceKind.Lamp).Box(P(x - g * 0.32, y - g * 0.32, z + height + g * 0.05), P(x + g * 0.32, y + g * 0.32, z + height + g * 0.3));
    }
}

/// <summary>Licht nach Tageszeit: Sonne, Aufhellung, Umgebungslicht, Himmel und Fensterbeleuchtung.</summary>
public sealed record DayLight(Rgb Sun, Vec3 SunDirection, Rgb Fill, Vec3 FillDirection, Rgb Ambient, Rgb SkyTop, Rgb SkyBottom, bool WindowsLit)
{
    /// <summary>Licht um <paramref name="hour"/> Uhr (5 bis 23). Richtungen zeigen vom Licht zur Szene.</summary>
    public static DayLight At(double hour)
    {
        var h = hour;
        // Sonnenhöhe: Aufgang 6 Uhr, Höchststand 13 Uhr, Untergang 20 Uhr
        var dayPhase = Math.Clamp((h - 6) / 14, 0, 1);
        var elevation = Math.Sin(dayPhase * Math.PI);
        var night = h < 6 || h > 20.5;
        var azimuth = (h - 13) / 14 * Math.PI;
        var warm = 1 - Math.Clamp(elevation * 2.2, 0, 1);
        var sunColor = night ? new Rgb(0x30, 0x3A, 0x60) : Rgb.Mix(new Rgb(0xFF, 0xF6, 0xE8), new Rgb(0xFF, 0xA8, 0x60), warm);
        var intensity = night ? 0.35 : 0.45 + 0.55 * elevation;
        return new DayLight(
            sunColor.Scale(intensity),
            new Vec3(-Math.Sin(azimuth), -Math.Max(elevation, 0.15), -Math.Cos(azimuth) * 0.6 - 0.4),
            (night ? new Rgb(0x20, 0x28, 0x48) : new Rgb(0x90, 0xA0, 0xB8)).Scale(night ? 0.4 : 0.35),
            new Vec3(0.6, -0.5, 0.7),
            night ? new Rgb(0x22, 0x26, 0x3A) : Rgb.Mix(new Rgb(0x70, 0x74, 0x7A), new Rgb(0x6A, 0x58, 0x50), warm).Scale(0.6 + 0.4 * elevation),
            night ? new Rgb(0x08, 0x0C, 0x20) : Rgb.Mix(new Rgb(0x5E, 0x9C, 0xE0), new Rgb(0x3A, 0x4A, 0x80), warm),
            night ? new Rgb(0x1C, 0x24, 0x40) : Rgb.Mix(new Rgb(0xCF, 0xE4, 0xF7), new Rgb(0xF4, 0xA2, 0x6A), warm),
            h > 19 || h < 7);
    }

    public static Rgb WindowGlow => new(0xFF, 0xD0, 0x70);

    public static string TimeText(double hour) => $"{(int)hour:00}:{(int)((hour - (int)hour) * 60):00} Uhr";
}

/// <summary>Fahrweg zum Mitfahren: ab einem Gleis, an Weichen geradeaus, an Prellböcken und offenen Enden zurück.</summary>
public sealed class RidePath
{
    public IReadOnlyList<(double X, double Y, double Z)> Points { get; }
    public double[] Distances { get; }
    /// <summary>Rundkurs: der Weg kehrt zum Anfang zurück.</summary>
    public bool Closed { get; }
    public double Length => Distances.Length == 0 ? 0 : Distances[^1];
    public bool IsEmpty => Points.Count < 2;

    private RidePath(List<(double X, double Y, double Z)> points, bool closed)
    {
        Points = points;
        Closed = closed;
        Distances = new double[points.Count];
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            Distances[i] = Distances[i - 1] + Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y) + (b.Z - a.Z) * (b.Z - a.Z));
        }
    }

    /// <summary>Maßstab „1:87“ → 87.</summary>
    public static double ScaleDivisor(string scale)
        => double.TryParse(scale.Split(':').Last().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 87;

    /// <summary>Vorbildgeschwindigkeit in km/h als Modell-mm je Sekunde.</summary>
    public static double ModelSpeed(double kmh, string scale) => kmh / 3.6 * 1000 / ScaleDivisor(scale);

    public (double X, double Y, double Z) PointAt(double s)
    {
        var total = Length;
        if (Points.Count == 0) return (0, 0, 0);
        if (Closed && total > 0) s = ((s % total) + total) % total;
        else s = Math.Clamp(s, 0, total);
        var i = Array.BinarySearch(Distances, s);
        if (i < 0) i = ~i;
        i = Math.Clamp(i, 1, Distances.Length - 1);
        var seg = Distances[i] - Distances[i - 1];
        var t = seg <= 0 ? 0 : (s - Distances[i - 1]) / seg;
        var a = Points[i - 1];
        var b = Points[i];
        return (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
    }

    /// <summary>Kamera beim Mitfahren: Augpunkt und Blickziel in 3D-Koordinaten (X, Höhe, Z).</summary>
    public (Vec3 Eye, Vec3 Target) Camera(double s, double gauge)
    {
        var eye = PointAt(s);
        var ahead = PointAt(s + gauge * 14);
        var eyeHeight = gauge * 2.6;
        return (new Vec3(eye.X, eye.Z + eyeHeight, eye.Y), new Vec3(ahead.X, ahead.Z + eyeHeight * 0.85, ahead.Y));
    }

    public static RidePath Build(LayoutPlan plan, int? startPiece)
    {
        if (plan.Pieces.Count == 0) return new RidePath([], false);
        var conn = Connectivity.Analyze(plan);
        var piece = (startPiece is { } id ? plan.Find(id) : null) ?? plan.Pieces.FirstOrDefault(p => p.Def.Kind == PieceKind.Straight) ?? plan.Pieces[0];
        var entry = 0;
        var limit = plan.Pieces.Sum(p => p.Def.TrackLength) * 3 + 1000;
        double length = 0;
        var visits = new HashSet<(int, int)>();
        var points = new List<(double, double, double)>();
        var step = plan.Gauge.TrackWidth;
        var startState = (piece.Id, entry);
        var closed = false;
        while (length < limit)
        {
            if (!visits.Add((piece.Id, entry)) && (piece.Id, entry) == startState)
            {
                closed = true;
                break;
            }
            var d = piece.Def;
            var count = TrackGeometry.LocalConnectors(d, piece.Flipped).Count;
            var exit = d.Kind switch
            {
                PieceKind.Crossing => entry switch { 0 => 1, 1 => 0, 2 => 3, _ => 2 },
                PieceKind.Buffer => 0,
                _ when count == 1 => 0,
                _ => entry == 0 ? 1 : 0,
            };
            var (way, forward) = d.Kind switch
            {
                PieceKind.Switch => entry == 0 ? (exit - 1, true) : (entry - 1, false),
                PieceKind.Crossing => (entry / 2, entry % 2 == 0),
                PieceKind.Buffer => (0, true),
                _ => (0, entry == 0),
            };
            var all = piece.WorldLegs();
            var paths = piece.WorldPaths();
            var first = paths.Take(way).Sum(p => p.Count);
            var order = Enumerable.Range(first, paths[way].Count);
            foreach (var leg in forward ? order : order.Reverse())
            {
                var l = all[leg];
                var n = Math.Max(2, (int)Math.Ceiling(l.TrackLength / step));
                for (var i = 0; i <= n; i++)
                {
                    var t = forward ? (double)i / n : 1 - (double)i / n;
                    var q = l.At(t);
                    points.Add((q.X, q.Y, piece.HeightAt(leg, t)));
                }
                if (d.Kind == PieceKind.Buffer)
                {
                    // Am Prellbock zurück
                    for (var i = n; i >= 0; i--)
                    {
                        var q = l.At((double)i / n);
                        points.Add((q.X, q.Y, piece.HeightAt(leg, (double)i / n)));
                    }
                }
                length += l.TrackLength;
            }
            if (conn.Find(piece.Id, exit)?.Link is { } next)
            {
                piece = plan.Find(next.PieceId)!;
                entry = next.Index;
            }
            else
            {
                // Offenes Ende: auf demselben Gleis zurückfahren
                entry = exit;
            }
        }
        // Doppelte Punkte entfernen
        var path = new List<(double X, double Y, double Z)>();
        foreach (var p in points)
            if (path.Count == 0 || Math.Abs(path[^1].X - p.Item1) + Math.Abs(path[^1].Y - p.Item2) > 0.01) path.Add(p);
        return new RidePath(path, closed);
    }
}
