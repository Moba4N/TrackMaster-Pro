namespace TrackMaster.Core;

/// <summary>
/// Einfacher Zug für die 3D-Ansicht: Lok und Wagen als Kästen, die auf dem Fahrweg laufen. Länge der Wagen aus der
/// Fahrzeuglänge des Plans, Breite und Höhe aus dem Maßstab (Regelspur 2,9 m × 3,9 m, Schmalspur 2,65 m × 3,5 m).
/// </summary>
public sealed class Train3D
{
    public static readonly Rgb LocoColor = new(0xB0, 0x2A, 0x24);
    public static readonly Rgb CoachColor = new(0x2E, 0x5E, 0x3A);
    public static readonly Rgb RoofColor = new(0x5A, 0x5E, 0x62);
    public static readonly Rgb UnderColor = new(0x2A, 0x2A, 0x2A);

    private readonly double _gauge, _width, _height, _locoLength, _coachLength, _gap;
    public int Coaches { get; }
    public double Length => _locoLength + Coaches * (_coachLength + _gap);

    public Train3D(LayoutPlan plan, int coaches = 3)
    {
        var g = plan.Gauge;
        var divisor = VehicleDefaults.ScaleDivisor(g);
        var narrow = VehicleDefaults.IsNarrow(g);
        _gauge = g.TrackWidth;
        _width = (narrow ? 2650 : 2900) / divisor;
        _height = (narrow ? 3500 : 3900) / divisor;
        _coachLength = plan.EffectiveVehicleLength;
        _locoLength = _coachLength * 0.65;
        _gap = _coachLength * 0.03;
        Coaches = coaches;
    }

    /// <summary>Dreiecke des Zugs, dessen Spitze bei Wegstrecke <paramref name="front"/> steht.</summary>
    public Dictionary<(Rgb Color, SurfaceKind Kind), SceneMesh> Build(RidePath path, double front)
    {
        var meshes = new Dictionary<(Rgb, SurfaceKind), SceneMesh>();
        SceneMesh M(Rgb c, SurfaceKind k = SurfaceKind.Matte) => meshes.TryGetValue((c, k), out var m) ? m : meshes[(c, k)] = new SceneMesh();
        var rail = Scene3DModel.RailTop(_gauge);
        var s = front;
        for (var i = 0; i <= Coaches; i++)
        {
            var loco = i == 0;
            var len = loco ? _locoLength : _coachLength;
            // Drehgestelle bei 15 % und 85 % der Länge; der Kasten liegt auf der Sehne dazwischen
            var a = path.PointAt(s - len * 0.15);
            var b = path.PointAt(s - len * 0.85);
            var pa = new Vec3(a.X, a.Z + rail, a.Y);
            var pb = new Vec3(b.X, b.Z + rail, b.Y);
            var f = (pa - pb).Normalized();
            if (f.Length < 0.5) f = new Vec3(1, 0, 0);
            var r = Vec3.Cross(f, new Vec3(0, 1, 0)).Normalized();
            var u = Vec3.Cross(r, f).Normalized();
            var c = (pa + pb) * 0.5;
            var body = loco ? LocoColor : CoachColor;
            var floor = _height * 0.22;
            OrientedBox(M(UnderColor), c, f, r, u, len * 0.42, _width * 0.4, _height * 0.05, floor);
            OrientedBox(M(body), c, f, r, u, len / 2, _width / 2, floor, _height * 0.88);
            OrientedBox(M(RoofColor), c, f, r, u, len / 2 * 0.98, _width / 2 * 0.86, _height * 0.88, _height);
            // Fensterband (leuchtet abends) an beiden Seiten, bei der Lok nur der Führerstand vorne
            var (w0, w1) = loco ? (len * 0.28, len * 0.46) : (-len * 0.42, len * 0.42);
            foreach (var side in new[] { 1.0, -1.0 })
            {
                var o = r * (side * (_width / 2 + 0.05));
                Vec3 P(double along, double up) => c + o + f * along + u * up;
                var win = M(Scene3DModel.WindowColor, SurfaceKind.Window);
                win.Quad(P(w0, _height * 0.5), P(w1, _height * 0.5), P(w1, _height * 0.74), P(w0, _height * 0.74));
            }
            if (loco)
            {
                // Stirnfenster und Spitzenlicht
                Vec3 Q(double side, double up) => c + f * (len / 2 + 0.05) + r * side + u * up;
                M(Scene3DModel.WindowColor, SurfaceKind.Window).Quad(Q(-_width * 0.38, _height * 0.52), Q(_width * 0.38, _height * 0.52), Q(_width * 0.38, _height * 0.74), Q(-_width * 0.38, _height * 0.74));
                foreach (var side in new[] { -0.3, 0.3 })
                    M(new Rgb(0xFF, 0xF4, 0xD0), SurfaceKind.Lamp).Quad(Q(side * _width - 1, _height * 0.28), Q(side * _width + 1, _height * 0.28), Q(side * _width + 1, _height * 0.33), Q(side * _width - 1, _height * 0.33));
            }
            s -= len + _gap;
        }
        return meshes;
    }

    /// <summary>Quader um <paramref name="c"/> (auf Schienenoberkante) mit Richtung f, Seite r, oben u.</summary>
    private static void OrientedBox(SceneMesh m, Vec3 c, Vec3 f, Vec3 r, Vec3 u, double halfLength, double halfWidth, double z0, double z1)
    {
        Vec3 P(double x, double y, double z) => c + f * x + r * y + u * z;
        double x0 = -halfLength, x1 = halfLength, y0 = -halfWidth, y1 = halfWidth;
        m.Quad(P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1));
        m.Quad(P(x0, y0, z0), P(x0, y1, z0), P(x1, y1, z0), P(x1, y0, z0));
        m.Quad(P(x0, y0, z0), P(x1, y0, z0), P(x1, y0, z1), P(x0, y0, z1));
        m.Quad(P(x0, y1, z0), P(x0, y1, z1), P(x1, y1, z1), P(x1, y1, z0));
        m.Quad(P(x1, y0, z0), P(x1, y1, z0), P(x1, y1, z1), P(x1, y0, z1));
        m.Quad(P(x0, y0, z0), P(x0, y0, z1), P(x0, y1, z1), P(x0, y1, z0));
    }
}
