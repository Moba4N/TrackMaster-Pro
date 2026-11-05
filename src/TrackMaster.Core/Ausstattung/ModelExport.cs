using System.Globalization;
using System.Text;

namespace TrackMaster.Core;

/// <summary>
/// Export eines Modells für andere 3D-Programme und den 3D-Druck (Pro): OBJ (mit MTL-Farben) und binäres STL.
/// Maßstab: <paramref name="mmPerMeter"/> = Millimeter je Meter im Vorbild (z. B. 1000/87 für H0; 1000 = Vorbildgröße in mm).
/// </summary>
public static class ModelExport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Dreiecke in Millimetern, rechtshändig mit z nach oben (Modell-y zeigt im Plan nach unten, daher gespiegelt).</summary>
    public static List<(Rgb Color, (double X, double Y, double Z) A, (double X, double Y, double Z) B, (double X, double Y, double Z) C)> Triangles(ModelDefinition def, double mmPerMeter, Rgb? mainColor = null)
    {
        (double, double, double) P((double X, double Y, double Z) p) => (p.X * mmPerMeter, -p.Y * mmPerMeter, p.Z * mmPerMeter);
        // Spiegeln dreht den Umlaufsinn um: B und C tauschen
        return ModelMesh.Triangles(def, mainColor).Select(t => (t.Color, P(t.Triangle.A), P(t.Triangle.C), P(t.Triangle.B))).ToList();
    }

    private static string N(double v) => Math.Round(v, 4).ToString("0.####", Inv);

    /// <summary>OBJ-Datei (y nach oben, wie in den meisten 3D-Programmen) mit Verweis auf die MTL-Datei <paramref name="mtlName"/>.</summary>
    public static string ToObj(ModelDefinition def, double mmPerMeter, string mtlName, Rgb? mainColor = null)
    {
        var sb = new StringBuilder();
        sb.Append("# TrackMaster Pro – ").Append(def.Name).Append('\n');
        sb.Append("# Einheit: mm\n");
        sb.Append("mtllib ").Append(mtlName).Append('\n');
        sb.Append("o ").Append(ObjName(def.Name)).Append('\n');
        var n = 0;
        foreach (var group in Triangles(def, mmPerMeter, mainColor).GroupBy(t => t.Color))
        {
            sb.Append("usemtl ").Append(MaterialName(group.Key)).Append('\n');
            foreach (var (_, a, b, c) in group)
            {
                foreach (var p in new[] { a, b, c })
                    sb.Append("v ").Append(N(p.X)).Append(' ').Append(N(p.Z)).Append(' ').Append(N(-p.Y)).Append('\n');
                sb.Append("f ").Append(n + 1).Append(' ').Append(n + 2).Append(' ').Append(n + 3).Append('\n');
                n += 3;
            }
        }
        return sb.ToString();
    }

    /// <summary>Farben zur OBJ-Datei.</summary>
    public static string ToMtl(ModelDefinition def, Rgb? mainColor = null)
    {
        var sb = new StringBuilder("# TrackMaster Pro\n");
        foreach (var c in ModelMesh.Triangles(def, mainColor).Select(t => t.Color).Distinct())
            sb.Append("newmtl ").Append(MaterialName(c)).Append('\n')
              .Append("Kd ").Append(N(c.R / 255.0)).Append(' ').Append(N(c.G / 255.0)).Append(' ').Append(N(c.B / 255.0)).Append('\n')
              .Append("Ka 0 0 0\nd 1\nillum 1\n\n");
        return sb.ToString();
    }

    /// <summary>Binäres STL (z nach oben, mm) für Slicer und 3D-Druck.</summary>
    public static byte[] ToStl(ModelDefinition def, double mmPerMeter)
    {
        var tris = Triangles(def, mmPerMeter);
        using var ms = new MemoryStream(84 + tris.Count * 50);
        using (var w = new BinaryWriter(ms, Encoding.ASCII, true))
        {
            var header = Encoding.ASCII.GetBytes(("TrackMaster Pro " + Ascii(def.Name)).PadRight(80)[..80]);
            w.Write(header);
            w.Write((uint)tris.Count);
            foreach (var (_, a, b, c) in tris)
            {
                double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z, vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                var len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len > 1e-12) { nx /= len; ny /= len; nz /= len; }
                foreach (var v in new[] { nx, ny, nz, a.X, a.Y, a.Z, b.X, b.Y, b.Z, c.X, c.Y, c.Z }) w.Write((float)v);
                w.Write((ushort)0);
            }
        }
        return ms.ToArray();
    }

    private static string MaterialName(Rgb c) => $"farbe_{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string ObjName(string name)
    {
        var s = new string(Ascii(name).Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray()).Trim('_');
        return s.Length == 0 ? "Modell" : s;
    }

    private static string Ascii(string s)
    {
        var d = (s ?? "").Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("Ä", "Ae").Replace("Ö", "Oe").Replace("Ü", "Ue").Replace("ß", "ss");
        return new string(d.Where(ch => ch is >= ' ' and < (char)127).ToArray());
    }
}
