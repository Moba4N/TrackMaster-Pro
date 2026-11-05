namespace TrackMaster.Core;

/// <summary>Wie ein Modell der Gleishöhe folgt.</summary>
public enum TrackHeightMode
{
    /// <summary>Gar nicht (freie Modelle: auf Gelände, Modul und Ebene).</summary>
    None,
    /// <summary>Wächst nach oben mit, die Grundfläche bleibt am Boden (Stützmauer, Bahnsteig, Tunnelportal).</summary>
    Stretch,
    /// <summary>Steht als Ganzes auf Gleishöhe (Lokschuppen, Bahnübergang, Prellbock).</summary>
    Lift,
    /// <summary>Reicht vom Boden bis unter das Gleis (Brücke).</summary>
    Top,
}

/// <summary>Fläche oder Linie eines Modells im Gleisplan (Weltkoordinaten in mm).</summary>
public sealed record ModelPlanShape(IReadOnlyList<(double X, double Y)> Points, Rgb Color, bool Closed);

/// <summary>
/// Ein Modell an seinem Platz im Plan: rechnet Modellkoordinaten (Meter im Vorbild) in Weltkoordinaten (mm) um,
/// gestreckt auf Breite, Tiefe und Höhe des Objekts. Folgt das Objekt einem Gleisbogen (<see cref="SceneryItem.Points"/>),
/// wird das Modell entlang dieser Linie gebogen.
/// </summary>
public sealed class PlacedModel
{
    public ModelDefinition Def { get; }
    public SceneryItem Item { get; }
    private readonly double _x0, _x1, _cx, _cy, _sx, _sy, _sz;
    private readonly double _h;
    /// <summary>Gleishöhe über dem Boden: je Stützpunkt der Linie oder ein Wert für das ganze Modell (leer = kein Gleisbezug).</summary>
    private readonly double[] _rise = [];
    private readonly TrackHeightMode _mode;
    private readonly IReadOnlyList<(double X, double Y)> _path;
    private readonly double[] _along = [];
    private readonly (double X, double Y)[] _normals = [];

    public PlacedModel(ModelDefinition def, SceneryItem item, double mmPerMeter, double[]? rise = null)
    {
        _mode = rise is { Length: > 0 } ? ModelPlacement.ModeOf(def) : TrackHeightMode.None;
        Def = def;
        Item = item;
        var b = def.Bounds();
        _x0 = b.X0; _x1 = b.X1;
        _cx = (b.X0 + b.X1) / 2; _cy = (b.Y0 + b.Y1) / 2;
        double w = b.X1 - b.X0, d = b.Y1 - b.Y0, h = b.Z1;
        _sx = item.Width > 0 ? item.Width / w : mmPerMeter;
        _sy = item.Depth > 0 ? item.Depth / d : mmPerMeter;
        _sz = item.Height > 0 ? item.Height / h : mmPerMeter;
        _h = Math.Max(h, 1e-9);
        _path = item.Points.Count >= 2 ? item.Points : [];
        // je Stützpunkt (gebogen), gleichmäßig entlang der Länge (gerade) oder ein Wert
        if (_mode != TrackHeightMode.None && (rise!.Length == 1 || (_path.Count >= 2 ? rise.Length == _path.Count : rise.Length >= 2))) _rise = rise;
        else _mode = TrackHeightMode.None;
        if (_path.Count >= 2)
        {
            _along = new double[_path.Count];
            for (var i = 1; i < _path.Count; i++)
                _along[i] = _along[i - 1] + Dist(_path[i - 1], _path[i]);
            if (_along[^1] < 1e-6) _path = [];
            else
            {
                _normals = new (double, double)[_path.Count];
                for (var i = 0; i < _path.Count; i++)
                {
                    var a = _path[Math.Max(0, i - 1)];
                    var c = _path[Math.Min(_path.Count - 1, i + 1)];
                    var l = Math.Max(Dist(a, c), 1e-9);
                    _normals[i] = (-(c.Y - a.Y) / l, (c.X - a.X) / l);
                }
            }
        }
    }

    /// <summary>Wird das Modell entlang einer Linie gebogen?</summary>
    public bool Bent => _path.Count >= 2;

    private static double Dist((double X, double Y) a, (double X, double Y) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>Modellpunkt (Meter) → Weltpunkt im Plan (mm).</summary>
    public (double X, double Y) Map(double x, double y)
    {
        if (Bent)
        {
            var s = Math.Clamp((x - _x0) / Math.Max(_x1 - _x0, 1e-9), 0, 1) * _along[^1];
            var i = Array.BinarySearch(_along, s);
            if (i < 0) i = ~i - 1;
            i = Math.Clamp(i, 0, _path.Count - 2);
            var t = (s - _along[i]) / Math.Max(_along[i + 1] - _along[i], 1e-9);
            var (ax, ay) = _path[i]; var (bx, by) = _path[i + 1];
            var (n0, n1) = (_normals[i], _normals[i + 1]);
            double nx = n0.X + (n1.X - n0.X) * t, ny = n0.Y + (n1.Y - n0.Y) * t;
            var nl = Math.Max(Math.Sqrt(nx * nx + ny * ny), 1e-9);
            var o = (y - _cy) * _sy;
            return (ax + (bx - ax) * t + nx / nl * o, ay + (by - ay) * t + ny / nl * o);
        }
        var r = Item.Rotation * Math.PI / 180;
        double c = Math.Cos(r), sn = Math.Sin(r), lx = (x - _cx) * _sx, ly = (y - _cy) * _sy;
        return (Item.X + lx * c - ly * sn, Item.Y + lx * sn + ly * c);
    }

    public double MapZ(double z) => z * _sz;

    /// <summary>
    /// Höhe eines Modellpunkts (Meter) über dem Boden in mm. Folgt das Modell der Gleishöhe, wächst es dort nach oben:
    /// die Grundfläche bleibt am Boden, alles darüber hebt sich um die Gleishöhe an dieser Stelle.
    /// </summary>
    public double MapZ(double x, double z)
    {
        if (_mode == TrackHeightMode.None) return z * _sz;
        var r = RiseAt(x);
        return _mode switch
        {
            // Mauer, Bahnsteig, Portal: unten am Boden, alles darüber um die Gleishöhe angehoben
            TrackHeightMode.Stretch => z <= 1e-9 ? 0 : z * _sz + r,
            // Lokschuppen, Bahnübergang, Prellbock: steht auf Gleishöhe
            TrackHeightMode.Lift => z * _sz + r,
            // Brücke: reicht vom Boden bis unter das Gleis
            TrackHeightMode.Top => r > 1 ? z / _h * r : z * _sz,
            _ => z * _sz,
        };
    }

    /// <summary>Gleishöhe über dem Boden an der Modellstelle x (Meter).</summary>
    public double RiseAt(double x)
    {
        if (_rise.Length == 0) return 0;
        if (_rise.Length == 1) return _rise[0];
        var f = Math.Clamp((x - _x0) / Math.Max(_x1 - _x0, 1e-9), 0, 1);
        if (!Bent || _rise.Length != _path.Count)
        {
            var u = f * (_rise.Length - 1);
            var j = Math.Min((int)u, _rise.Length - 2);
            return _rise[j] + (_rise[j + 1] - _rise[j]) * (u - j);
        }
        var s = f * _along[^1];
        var i = Array.BinarySearch(_along, s);
        if (i < 0) i = ~i - 1;
        i = Math.Clamp(i, 0, _path.Count - 2);
        var t = (s - _along[i]) / Math.Max(_along[i + 1] - _along[i], 1e-9);
        return _rise[i] + (_rise[i + 1] - _rise[i]) * t;
    }

    /// <summary>Größte Länge eines Dreiecks entlang x, bevor es zum Biegen geteilt wird (Meter).</summary>
    private double BendStep
    {
        get
        {
            var parts = Math.Clamp(_path.Count * 2, 8, 200);
            return (_x1 - _x0) / parts;
        }
    }

    /// <summary>Dreiecke in Weltkoordinaten (x, y im Plan in mm, z über <paramref name="ground"/>), je mit Farbe.</summary>
    public IEnumerable<(Rgb Color, SurfaceKind Kind, (double X, double Y, double Z) A, (double X, double Y, double Z) B, (double X, double Y, double Z) C)> WorldTriangles(double ground)
    {
        var main = ModelBody.ParseColor(Item.Color);
        var step = BendStep;
        foreach (var (color, kind, t) in ModelMesh.Triangles(Def, main))
            foreach (var p in Bent ? Split(t, step) : [t])
            {
                (double, double, double) W((double X, double Y, double Z) q) { var (x, y) = Map(q.X, q.Y); return (x, y, ground + MapZ(q.X, q.Z)); }
                yield return (color, kind, W(p.A), W(p.B), W(p.C));
            }
    }

    /// <summary>Teilt ein Dreieck, bis keine Kante länger als <paramref name="step"/> entlang x ist (zum Biegen).</summary>
    private static IEnumerable<ModelTriangle> Split(ModelTriangle t, double step)
    {
        var stack = new Stack<ModelTriangle>();
        stack.Push(t);
        var guard = 0;
        while (stack.Count > 0)
        {
            var q = stack.Pop();
            double ab = Math.Abs(q.A.X - q.B.X), bc = Math.Abs(q.B.X - q.C.X), ca = Math.Abs(q.C.X - q.A.X);
            var m = Math.Max(ab, Math.Max(bc, ca));
            if (m <= step || guard++ > 20000) { yield return q; continue; }
            static (double, double, double) Mid((double X, double Y, double Z) a, (double X, double Y, double Z) b) => ((a.X + b.X) / 2, (a.Y + b.Y) / 2, (a.Z + b.Z) / 2);
            if (m == ab) { var p = Mid(q.A, q.B); stack.Push(new(q.A, p, q.C)); stack.Push(new(p, q.B, q.C)); }
            else if (m == bc) { var p = Mid(q.B, q.C); stack.Push(new(q.A, q.B, p)); stack.Push(new(q.A, p, q.C)); }
            else { var p = Mid(q.C, q.A); stack.Push(new(q.A, q.B, p)); stack.Push(new(p, q.B, q.C)); }
        }
    }

    /// <summary>Teilt eine Linie in Modellkoordinaten zum Biegen in kurze Stücke.</summary>
    private IEnumerable<(double X, double Y)> Dense((double X, double Y) a, (double X, double Y) b)
    {
        var n = Bent ? Math.Clamp((int)Math.Ceiling(Math.Abs(b.X - a.X) / BendStep), 1, 400) : 1;
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / n;
            yield return (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }
    }

    /// <summary>Flächen (von unten nach oben) und Dachlinien für den Gleisplan.</summary>
    public IReadOnlyList<ModelPlanShape> PlanShapes()
    {
        var main = ModelBody.ParseColor(Item.Color);
        var result = new List<ModelPlanShape>();
        foreach (var b in Def.Bodies.Where(b => !b.IsDetail).OrderBy(ModelMesh.TopOf))
        {
            var (color, kind) = ModelMesh.Surface(Def, b, main);
            if (kind == SurfaceKind.Window) color = new Rgb(0x9C, 0xC4, 0xE0);
            var fp = ModelMesh.Footprint(b);
            if (fp.Count < 3) continue;
            var pts = new List<(double X, double Y)>();
            for (var i = 0; i < fp.Count; i++)
                pts.AddRange(Dense(fp[i], fp[(i + 1) % fp.Count]).Select(p => Map(p.X, p.Y)));
            result.Add(new ModelPlanShape(pts, color, true));
            foreach (var (a, e) in ModelMesh.RoofLines(b))
                result.Add(new ModelPlanShape([.. Dense(a, e).Select(p => Map(p.X, p.Y)), Map(e.X, e.Y)], color.Scale(0.6), false));
        }
        return result;
    }

    /// <summary>Umriss eines Objekts entlang einer Mittellinie: links und rechts um die halbe Tiefe versetzt.</summary>
    public static IReadOnlyList<(double X, double Y)> PathOutline(IReadOnlyList<(double X, double Y)> path, double depth)
    {
        var n = path.Count;
        var left = new List<(double X, double Y)>(n);
        var right = new List<(double X, double Y)>(n);
        var h = Math.Max(depth, 1) / 2;
        for (var i = 0; i < n; i++)
        {
            var a = path[Math.Max(0, i - 1)];
            var c = path[Math.Min(n - 1, i + 1)];
            var l = Math.Max(Dist(a, c), 1e-9);
            double nx = -(c.Y - a.Y) / l, ny = (c.X - a.X) / l;
            left.Add((path[i].X - nx * h, path[i].Y - ny * h));
            right.Add((path[i].X + nx * h, path[i].Y + ny * h));
        }
        right.Reverse();
        return [.. left, .. right];
    }

    /// <summary>Länge einer Linie.</summary>
    public static double PathLength(IReadOnlyList<(double X, double Y)> path)
    {
        double l = 0;
        for (var i = 1; i < path.Count; i++) l += Dist(path[i - 1], path[i]);
        return l;
    }
}
