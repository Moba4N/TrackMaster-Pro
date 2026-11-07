namespace TrackMaster.Core;

/// <summary>Ein Dreieck in einer Ansicht des Modell-Editors: Bildschirmlage (v nach unten), Tiefe, schattierte Farbe, Körper.</summary>
public readonly record struct DrawnTriangle(int Body, double Depth, (double U, double V) A, (double U, double V) B, (double U, double V) C, Rgb Color);

/// <summary>
/// Zeichnet ein Modell für Editor-Ansichten ohne 3D-Grafik: Dreiecke projiziert, nach Tiefe sortiert (hinten zuerst) und nach
/// ihrer Neigung zum Licht schattiert. Für die Rissansichten und eine drehbare Schrägansicht.
/// </summary>
public static class ModelDrawing
{
    /// <summary>Schrägansicht: Drehung um die Hochachse (<paramref name="yaw"/>) und Neigung von oben (<paramref name="pitch"/>), in Grad.</summary>
    public static Func<(double X, double Y, double Z), (double U, double V, double Depth)> Orbit(double yaw, double pitch)
    {
        double cy = Math.Cos(yaw * Math.PI / 180), sy = Math.Sin(yaw * Math.PI / 180);
        double cp = Math.Cos(pitch * Math.PI / 180), sp = Math.Sin(pitch * Math.PI / 180);
        return p =>
        {
            // um z drehen; Betrachter schaut aus +y-Richtung (Vorderseite) und von oben
            double x = p.X * cy - p.Y * sy, y = p.X * sy + p.Y * cy;
            return (x, -(p.Z * cp - y * sp), y * cp + p.Z * sp);
        };
    }

    public static Func<(double X, double Y, double Z), (double U, double V, double Depth)> Ortho(EditorView view) => p => ModelEditor.Project(view, p);

    /// <summary>Dreiecke aller Körper, hinten zuerst. Gewählte Körper werden mit <paramref name="highlight"/> getönt.</summary>
    public static List<DrawnTriangle> Draw(ModelDefinition def, Func<(double X, double Y, double Z), (double U, double V, double Depth)> project,
        ICollection<int>? selected = null, Rgb? highlight = null, Rgb? mainColor = null)
    {
        var list = new List<DrawnTriangle>();
        for (var i = 0; i < def.Bodies.Count; i++)
        {
            var body = def.Bodies[i];
            var (rgb, _) = ModelMesh.Surface(def, body, mainColor);
            if (highlight is { } h && selected?.Contains(i) == true) rgb = Mix(rgb, h, 0.3);
            foreach (var t in ModelMesh.Body(body))
            {
                var (a, b, c) = (project(t.A), project(t.B), project(t.C));
                var area = (b.U - a.U) * (c.V - a.V) - (c.U - a.U) * (b.V - a.V);
                if (Math.Abs(area) < 1e-10) continue;
                // Schattierung: Neigung der Fläche zur Blickrichtung (Tiefenachse der Projektion)
                double ux = b.U - a.U, uy = b.V - a.V, uz = b.Depth - a.Depth, vx = c.U - a.U, vy = c.V - a.V, vz = c.Depth - a.Depth;
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                var len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                var facing = len < 1e-12 ? 1 : Math.Abs(nz) / len;
                // etwas Licht von oben links für die Schrägansicht
                var light = len < 1e-12 ? 0 : (-nx * 0.3 - ny * 0.5) / len;
                var shade = Math.Clamp(0.6 + 0.32 * facing + 0.12 * Math.Abs(light), 0, 1);
                list.Add(new DrawnTriangle(i, (a.Depth + b.Depth + c.Depth) / 3 + (body.IsDetail ? 0.02 : 0), (a.U, a.V), (b.U, b.V), (c.U, c.V), rgb.Scale(shade)));
            }
        }
        list.Sort((x, y) => x.Depth.CompareTo(y.Depth));
        return list;
    }

    public static Rgb Mix(Rgb a, Rgb b, double t) => new((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>Umgebendes Rechteck (u0, v0, u1, v1) der Dreiecke; leer = um den Ursprung.</summary>
    public static (double U0, double V0, double U1, double V1) Bounds(IReadOnlyList<DrawnTriangle> tris)
    {
        if (tris.Count == 0) return (-4, -4, 4, 4);
        double u0 = double.MaxValue, v0 = double.MaxValue, u1 = double.MinValue, v1 = double.MinValue;
        foreach (var t in tris)
            foreach (var (u, v) in new[] { t.A, t.B, t.C })
            {
                u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); v0 = Math.Min(v0, v); v1 = Math.Max(v1, v);
            }
        return (u0, v0, u1, v1);
    }
}
