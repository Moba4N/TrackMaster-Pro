using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace TrackMaster.Core;

/// <summary>Eine SVG-Datei mit genau einem Bauteil (Name ohne Pfad, mit Endung).</summary>
public sealed record SvgFile(string Name, string Content);

/// <summary>
/// Zuschnitte als einzelne SVG-Dateien: jedes Bauteil in einer eigenen Datei in Originalgröße (mm), damit es sich direkt
/// in Laser-, Fräs- oder Plottersoftware laden lässt. Mehrere Dateien werden zusammen als ZIP gespeichert.
/// </summary>
public static class SvgFiles
{
    public const double Margin = 10;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string N(double v) => Math.Round(v, 2).ToString(Inv);

    public static string Esc(string s) => System.Security.SecurityElement.Escape(s) ?? "";

    /// <summary>
    /// SVG für ein Bauteil der Größe <paramref name="width"/> × <paramref name="height"/> mm; <paramref name="body"/> zeichnet es ab (0, 0).
    /// Die Beschriftung steht über dem Teil und wird bei schmalen Teilen an den „·“ umbrochen.
    /// </summary>
    public static string Document(string title, string caption, double width, double height, string body)
    {
        var (lines, size) = Wrap(caption, Math.Max(width, 60));
        var captionHeight = lines.Count * size * 1.25 + 4;
        double w = Math.Max(width, 60) + 2 * Margin, h = height + 2 * Margin + captionHeight;
        var text = string.Concat(lines.Select((l, i) => $"<tspan x=\"0\" dy=\"{N(i == 0 ? 0 : size * 1.25)}\">{Esc(l)}</tspan>"));
        return $"""
<svg xmlns="http://www.w3.org/2000/svg" width="{N(w)}mm" height="{N(h)}mm" viewBox="{N(-Margin)} {N(-Margin - captionHeight)} {N(w)} {N(h)}">
<title>{Esc(title)}</title>
<style>
.cut {"{"} fill: #F7E9D0; stroke: #000; stroke-width: 0.5; {"}"}
.hole {"{"} fill: #fff; stroke: #000; stroke-width: 0.5; {"}"}
.mark {"{"} fill: none; stroke: #C42B1C; stroke-width: 0.4; stroke-dasharray: 6 4; {"}"}
.tick {"{"} fill: none; stroke: #1F4E79; stroke-width: 0.4; {"}"}
.head {"{"} font-family: 'Segoe UI', sans-serif; fill: #333; {"}"}
.label {"{"} font-family: 'Segoe UI', sans-serif; fill: #1F4E79; text-anchor: middle; {"}"}
</style>
<text x="0" y="{N(-captionHeight + size)}" font-size="{N(size)}" class="head">{text}</text>
{body}
</svg>
""";
    }

    /// <summary>Bricht die Beschriftung an „ · “ so um, dass sie in die Breite passt; bei vielen Zeilen kleinere Schrift.</summary>
    private static (List<string> Lines, double Size) Wrap(string caption, double width)
    {
        var parts = caption.Split(" · ");
        foreach (var size in new[] { 8.0, 6, 4 })
        {
            var max = Math.Max(10, (int)(width / (size * 0.55)));
            var lines = new List<string>();
            foreach (var part in parts)
            {
                if (lines.Count > 0 && lines[^1].Length + 3 + part.Length <= max) lines[^1] += " · " + part;
                else lines.Add(part);
            }
            if (lines.Count <= 4 || size == 4) return (lines, size);
        }
        return ([caption], 4);
    }

    /// <summary>Laufende Nummer mit führenden Nullen, damit die Dateien richtig sortiert werden.</summary>
    public static string Number(int n, int count) => n.ToString(new string('0', Math.Max(2, count.ToString(Inv).Length)), Inv);

    /// <summary>Entfernt Zeichen, die in Dateinamen nicht erlaubt sind.</summary>
    public static string FileName(string name)
    {
        var bad = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var s = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
        return s.Length == 0 ? "Teil" : s;
    }

    /// <summary>Packt die Dateien in ein ZIP-Archiv.</summary>
    public static byte[] Zip(IEnumerable<SvgFile> files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                var name = FileName(f.Name);
                for (var i = 2; !used.Add(name); i++) name = $"{Path.GetFileNameWithoutExtension(f.Name)} ({i}){Path.GetExtension(f.Name)}";
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var s = entry.Open();
                var bytes = new UTF8Encoding(false).GetBytes(f.Content);
                s.Write(bytes, 0, bytes.Length);
            }
        }
        return ms.ToArray();
    }

    /// <summary>Pfad eines Polygons (geschlossen).</summary>
    public static string Polygon(IEnumerable<(double X, double Y)> points)
        => "M" + string.Join("L", points.Select(p => $"{N(p.X)} {N(p.Y)}")) + "Z";

    /// <summary>Pfad einer offenen Linie.</summary>
    public static string Polyline(IEnumerable<(double X, double Y)> points)
        => "M" + string.Join("L", points.Select(p => $"{N(p.X)} {N(p.Y)}"));
}
