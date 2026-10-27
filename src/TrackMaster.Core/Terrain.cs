namespace TrackMaster.Core;

/// <summary>
/// Höhenfeld einer Höhenfläche: am Rand auf Plattenhöhe, nach innen mit weichem Hang bis zur eingestellten Höhe.
/// Grundlage für die Hügel in der 3D-Ansicht und für Höhenlinien und Höhenstufen im Gleisplan.
/// </summary>
public sealed class TerrainField
{
    public double X0 { get; }
    public double Y0 { get; }
    public double Cell { get; }
    public int Nx { get; }
    public int Ny { get; }
    public double Height { get; }
    private readonly double[] _h;

    private static readonly Dictionary<string, TerrainField> Cache = [];

    public TerrainField(IReadOnlyList<(double X, double Y)> poly, double height)
    {
        Height = Math.Max(0, height);
        if (poly.Count < 3) { Cell = 1; _h = new double[4]; Nx = Ny = 1; return; }
        double minX = poly.Min(p => p.X), maxX = poly.Max(p => p.X), minY = poly.Min(p => p.Y), maxY = poly.Max(p => p.Y);
        Cell = Math.Max(Math.Max(maxX - minX, maxY - minY) / 44, 2);
        // Ein Feld Rand ringsum, damit alle Höhenlinien geschlossen sind
        X0 = minX - Cell;
        Y0 = minY - Cell;
        Nx = (int)Math.Ceiling((maxX - minX) / Cell) + 2;
        Ny = (int)Math.Ceiling((maxY - minY) / Cell) + 2;
        _h = new double[(Nx + 1) * (Ny + 1)];
        var dist = new double[_h.Length];
        for (var j = 0; j <= Ny; j++)
            for (var i = 0; i <= Nx; i++)
            {
                double x = X0 + i * Cell, y = Y0 + j * Cell;
                dist[j * (Nx + 1) + i] = Geometry2D.PointInPolygon(poly, x, y) ? EdgeDistance(poly, x, y) : -1;
            }
        // Hangbreite: etwa 1 : 2,2 wie eine natürliche Böschung; schmale Flächen werden steiler, erreichen aber ihre Höhe
        var falloff = Math.Max(Math.Min(Height * 2.2, dist.Max() * 0.75), 1);
        for (var k = 0; k < _h.Length; k++)
        {
            if (dist[k] < 0) continue;
            var t = Math.Clamp(dist[k] / falloff, 0, 1);
            _h[k] = Height * t * t * (3 - 2 * t);
        }
    }

    /// <summary>Höhenfeld einer Höhenfläche; wird je Form und Höhe nur einmal berechnet.</summary>
    public static TerrainField For(SceneryItem s)
    {
        var key = $"{s.Height:R}|{string.Join(";", s.Points.Select(p => $"{p.X:R},{p.Y:R}"))}";
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var f)) return f;
            if (Cache.Count > 64) Cache.Clear();
            return Cache[key] = new TerrainField(s.Points, s.Height);
        }
    }

    /// <summary>Höhe der Landschaft an einer Stelle: höchste der Höhenflächen, sonst 0 (Platte).</summary>
    public static double GroundAt(LayoutPlan plan, double x, double y)
    {
        double z = 0;
        foreach (var s in plan.Scenery)
            if (s.Kind == SceneryKind.Terrain && s.Points.Count >= 3) z = Math.Max(z, For(s).At(x, y));
        return z;
    }

    public double Vertex(int i, int j) => _h[j * (Nx + 1) + i];

    /// <summary>Höhe an einer beliebigen Stelle (bilinear).</summary>
    public double At(double x, double y)
    {
        double fx = (x - X0) / Cell, fy = (y - Y0) / Cell;
        if (fx < 0 || fy < 0 || fx >= Nx || fy >= Ny) return 0;
        int i = (int)fx, j = (int)fy;
        double tx = fx - i, ty = fy - j;
        return (Vertex(i, j) * (1 - tx) + Vertex(i + 1, j) * tx) * (1 - ty) + (Vertex(i, j + 1) * (1 - tx) + Vertex(i + 1, j + 1) * tx) * ty;
    }

    private static double EdgeDistance(IReadOnlyList<(double X, double Y)> poly, double x, double y)
    {
        var best = double.MaxValue;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var (ax, ay) = poly[j];
            var (bx, by) = poly[i];
            double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
            var t = l2 < 1e-12 ? 0 : Math.Clamp(((x - ax) * dx + (y - ay) * dy) / l2, 0, 1);
            double px = ax + dx * t - x, py = ay + dy * t - y;
            best = Math.Min(best, px * px + py * py);
        }
        return Math.Sqrt(best);
    }

    /// <summary>Abstand der Höhenlinien: ein runder Wert, so dass es etwa vier bis acht Linien gibt.</summary>
    public static double ContourStep(double height)
    {
        foreach (var s in new double[] { 5, 10, 20, 25, 50, 100, 200 })
            if (height / s <= 8) return s;
        return 500;
    }

    /// <summary>Geschlossene Höhenlinien auf einer Höhe (Marching Squares). Innen liegt alles, was mindestens so hoch ist.</summary>
    public List<List<(double X, double Y)>> Contours(double level)
    {
        var stride = Nx + 1;
        long H(int i, int j) => (long)(j * stride + i) * 2;     // waagerechte Kante (i,j)–(i+1,j)
        long V(int i, int j) => (long)(j * stride + i) * 2 + 1; // senkrechte Kante (i,j)–(i,j+1)
        bool Up(int i, int j) => Vertex(i, j) >= level;
        (double X, double Y) Point(long id)
        {
            var v = (int)(id / 2);
            int i = v % stride, j = v / stride;
            var (i2, j2) = id % 2 == 0 ? (i + 1, j) : (i, j + 1);
            double a = Vertex(i, j), b = Vertex(i2, j2);
            var t = Math.Abs(b - a) < 1e-12 ? 0.5 : Math.Clamp((level - a) / (b - a), 0, 1);
            return (X0 + (i + (i2 - i) * t) * Cell, Y0 + (j + (j2 - j) * t) * Cell);
        }
        var links = new Dictionary<long, List<long>>();
        void Link(long a, long b)
        {
            if (!links.TryGetValue(a, out var la)) links[a] = la = [];
            if (!links.TryGetValue(b, out var lb)) links[b] = lb = [];
            la.Add(b);
            lb.Add(a);
        }
        for (var j = 0; j < Ny; j++)
            for (var i = 0; i < Nx; i++)
            {
                bool c0 = Up(i, j), c1 = Up(i + 1, j), c2 = Up(i + 1, j + 1), c3 = Up(i, j + 1);
                var edges = new List<long>(4);
                if (c0 != c1) edges.Add(H(i, j));
                if (c1 != c2) edges.Add(V(i + 1, j));
                if (c3 != c2) edges.Add(H(i, j + 1));
                if (c0 != c3) edges.Add(V(i, j));
                if (edges.Count == 2) Link(edges[0], edges[1]);
                else if (edges.Count == 4)
                {
                    // Sattel: nach der Höhe in der Zellmitte auflösen
                    var center = (Vertex(i, j) + Vertex(i + 1, j) + Vertex(i + 1, j + 1) + Vertex(i, j + 1)) / 4 >= level;
                    if (center == c0) { Link(edges[0], edges[1]); Link(edges[2], edges[3]); }
                    else { Link(edges[0], edges[3]); Link(edges[1], edges[2]); }
                }
            }
        var loops = new List<List<(double X, double Y)>>();
        var done = new HashSet<long>();
        foreach (var start in links.Keys)
        {
            if (done.Contains(start)) continue;
            var loop = new List<(double X, double Y)>();
            long prev = -1, cur = start;
            while (done.Add(cur))
            {
                loop.Add(Point(cur));
                var next = links[cur].FirstOrDefault(n => n != prev && !done.Contains(n), -1);
                if (next < 0) break;
                prev = cur;
                cur = next;
            }
            if (loop.Count >= 3) loops.Add(loop);
        }
        return loops;
    }

    private List<(double Level, List<List<(double X, double Y)>> Loops)>? _layers;

    /// <summary>Höhenstufen für den Gleisplan: je Höhenlinie die Flächen, die mindestens so hoch liegen.</summary>
    public IReadOnlyList<(double Level, List<List<(double X, double Y)>> Loops)> Layers()
    {
        if (_layers is not null) return _layers;
        var list = new List<(double, List<List<(double X, double Y)>>)>();
        var step = ContourStep(Height);
        for (var level = step; level < Height - step * 0.2; level += step)
            list.Add((level, Contours(level)));
        return _layers = list;
    }

    /// <summary>Farbe einer Höhenstufe im Gleisplan: von Wiesengrün unten zu hellem Erdbraun oben.</summary>
    public static Rgb TintAt(double z)
    {
        var t = Math.Clamp(z / 300, 0, 1);
        return t < 0.5
            ? Rgb.Mix(new Rgb(0xB9, 0xD0, 0x96), new Rgb(0xC9, 0xC8, 0x8E), t * 2)
            : Rgb.Mix(new Rgb(0xC9, 0xC8, 0x8E), new Rgb(0xC4, 0xAA, 0x82), (t - 0.5) * 2);
    }

    public static readonly Rgb ContourColor = new(0x8A, 0x72, 0x50);
}
