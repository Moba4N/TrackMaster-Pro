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

    public void Box(Vec3 min, Vec3 max, bool top = true)
    {
        double x0 = Math.Min(min.X, max.X), x1 = Math.Max(min.X, max.X);
        double y0 = Math.Min(min.Y, max.Y), y1 = Math.Max(min.Y, max.Y);
        double z0 = Math.Min(min.Z, max.Z), z1 = Math.Max(min.Z, max.Z);
        static Vec3 P(double x, double y, double z) => new(x, y, z);
        if (top) Quad(P(x0, y1, z0), P(x0, y1, z1), P(x1, y1, z1), P(x1, y1, z0)); // oben
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
    public static readonly Rgb RailColor = new(0xB4, 0xB8, 0xBC);
    public static readonly Rgb RailSideColor = new(0x6E, 0x55, 0x44);

    /// <summary>Schienenoberkante über der Trasse (Unterkante des Schotterbetts): Schotter, Schwelle und Schiene.</summary>
    public static double RailTop(double gauge) => gauge * 0.42;

    /// <summary>Feste Pseudo-Zufallszahl 0…1 für Farbabwechslung (immer gleich für dieselben Werte).</summary>
    public static double Noise(int a, int b, int c = 0)
    {
        unchecked
        {
            var h = (uint)(a * 73856093 ^ b * 19349663 ^ c * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xFFFFFF) / (double)0xFFFFFF;
        }
    }
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
            Mesh(new Rgb(0x6E, 0x5A, 0x40)).Box(P(r.X0, r.Y0, -thickness), P(r.X1, r.Y1, 0), top: false);
            Mesh(new Rgb(0x8A, 0x6B, 0x4A)).Box(P(r.X0 - 2, r.Y0 - 2, -thickness * 1.6), P(r.X1 + 2, r.Y1 + 2, -thickness));
            Meadow(r.X0, r.Y0, r.X1, r.Y1);
        }

        foreach (var s in plan.Scenery)
            switch (s.Kind)
            {
                case SceneryKind.Terrain: Hill(s); break;
                case SceneryKind.Water: Water(s); break;
                case SceneryKind.Building: Building(s); break;
                case SceneryKind.Tree: Tree(s); break;
            }

        var totalLength = plan.Pieces.Sum(p => p.Def.TrackLength);
        var sleepers = totalLength / g < 8000;
        foreach (var p in plan.Pieces) Track(p, sleepers);
        foreach (var p in plan.Pieces.Where(p => p.Def.IsTable)) Table(p);
        foreach (var sig in plan.Signals) SignalModel(sig);
    }

    /// <summary>Drehscheibe bzw. Schiebebühne: Grube, Ring, Bühne mit Gleis in Richtung Anschluss 0.</summary>
    private void Table(PlacedPiece p)
    {
        if (TableShape.Of(p) is not { } t) return;
        var g = _g;
        var z = p.Height;
        var lift = RailTop(g);
        var pit = Mesh(new Rgb(0x6A, 0x62, 0x56));
        if (t.Round)
        {
            var rim = Mesh(new Rgb(0xB8, 0xB2, 0xA6));
            const int n = 48;
            for (var i = 0; i < n; i++)
            {
                double a0 = i * 2 * Math.PI / n, a1 = (i + 1) * 2 * Math.PI / n;
                (double X, double Y) On(double r, double a) => (t.Center.X + r * Math.Cos(a), t.Center.Y + r * Math.Sin(a));
                var (p0, p1, q0, q1) = (On(t.PitRadius, a0), On(t.PitRadius, a1), On(t.RimRadius, a0), On(t.RimRadius, a1));
                pit.Triangle(P(t.Center.X, t.Center.Y, z + 0.3), P(p0.X, p0.Y, z + 0.3), P(p1.X, p1.Y, z + 0.3));
                if (t.RimRadius > t.PitRadius + 0.5)
                    rim.Quad(P(p0.X, p0.Y, z + 0.6), P(q0.X, q0.Y, z + 0.6), P(q1.X, q1.Y, z + 0.6), P(p1.X, p1.Y, z + 0.6));
            }
        }
        else
            foreach (var (a, b, c) in Geometry2D.Triangulate(t.Pit))
                pit.Triangle(P(t.Pit[a].X, t.Pit[a].Y, z + 0.3), P(t.Pit[c].X, t.Pit[c].Y, z + 0.3), P(t.Pit[b].X, t.Pit[b].Y, z + 0.3));
        // Bühne: Brücke mit zwei Schienen
        var (s, e) = (t.BridgeA, t.BridgeB);
        Beam(Mesh(new Rgb(0x4E, 0x46, 0x3E)), s, e, 0, g * 1.3, z + 0.3, z + lift - 0.2 * g);
        Beam(Mesh(RailColor, SurfaceKind.Rail), s, e, g / 2, g * 0.07, z + lift - 0.2 * g, z + lift);
        Beam(Mesh(RailColor, SurfaceKind.Rail), s, e, -g / 2, g * 0.07, z + lift - 0.2 * g, z + lift);
    }

    /// <summary>Quader entlang der Strecke a–b, seitlich um <paramref name="offset"/> versetzt.</summary>
    private static void Beam(SceneMesh m, (double X, double Y) a, (double X, double Y) b, double offset, double halfWidth, double z0, double z1)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Max(Math.Sqrt(dx * dx + dy * dy), 1e-6);
        double nx = -dy / len, ny = dx / len;
        Vec3 C(double x, double y, double side, double h) => P(x + nx * (offset + side), y + ny * (offset + side), h);
        var (a0, a1, b0, b1) = (C(a.X, a.Y, -halfWidth, z1), C(a.X, a.Y, halfWidth, z1), C(b.X, b.Y, -halfWidth, z1), C(b.X, b.Y, halfWidth, z1));
        m.Quad(a0, b0, b1, a1);
        m.Quad(C(a.X, a.Y, -halfWidth, z0), C(b.X, b.Y, -halfWidth, z0), b0, a0);
        m.Quad(C(a.X, a.Y, halfWidth, z0), a1, b1, C(b.X, b.Y, halfWidth, z0));
        m.Quad(C(a.X, a.Y, -halfWidth, z0), a0, a1, C(a.X, a.Y, halfWidth, z0));
        m.Quad(C(b.X, b.Y, -halfWidth, z0), C(b.X, b.Y, halfWidth, z0), b1, b0);
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

    // Wiese in mehreren Grüntönen, damit die Fläche nicht wie ein einfarbiges Brett wirkt
    private static readonly Rgb[] Grass = [new(0x76, 0x98, 0x4A), new(0x7D, 0x9E, 0x4E), new(0x84, 0xA3, 0x53), new(0x8A, 0xA6, 0x57), new(0x92, 0xA8, 0x5C)];

    /// <summary>Weiches Rauschen 0…1 (bilinear zwischen festen Zufallswerten im Raster <paramref name="scale"/>).</summary>
    public static double Smooth(double x, double y, double scale, int seed)
    {
        double fx = x / scale, fy = y / scale;
        int i = (int)Math.Floor(fx), j = (int)Math.Floor(fy);
        double tx = fx - i, ty = fy - j;
        tx = tx * tx * (3 - 2 * tx);
        ty = ty * ty * (3 - 2 * ty);
        double a = Noise(i, j, seed), b = Noise(i + 1, j, seed), c = Noise(i, j + 1, seed), d = Noise(i + 1, j + 1, seed);
        return (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty;
    }

    private Rgb GrassAt(double x, double y, double fine)
    {
        var v = Smooth(x, y, _g * 14, 7) * 0.75 + fine * 0.25;
        return Grass[Math.Clamp((int)(v * Grass.Length), 0, Grass.Length - 1)];
    }
    private static readonly Rgb Earth = new(0x8E, 0x76, 0x52);
    private static readonly Rgb Rock = new(0x8C, 0x88, 0x80);
    private static readonly Rgb RockDark = new(0x76, 0x72, 0x6C);

    private void Meadow(double x0, double y0, double x1, double y1)
    {
        var cell = Math.Max(_g * 3, 25);
        int nx = Math.Max(1, (int)Math.Ceiling((x1 - x0) / cell)), ny = Math.Max(1, (int)Math.Ceiling((y1 - y0) / cell));
        double cx = (x1 - x0) / nx, cy = (y1 - y0) / ny;
        for (var j = 0; j < ny; j++)
            for (var i = 0; i < nx; i++)
            {
                double a = x0 + i * cx, b = y0 + j * cy;
                // Weiche Flecken plus feine Abwechslung
                var color = GrassAt(a + cx / 2, b + cy / 2, Noise(i, j, 11));
                Mesh(color).Quad(P(a, b, 0), P(a, b + cy, 0), P(a + cx, b + cy, 0), P(a + cx, b, 0));
            }
    }

    /// <summary>Hügel als Höhenfeld mit weichem Hang; Wiese, Erde und Fels je nach Neigung.</summary>
    private void Hill(SceneryItem s)
    {
        if (s.Points.Count < 3 || s.Height <= 0) return;
        var f = TerrainField.For(s);
        Vec3 V(int i, int j) => P(f.X0 + i * f.Cell, f.Y0 + j * f.Cell, f.Vertex(i, j) + (f.Vertex(i, j) > 0 ? 0.2 : 0));
        void Tri(int i, int j, Vec3 a, Vec3 b, Vec3 c, int k)
        {
            if (a.Y < 0.01 && b.Y < 0.01 && c.Y < 0.01) return;
            var n = Vec3.Cross(b - a, c - a).Normalized();
            var steep = 1 - Math.Abs(n.Y);   // 0 = eben, 1 = senkrecht
            var noise = Smooth(a.X, a.Z, _g * 5, s.Id + 3) * 0.7 + Noise(i + s.Id * 31, j, k) * 0.3;
            var top = (a.Y + b.Y + c.Y) / 3 / Math.Max(f.Height, 1);
            Rgb color;
            if (steep > 0.55 + noise * 0.1) color = noise > 0.5 ? Rock : RockDark;
            else if (steep > 0.38 + noise * 0.12) color = Earth;
            else
                color = top > 0.85 && noise > 0.6 ? Grass[^1] : GrassAt(a.X, a.Z, noise);
            Mesh(color).Triangle(a, b, c);
        }
        for (var j = 0; j < f.Ny; j++)
            for (var i = 0; i < f.Nx; i++)
            {
                Vec3 a = V(i, j), b = V(i + 1, j), c = V(i + 1, j + 1), d = V(i, j + 1);
                Tri(i, j, a, b, c, 0);
                Tri(i, j, a, c, d, 1);
            }
    }

    /// <summary>Wasserfläche knapp über der Platte mit sandig-erdigem Uferstreifen.</summary>
    private void Water(SceneryItem s)
    {
        var pts = s.Points;
        if (pts.Count < 3) return;
        Prism(pts, 0.3, 0.8, WaterColor, new Rgb(0x2E, 0x6A, 0x9A), SurfaceKind.Water);
        var bank = Mesh(new Rgb(0xA8, 0x96, 0x6E));
        var mud = Mesh(new Rgb(0x7A, 0x6A, 0x4C));
        var w = Math.Max(_g * 1.2, 6);
        for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
        {
            var (ax, ay) = pts[j];
            var (bx, by) = pts[i];
            var len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
            if (len < 1e-6) continue;
            double nx = -(by - ay) / len, ny = (bx - ax) / len;
            // nach außen zeigen lassen
            if (Geometry2D.PointInPolygon(pts, (ax + bx) / 2 + nx * 0.5, (ay + by) / 2 + ny * 0.5)) { nx = -nx; ny = -ny; }
            mud.Quad(P(ax, ay, 0.85), P(bx, by, 0.85), P(bx + nx * w * 0.4, by + ny * w * 0.4, 0.6), P(ax + nx * w * 0.4, ay + ny * w * 0.4, 0.6));
            bank.Quad(P(ax + nx * w * 0.4, ay + ny * w * 0.4, 0.6), P(bx + nx * w * 0.4, by + ny * w * 0.4, 0.6), P(bx + nx * w, by + ny * w, 0.15), P(ax + nx * w, ay + ny * w, 0.15));
        }
    }

    private void Building(SceneryItem s)
    {
        var o = s.Outline();
        // Auf dem Hügel: auf die tiefste Ecke stellen, bergseitig steckt das Haus im Hang
        var ground = o.Min(c => TerrainField.GroundAt(_plan, c.X, c.Y));
        Vec3 B(double x, double y, double z) => P(x, y, z + ground);
        var wall = s.Height * 0.68;
        var walls = Mesh(new Rgb(0xE4, 0xD6, 0xBE));
        // Wände
        for (int i = 0, j = 3; i < 4; j = i++)
            walls.Quad(B(o[j].X, o[j].Y, 0), B(o[i].X, o[i].Y, 0), B(o[i].X, o[i].Y, wall), B(o[j].X, o[j].Y, wall));
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
                    win.Quad(B(ax + (bx - ax) * t0 + nx, ay + (by - ay) * t0 + ny, z0), B(ax + (bx - ax) * t1 + nx, ay + (by - ay) * t1 + ny, z0),
                             B(ax + (bx - ax) * t1 + nx, ay + (by - ay) * t1 + ny, z1), B(ax + (bx - ax) * t0 + nx, ay + (by - ay) * t0 + ny, z1));
            }
        }
        // Satteldach entlang der längeren Seite
        var roof = Mesh(new Rgb(0xA8, 0x4A, 0x36));
        var top = s.Height;
        static (double X, double Y) Mid((double X, double Y) a, (double X, double Y) b) => ((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        var (r0, r1) = s.Width >= s.Depth ? (Mid(o[0], o[3]), Mid(o[1], o[2])) : (Mid(o[0], o[1]), Mid(o[3], o[2]));
        if (s.Width >= s.Depth)
        {
            roof.Quad(B(o[0].X, o[0].Y, wall), B(o[1].X, o[1].Y, wall), B(r1.X, r1.Y, top), B(r0.X, r0.Y, top));
            roof.Quad(B(o[2].X, o[2].Y, wall), B(o[3].X, o[3].Y, wall), B(r0.X, r0.Y, top), B(r1.X, r1.Y, top));
            walls.Triangle(B(o[0].X, o[0].Y, wall), B(r0.X, r0.Y, top), B(o[3].X, o[3].Y, wall));
            walls.Triangle(B(o[1].X, o[1].Y, wall), B(r1.X, r1.Y, top), B(o[2].X, o[2].Y, wall));
        }
        else
        {
            roof.Quad(B(o[1].X, o[1].Y, wall), B(o[2].X, o[2].Y, wall), B(r1.X, r1.Y, top), B(r0.X, r0.Y, top));
            roof.Quad(B(o[3].X, o[3].Y, wall), B(o[0].X, o[0].Y, wall), B(r0.X, r0.Y, top), B(r1.X, r1.Y, top));
            walls.Triangle(B(o[0].X, o[0].Y, wall), B(r0.X, r0.Y, top), B(o[1].X, o[1].Y, wall));
            walls.Triangle(B(o[3].X, o[3].Y, wall), B(r1.X, r1.Y, top), B(o[2].X, o[2].Y, wall));
        }
    }

    private static readonly Rgb[] Leaves = [new(0x3E, 0x7A, 0x32), new(0x4F, 0x8C, 0x3A), new(0x5E, 0x94, 0x3C), new(0x36, 0x6E, 0x2C)];
    private static readonly Rgb[] Needles = [new(0x2A, 0x58, 0x2E), new(0x32, 0x64, 0x34), new(0x26, 0x4E, 0x2A)];

    /// <summary>Laubbaum mit runder, unregelmäßiger Krone oder Nadelbaum aus gestaffelten Kegeln (fest je Baum).</summary>
    /// <summary>Nadelbaum oder Laubbaum – fest je Baum, gleich im Gleisplan und in 3D.</summary>
    public static bool IsConifer(SceneryItem t) => Noise(t.Id, 3) < 0.35;

    private void Tree(SceneryItem t)
    {
        var r = t.Width / 2;
        var z0 = TerrainField.GroundAt(_plan, t.X, t.Y);
        var conifer = IsConifer(t);
        var trunk = Mesh(new Rgb(0x5E, 0x44, 0x2E));
        if (conifer)
        {
            var trunkH = t.Height * 0.18;
            trunk.Box(P(t.X - r * 0.1, t.Y - r * 0.1, z0), P(t.X + r * 0.1, t.Y + r * 0.1, z0 + trunkH));
            const int tiers = 3;
            for (var k = 0; k < tiers; k++)
            {
                var b = z0 + trunkH * 0.8 + (t.Height - trunkH) * k * 0.28;
                var top = k == tiers - 1 ? z0 + t.Height : b + (t.Height - trunkH) * 0.45;
                Cone(Mesh(Needles[(int)(Noise(t.Id, k, 5) * Needles.Length) % Needles.Length]), t.X, t.Y, b, top, r * (1 - k * 0.24), 12);
            }
            return;
        }
        var stem = t.Height * 0.38;
        trunk.Box(P(t.X - r * 0.1, t.Y - r * 0.1, z0), P(t.X + r * 0.1, t.Y + r * 0.1, z0 + stem));
        // Krone aus mehreren Kugeln
        var blobs = 3 + (int)(Noise(t.Id, 9) * 3);
        for (var k = 0; k < blobs; k++)
        {
            var a = Noise(t.Id, k, 1) * Math.PI * 2;
            var off = k == 0 ? 0 : r * 0.42;
            var size = k == 0 ? r * 0.85 : r * (0.5 + Noise(t.Id, k, 2) * 0.25);
            var cz = z0 + stem + (t.Height - stem) * (k == 0 ? 0.5 : 0.35 + Noise(t.Id, k, 3) * 0.35);
            Blob(Mesh(Leaves[(int)(Noise(t.Id, k, 4) * Leaves.Length) % Leaves.Length]), t.X + Math.Cos(a) * off, t.Y + Math.Sin(a) * off, cz, size, Math.Min(size, (t.Height - stem) * 0.5));
        }
    }

    private void Cone(SceneMesh m, double x, double y, double z0, double z1, double r, int n)
    {
        var apex = P(x, y, z1);
        for (var i = 0; i < n; i++)
        {
            double a0 = i * 2 * Math.PI / n, a1 = (i + 1) * 2 * Math.PI / n;
            var b0 = P(x + r * Math.Cos(a0), y + r * Math.Sin(a0), z0);
            var b1 = P(x + r * Math.Cos(a1), y + r * Math.Sin(a1), z0);
            m.Triangle(b0, apex, b1);
            m.Triangle(b1, P(x, y, z0), b0);
        }
    }

    /// <summary>Abgeflachte Kugel aus Breiten- und Längenkreisen.</summary>
    private void Blob(SceneMesh m, double x, double y, double z, double r, double rz)
    {
        const int lat = 5, lon = 9;
        Vec3 At(int i, int j)
        {
            double th = Math.PI * i / lat, ph = 2 * Math.PI * j / lon;
            return P(x + r * Math.Sin(th) * Math.Cos(ph), y + r * Math.Sin(th) * Math.Sin(ph), z + rz * Math.Cos(th));
        }
        for (var i = 0; i < lat; i++)
            for (var j = 0; j < lon; j++)
                m.Quad(At(i, j), At(i + 1, j), At(i + 1, j + 1), At(i, j + 1));
    }

    /// <summary>Punkte entlang eines Abschnitts mit Höhe.</summary>
    private static List<(double X, double Y, double Z, double H)> Samples(PlacedPiece p, int legIndex, Leg leg, double step, double lift)
    {
        var n = leg.IsArc ? Math.Max(2, (int)Math.Ceiling(leg.TrackLength / step)) : 1;
        var list = new List<(double, double, double, double)>();
        for (var i = 0; i <= n; i++)
        {
            var t = (double)i / n;
            var q = leg.At(t);
            list.Add((q.X, q.Y, p.HeightAt(legIndex, t) + lift, q.HeadingRad));
        }
        return list;
    }

    private void Track(PlacedPiece p, bool sleepers)
    {
        var g = _g;
        var legs = p.WorldLegs();
        var lift = RailTop(g);
        // Schotter in zwei Tönen abwechselnd, Schwellen aus Holz in drei Tönen, Schienen: blanker Kopf, rostige Flanken
        var ballastLight = Mesh(new Rgb(0x9E, 0x96, 0x8A));
        var ballastDark = Mesh(new Rgb(0x86, 0x7E, 0x74));
        var ballastSide = Mesh(new Rgb(0x7C, 0x74, 0x6A));
        var sleeperTones = new[] { Mesh(new Rgb(0x4E, 0x3E, 0x30)), Mesh(new Rgb(0x5C, 0x4A, 0x3A)), Mesh(new Rgb(0x46, 0x3A, 0x30)) };
        var rail = Mesh(RailColor, SurfaceKind.Rail);
        var railSide = Mesh(RailSideColor);
        var pier = Mesh(new Rgb(0xA8, 0xA4, 0x9C));
        var tunnel = Mesh(new Rgb(0x7A, 0x72, 0x66));
        for (var li = 0; li < legs.Count; li++)
        {
            var pts = Samples(p, li, legs[li], g * 1.5, lift);
            for (var i = 0; i + 1 < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                static (double X, double Y) N(double h) => (-Math.Sin(h), Math.Cos(h));
                var na = N(a.H);
                var nb = N(b.H);
                double bt = 0.24 * g, bb = lift;      // Schotter oben/unten unter Schienenoberkante
                double wt = 1.05 * g, wb = 1.45 * g;  // halbe Breite oben/unten
                static Vec3 At((double X, double Y, double Z, double H) s, (double X, double Y) n, double off, double dz) => P(s.X + n.X * off, s.Y + n.Y * off, s.Z - dz);
                var ballast = Noise(p.Id, li, i) < 0.5 ? ballastLight : ballastDark;
                ballast.Quad(At(a, na, -wt, bt), At(b, nb, -wt, bt), At(b, nb, wt, bt), At(a, na, wt, bt));
                ballastSide.Quad(At(a, na, wt, bt), At(b, nb, wt, bt), At(b, nb, wb, bb), At(a, na, wb, bb));
                ballastSide.Quad(At(a, na, -wb, bb), At(b, nb, -wb, bb), At(b, nb, -wt, bt), At(a, na, -wt, bt));
                if (a.Z > bb + 1)
                    ballastSide.Quad(At(a, na, -wb, bb), At(a, na, wb, bb), At(b, nb, wb, bb), At(b, nb, -wb, bb));
                foreach (var side in new[] { -0.5, 0.5 })
                {
                    double hw = 0.06 * g, rh = 0.16 * g, fw = 0.11 * g;
                    rail.Quad(At(a, na, side * g - hw, 0), At(b, nb, side * g - hw, 0), At(b, nb, side * g + hw, 0), At(a, na, side * g + hw, 0));
                    railSide.Quad(At(a, na, side * g - hw, rh), At(b, nb, side * g - hw, rh), At(b, nb, side * g - hw, 0), At(a, na, side * g - hw, 0));
                    railSide.Quad(At(a, na, side * g + hw, 0), At(b, nb, side * g + hw, 0), At(b, nb, side * g + hw, rh), At(a, na, side * g + hw, rh));
                    // Schienenfuß
                    railSide.Quad(At(a, na, side * g - fw, rh), At(b, nb, side * g - fw, rh), At(b, nb, side * g + fw, rh), At(a, na, side * g + fw, rh));
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
                    var z = p.HeightAt(li, t) + lift;
                    double cx = Math.Cos(q.HeadingRad), cy = Math.Sin(q.HeadingRad), nx = -cy, ny = cx;
                    double hl = 0.13 * g, hw = 0.92 * g;
                    var c = new[] { (-hl, -hw), (hl, -hw), (hl, hw), (-hl, hw) }.Select(o => (q.X + cx * o.Item1 + nx * o.Item2, q.Y + cy * o.Item1 + ny * o.Item2)).ToArray();
                    var sleeper = sleeperTones[(int)(Noise(p.Id, li, i + 1000) * 3) % 3];
                    double zt = z - 0.16 * g, zb = z - 0.25 * g;
                    sleeper.Quad(P(c[0].Item1, c[0].Item2, zt), P(c[1].Item1, c[1].Item2, zt), P(c[2].Item1, c[2].Item2, zt), P(c[3].Item1, c[3].Item2, zt));
                    for (var k = 0; k < 4; k++)
                    {
                        var (e0, e1) = (c[k], c[(k + 1) % 4]);
                        sleeper.Quad(P(e0.Item1, e0.Item2, zb), P(e1.Item1, e1.Item2, zb), P(e1.Item1, e1.Item2, zt), P(e0.Item1, e0.Item2, zt));
                    }
                }
            }
            // Pfeiler unter hochliegenden Gleisen
            var mid = legs[li].At(0.5);
            var zm = p.HeightAt(li, 0.5);
            var below = TerrainField.GroundAt(_plan, mid.X, mid.Y);
            if (zm - below > g * 1.5 && !p.Tunnel)
            {
                var w = g * 0.6;
                pier.Box(P(mid.X - w, mid.Y - w, below), P(mid.X + w, mid.Y + w, zm));
            }
        }
        if (p.Def.Kind == PieceKind.Buffer)
        {
            var e = legs[0].End;
            var z = p.Height + lift;
            Mesh(new Rgb(0xC4, 0x2B, 0x1C)).Box(P(e.X - g * 0.5, e.Y - g * 0.5, z), P(e.X + g * 0.5, e.Y + g * 0.5, z + g * 0.8));
        }
    }

    /// <summary>Lichtsignal nach <see cref="SignalFace"/>: Mast, Mastschild, schwarzer Schirm und Lampen an ihrer echten Stelle, dem Zug zugewandt.</summary>
    private void SignalModel(Signal s)
    {
        var (x, y, heading) = Electrics.SignalPose(_plan, s);
        var g = _g;
        var z = (_plan.Find(s.PieceId)?.Height ?? 0) + RailTop(g);
        var face = SignalFace.For(s.Type);
        var low = s.Type == SignalType.Sperrsignal;
        // Schirmbreite und Höhe der Schirmoberkante über der Schiene
        var w = low ? g * 0.42 : g * 0.48;
        var top = low ? face.TotalHeight * w : g * 4.4;
        var h = heading * Math.PI / 180;
        double rx = -Math.Sin(h), ry = Math.Cos(h);   // rechts, vom Zug aus gesehen
        double nx = -Math.Cos(h), ny = -Math.Sin(h);  // Blickrichtung des Schirms (zum Zug hin)
        Vec3 At(double fx, double fy, double depth)
            => P(x + rx * fx * w + nx * depth * w, y + ry * fx * w + ny * depth * w, z + top - fy * w);
        void Block(SceneMesh m, double fx0, double fx1, double fy0, double fy1, double d0, double d1)
        {
            Vec3 a = At(fx0, fy0, d1), b = At(fx1, fy0, d1), c = At(fx1, fy1, d1), d = At(fx0, fy1, d1);
            Vec3 a2 = At(fx0, fy0, d0), b2 = At(fx1, fy0, d0), c2 = At(fx1, fy1, d0), d2 = At(fx0, fy1, d0);
            m.Quad(a, b, c, d); m.Quad(b2, a2, d2, c2);
            m.Quad(a2, b2, b, a); m.Quad(d, c, c2, d2);
            m.Quad(a2, a, d, d2); m.Quad(b, b2, c2, c);
        }
        // Mast vom Boden bis unter den Schirm
        var mastBottom = top / w;
        Block(Mesh(SignalFace.MastColor), -0.07, 0.07, face.Height - 0.05, mastBottom, -0.2, -0.06);
        if (face.MastPlate)
        {
            var py = face.Height + SignalFace.PlateGap;
            var pw = SignalFace.PlateWidth / 2;
            for (var i = 0; i < 5; i++)
                Block(Mesh(i % 2 == 0 ? SignalFace.PlateWhite : SignalFace.PlateRed), -pw, pw,
                    py + SignalFace.PlateHeight * i / 5, py + SignalFace.PlateHeight * (i + 1) / 5, -0.06, 0.0);
        }
        Block(Mesh(SignalFace.ShieldColor), -face.Width / 2, face.Width / 2, 0, face.Height, -0.18, 0.02);
        foreach (var l in face.Lamps)
        {
            var mesh = l.Lit ? Mesh(l.Color, SurfaceKind.Lamp) : Mesh(SignalFace.Dark);
            var center = At(l.X, l.Y, 0.05);
            for (var k = 0; k < 8; k++)
            {
                double a0 = k * Math.PI / 4, a1 = (k + 1) * Math.PI / 4;
                mesh.Triangle(center, At(l.X + Math.Cos(a0) * l.R, l.Y + Math.Sin(a0) * l.R, 0.05), At(l.X + Math.Cos(a1) * l.R, l.Y + Math.Sin(a1) * l.R, 0.05));
            }
        }
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
        // Augpunkt etwa in Führerstandshöhe, Blick leicht nach unten auf das Gleis vor dem Zug
        var eye = PointAt(s);
        var ahead = PointAt(s + gauge * 10);
        var rail = Scene3DModel.RailTop(gauge);
        return (new Vec3(eye.X, eye.Z + rail + gauge * 1.9, eye.Y), new Vec3(ahead.X, ahead.Z + rail + gauge * 0.55, ahead.Y));
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
            if (d.IsTable)
            {
                // Drehscheibe, Schiebebühne: hinein bis zur Bühne, zum gegenüberliegenden (sonst zum nächsten) belegten Anschluss und hinaus
                var ports = piece.WorldConnectors();
                var linked = Enumerable.Range(0, count).Where(k => k != entry && conn.Find(piece.Id, k)?.Link is not null).ToList();
                var exitPort = linked.OrderBy(k => Math.Abs(Math.Abs(Angles.Normalize(ports[k].Heading - ports[entry].Heading)) - 180)).Cast<int?>().FirstOrDefault() ?? entry;
                var stubs = piece.WorldLegs();
                foreach (var (leg, inward) in new[] { (stubs[entry], true), (stubs[exitPort], false) })
                    for (var i = 0; i <= 4; i++)
                    {
                        var q = leg.At(inward ? i / 4.0 : 1 - i / 4.0);
                        points.Add((q.X, q.Y, piece.Height));
                    }
                length += stubs[entry].TrackLength * 2 + piece.Def.PitRadius * 2;
                if (conn.Find(piece.Id, exitPort)?.Link is { } after) { piece = plan.Find(after.PieceId)!; entry = after.Index; }
                continue;
            }
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
