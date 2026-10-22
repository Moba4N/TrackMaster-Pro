using System.Windows.Media;
using System.Windows.Media.Media3D;
using TrackMaster.Core;

namespace TrackMaster.App;

/// <summary>
/// Baut aus dem Plan ein 3D-Modell. Planachsen: x nach rechts, y nach unten, Höhe z.
/// 3D-Achsen: X = x, Y = Höhe (oben), Z = y – von oben gesehen liegt der Plan also wie auf dem Bildschirm.
/// </summary>
public sealed class Scene3D
{
    private readonly Dictionary<Color, MeshBuilder> _meshes = [];
    private readonly LayoutPlan _plan;
    private readonly double _g;

    /// <summary>Fensterlicht der Gebäude; nachts gelb, tagsüber schwarz (leuchtet nicht).</summary>
    public SolidColorBrush WindowGlow { get; } = new(Colors.Black);
    /// <summary>Signallampen leuchten immer.</summary>
    private readonly List<(MeshBuilder Mesh, Color Color)> _lamps = [];

    public Model3DGroup Model { get; } = new();

    public Scene3D(LayoutPlan plan)
    {
        _plan = plan;
        _g = plan.Gauge.TrackWidth;
        Build();
    }

    private MeshBuilder Mesh(Color c)
    {
        if (!_meshes.TryGetValue(c, out var m)) _meshes[c] = m = new MeshBuilder();
        return m;
    }

    private static Point3D P(double x, double y, double z) => new(x, z, y);

    private void Build()
    {
        var plan = _plan;
        var g = _g;
        // Platte
        var thickness = Math.Max(g * 1.5, 15);
        Mesh(Color.FromRgb(0x8E, 0xA8, 0x6E)).Box(P(0, 0, -thickness), P(plan.BoardWidth, plan.BoardHeight, 0));
        Mesh(Color.FromRgb(0x8A, 0x6B, 0x4A)).Box(P(-2, -2, -thickness * 1.6), P(plan.BoardWidth + 2, plan.BoardHeight + 2, -thickness));

        foreach (var s in plan.Scenery)
            switch (s.Kind)
            {
                case SceneryKind.Terrain: Prism(s.Points, 0, s.Height, Color.FromRgb(0x7F, 0x9E, 0x55), Color.FromRgb(0x8C, 0x74, 0x50)); break;
                case SceneryKind.Water: Prism(s.Points, 0.3, 0.8, Color.FromRgb(0x3F, 0x86, 0xC0), Color.FromRgb(0x3F, 0x86, 0xC0)); break;
                case SceneryKind.Building: Building(s); break;
                case SceneryKind.Tree: Tree(s); break;
            }

        var totalLength = plan.Pieces.Sum(p => p.Def.TrackLength);
        var sleepers = totalLength / g < 4000;
        foreach (var p in plan.Pieces) Track(p, sleepers);
        foreach (var sig in plan.Signals) SignalModel(sig);

        foreach (var (color, mesh) in _meshes)
        {
            if (mesh.IsEmpty) continue;
            var material = new MaterialGroup();
            material.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
            if (color == WaterColor) material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(0xC0, 0xD8, 0xF0)), 40));
            if (color == RailColor) material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90)), 20));
            if (color == WindowColor) material.Children.Add(new EmissiveMaterial(WindowGlow));
            Model.Children.Add(new GeometryModel3D(mesh.ToMesh(), material) { BackMaterial = new DiffuseMaterial(new SolidColorBrush(color)) });
        }
        foreach (var (mesh, color) in _lamps)
        {
            var m = new MaterialGroup();
            m.Children.Add(new DiffuseMaterial(Brushes.Black));
            m.Children.Add(new EmissiveMaterial(new SolidColorBrush(color)));
            Model.Children.Add(new GeometryModel3D(mesh.ToMesh(), m));
        }
    }

    private static readonly Color WaterColor = Color.FromRgb(0x3F, 0x86, 0xC0);
    private static readonly Color RailColor = Color.FromRgb(0x6A, 0x6E, 0x74);
    private static readonly Color WindowColor = Color.FromRgb(0x30, 0x34, 0x3A);

    /// <summary>Senkrechtes Prisma über einem Vieleck (Gelände, Wasser).</summary>
    private void Prism(IReadOnlyList<(double X, double Y)> pts, double z0, double z1, Color top, Color side)
    {
        if (pts.Count < 3) return;
        var tris = Geometry2D.Triangulate(pts);
        var m = Mesh(top);
        foreach (var (a, b, c) in tris)
            m.Triangle(P(pts[a].X, pts[a].Y, z1), P(pts[c].X, pts[c].Y, z1), P(pts[b].X, pts[b].Y, z1), doubleSided: true);
        var sm = Mesh(side);
        for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
            sm.Quad(P(pts[j].X, pts[j].Y, z0), P(pts[i].X, pts[i].Y, z0), P(pts[i].X, pts[i].Y, z1), P(pts[j].X, pts[j].Y, z1), doubleSided: true);
    }

    private void Building(SceneryItem s)
    {
        var o = s.Outline();
        var wall = s.Height * 0.68;
        var walls = Mesh(Color.FromRgb(0xE4, 0xD6, 0xBE));
        // Wände
        for (int i = 0, j = 3; i < 4; j = i++)
            walls.Quad(P(o[j].X, o[j].Y, 0), P(o[i].X, o[i].Y, 0), P(o[i].X, o[i].Y, wall), P(o[j].X, o[j].Y, wall), doubleSided: true);
        // Fenster als schmale Bänder vor den Wänden
        var win = Mesh(WindowColor);
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
                             P(ax + (bx - ax) * t1 + nx, ay + (by - ay) * t1 + ny, z1), P(ax + (bx - ax) * t0 + nx, ay + (by - ay) * t0 + ny, z1), doubleSided: true);
            }
        }
        // Satteldach entlang der längeren Seite
        var roof = Mesh(Color.FromRgb(0xA8, 0x4A, 0x36));
        var top = s.Height;
        (double X, double Y) Mid((double X, double Y) a, (double X, double Y) b) => ((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        var (r0, r1) = s.Width >= s.Depth ? (Mid(o[0], o[3]), Mid(o[1], o[2])) : (Mid(o[0], o[1]), Mid(o[3], o[2]));
        if (s.Width >= s.Depth)
        {
            roof.Quad(P(o[0].X, o[0].Y, wall), P(o[1].X, o[1].Y, wall), P(r1.X, r1.Y, top), P(r0.X, r0.Y, top), doubleSided: true);
            roof.Quad(P(o[2].X, o[2].Y, wall), P(o[3].X, o[3].Y, wall), P(r0.X, r0.Y, top), P(r1.X, r1.Y, top), doubleSided: true);
            walls.Triangle(P(o[0].X, o[0].Y, wall), P(r0.X, r0.Y, top), P(o[3].X, o[3].Y, wall), doubleSided: true);
            walls.Triangle(P(o[1].X, o[1].Y, wall), P(r1.X, r1.Y, top), P(o[2].X, o[2].Y, wall), doubleSided: true);
        }
        else
        {
            roof.Quad(P(o[1].X, o[1].Y, wall), P(o[2].X, o[2].Y, wall), P(r1.X, r1.Y, top), P(r0.X, r0.Y, top), doubleSided: true);
            roof.Quad(P(o[3].X, o[3].Y, wall), P(o[0].X, o[0].Y, wall), P(r0.X, r0.Y, top), P(r1.X, r1.Y, top), doubleSided: true);
            walls.Triangle(P(o[0].X, o[0].Y, wall), P(r0.X, r0.Y, top), P(o[1].X, o[1].Y, wall), doubleSided: true);
            walls.Triangle(P(o[3].X, o[3].Y, wall), P(r1.X, r1.Y, top), P(o[2].X, o[2].Y, wall), doubleSided: true);
        }
    }

    private void Tree(SceneryItem t)
    {
        var r = t.Width / 2;
        var trunkH = t.Height * 0.3;
        Mesh(Color.FromRgb(0x6B, 0x4A, 0x2F)).Box(P(t.X - r * 0.12, t.Y - r * 0.12, 0), P(t.X + r * 0.12, t.Y + r * 0.12, trunkH));
        var crown = Mesh(Color.FromRgb(0x3E, 0x7A, 0x32));
        const int n = 10;
        var apex = P(t.X, t.Y, t.Height);
        for (var i = 0; i < n; i++)
        {
            double a0 = i * 2 * Math.PI / n, a1 = (i + 1) * 2 * Math.PI / n;
            var b0 = P(t.X + r * Math.Cos(a0), t.Y + r * Math.Sin(a0), trunkH * 0.8);
            var b1 = P(t.X + r * Math.Cos(a1), t.Y + r * Math.Sin(a1), trunkH * 0.8);
            crown.Triangle(b0, apex, b1, doubleSided: true);
            crown.Triangle(b1, P(t.X, t.Y, trunkH * 0.8), b0, doubleSided: true);
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
        var ballast = Mesh(Color.FromRgb(0x9C, 0x92, 0x84));
        var sleeper = Mesh(Color.FromRgb(0x5A, 0x48, 0x38));
        var rail = Mesh(RailColor);
        var pier = Mesh(Color.FromRgb(0xA8, 0xA4, 0x9C));
        var tunnel = Mesh(Color.FromRgb(0x7A, 0x72, 0x66));
        for (var li = 0; li < legs.Count; li++)
        {
            var pts = Samples(p, li, legs[li], g * 1.5);
            for (var i = 0; i + 1 < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                (double X, double Y) N(double h) => (-Math.Sin(h), Math.Cos(h));
                var na = N(a.H);
                var nb = N(b.H);
                double bt = 0.22 * g, bb = 0.5 * g;   // Schotter oben/unten unter Schienenoberkante
                double wt = 1.05 * g, wb = 1.45 * g;  // halbe Breite oben/unten
                Point3D At((double X, double Y, double Z, double H) s, (double X, double Y) n, double off, double dz) => P(s.X + n.X * off, s.Y + n.Y * off, s.Z - dz);
                ballast.Quad(At(a, na, -wt, bt), At(b, nb, -wt, bt), At(b, nb, wt, bt), At(a, na, wt, bt), doubleSided: true);
                ballast.Quad(At(a, na, wt, bt), At(b, nb, wt, bt), At(b, nb, wb, bb), At(a, na, wb, bb), doubleSided: true);
                ballast.Quad(At(a, na, -wb, bb), At(b, nb, -wb, bb), At(b, nb, -wt, bt), At(a, na, -wt, bt), doubleSided: true);
                if (a.Z > bb + 1)
                    ballast.Quad(At(a, na, -wb, bb), At(a, na, wb, bb), At(b, nb, wb, bb), At(b, nb, -wb, bb), doubleSided: true);
                foreach (var side in new[] { -0.5, 0.5 })
                {
                    double hw = 0.07 * g, rh = 0.16 * g;
                    rail.Quad(At(a, na, side * g - hw, 0), At(b, nb, side * g - hw, 0), At(b, nb, side * g + hw, 0), At(a, na, side * g + hw, 0), doubleSided: true);
                    rail.Quad(At(a, na, side * g - hw, rh), At(b, nb, side * g - hw, rh), At(b, nb, side * g - hw, 0), At(a, na, side * g - hw, 0), doubleSided: true);
                    rail.Quad(At(a, na, side * g + hw, 0), At(b, nb, side * g + hw, 0), At(b, nb, side * g + hw, rh), At(a, na, side * g + hw, rh), doubleSided: true);
                }
                if (p.Tunnel)
                {
                    // Tunnelröhre als Halbkreis-Gewölbe
                    const int k = 8;
                    var r = g * 2.2;
                    for (var j = 0; j < k; j++)
                    {
                        double t0 = Math.PI * j / k, t1 = Math.PI * (j + 1) / k;
                        Point3D Arc((double X, double Y, double Z, double H) s, (double X, double Y) n, double t) => P(s.X + n.X * r * Math.Cos(t), s.Y + n.Y * r * Math.Cos(t), s.Z - bb + r * Math.Sin(t) * 1.15);
                        tunnel.Quad(Arc(a, na, t0), Arc(b, nb, t0), Arc(b, nb, t1), Arc(a, na, t1), doubleSided: true);
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
                    sleeper.Quad(P(c[0].Item1, c[0].Item2, z - 0.16 * g), P(c[1].Item1, c[1].Item2, z - 0.16 * g), P(c[2].Item1, c[2].Item2, z - 0.16 * g), P(c[3].Item1, c[3].Item2, z - 0.16 * g), doubleSided: true);
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
            Mesh(Color.FromRgb(0xC4, 0x2B, 0x1C)).Box(P(e.X - g * 0.5, e.Y - g * 0.5, z), P(e.X + g * 0.5, e.Y + g * 0.5, z + g * 0.8));
        }
    }

    private void SignalModel(Signal s)
    {
        var (x, y, _) = Electrics.SignalPose(_plan, s);
        var z = _plan.Find(s.PieceId)?.Height ?? 0;
        var g = _g;
        var height = s.Type == SignalType.Sperrsignal ? g * 1.2 : g * 4;
        Mesh(Color.FromRgb(0x50, 0x50, 0x50)).Box(P(x - g * 0.08, y - g * 0.08, z), P(x + g * 0.08, y + g * 0.08, z + height));
        Mesh(Color.FromRgb(0x20, 0x20, 0x20)).Box(P(x - g * 0.3, y - g * 0.3, z + height - g * 0.2), P(x + g * 0.3, y + g * 0.3, z + height + g * 0.9));
        var (top, bottom) = PlanScene.SignalColors(s.Type);
        var lamp = new MeshBuilder();
        lamp.Box(P(x - g * 0.32, y - g * 0.32, z + height + g * 0.45), P(x + g * 0.32, y + g * 0.32, z + height + g * 0.7));
        _lamps.Add((lamp, ((SolidColorBrush)top).Color));
        var lamp2 = new MeshBuilder();
        lamp2.Box(P(x - g * 0.32, y - g * 0.32, z + height + g * 0.05), P(x + g * 0.32, y + g * 0.32, z + height + g * 0.3));
        _lamps.Add((lamp2, ((SolidColorBrush)bottom).Color));
    }
}

/// <summary>Sammelt Dreiecke für ein MeshGeometry3D.</summary>
public sealed class MeshBuilder
{
    private readonly Point3DCollection _points = [];
    private readonly Int32Collection _indices = [];

    public bool IsEmpty => _indices.Count == 0;

    public void Triangle(Point3D a, Point3D b, Point3D c, bool doubleSided = false)
    {
        var i = _points.Count;
        _points.Add(a); _points.Add(b); _points.Add(c);
        _indices.Add(i); _indices.Add(i + 1); _indices.Add(i + 2);
        if (doubleSided) { _indices.Add(i); _indices.Add(i + 2); _indices.Add(i + 1); }
    }

    public void Quad(Point3D a, Point3D b, Point3D c, Point3D d, bool doubleSided = false)
    {
        Triangle(a, b, c, doubleSided);
        Triangle(a, c, d, doubleSided);
    }

    public void Box(Point3D min, Point3D max)
    {
        double x0 = Math.Min(min.X, max.X), x1 = Math.Max(min.X, max.X);
        double y0 = Math.Min(min.Y, max.Y), y1 = Math.Max(min.Y, max.Y);
        double z0 = Math.Min(min.Z, max.Z), z1 = Math.Max(min.Z, max.Z);
        Point3D P(double x, double y, double z) => new(x, y, z);
        Quad(P(x0, y1, z0), P(x0, y1, z1), P(x1, y1, z1), P(x1, y1, z0)); // oben
        Quad(P(x0, y0, z0), P(x1, y0, z0), P(x1, y0, z1), P(x0, y0, z1)); // unten
        Quad(P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1)); // vorne
        Quad(P(x1, y0, z0), P(x0, y0, z0), P(x0, y1, z0), P(x1, y1, z0)); // hinten
        Quad(P(x1, y0, z1), P(x1, y0, z0), P(x1, y1, z0), P(x1, y1, z1)); // rechts
        Quad(P(x0, y0, z0), P(x0, y0, z1), P(x0, y1, z1), P(x0, y1, z0)); // links
    }

    public MeshGeometry3D ToMesh()
    {
        var mesh = new MeshGeometry3D { Positions = _points, TriangleIndices = _indices };
        mesh.Freeze();
        return mesh;
    }
}
