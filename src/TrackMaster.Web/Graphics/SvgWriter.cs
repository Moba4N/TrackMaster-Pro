using System.Globalization;
using System.Text;

namespace TrackMaster.Web.Graphics;

/// <summary>Übersetzt das Zeichenprotokoll einer <see cref="Canvas"/> in SVG (Vorschaubilder, Signalbilder, Druck).</summary>
public static class SvgWriter
{
    private static int _clip;

    public static string FontFamily { get; set; } = "'Segoe UI Variable Text','Segoe UI',Roboto,system-ui,sans-serif";

    /// <summary>Nur der Inhalt (ohne &lt;svg&gt;-Hülle).</summary>
    public static string Body(Canvas canvas)
    {
        var inv = CultureInfo.InvariantCulture;
        string N(double v) => Math.Round(v, 3).ToString(inv);
        string Col(double packed)
        {
            var u = (uint)packed;
            var c = new Color((byte)(u >> 24), (byte)(u >> 16), (byte)(u >> 8), (byte)u);
            return c.Hex;
        }
        string Alpha(double packed) => (((uint)packed >> 24) / 255.0).ToString("0.###", inv);
        string Fill(int style, double color, double width, string extra = "")
        {
            var c = Col(color);
            var o = Alpha(color);
            var w = width <= 0 ? "1" : N(width);
            var vector = width <= 0 ? " vector-effect=\"non-scaling-stroke\"" : "";
            return style switch
            {
                0 => $" fill=\"{c}\" fill-opacity=\"{o}\" stroke=\"none\"",
                1 => $" fill=\"none\" stroke=\"{c}\" stroke-opacity=\"{o}\" stroke-width=\"{w}\"{vector}{extra}",
                _ => $" fill=\"{c}\" fill-opacity=\"{o}\" stroke=\"{c}\" stroke-opacity=\"{o}\" stroke-width=\"{w}\"{vector}{extra}",
            };
        }
        string Cap(int cap) => cap switch { 1 => "round", 2 => "square", _ => "butt" };
        string Join(int join) => join switch { 1 => "round", 2 => "bevel", _ => "miter" };
        static string Esc(string s) => System.Security.SecurityElement.Escape(s) ?? "";

        var d = canvas.Data;
        var paths = new Dictionary<int, (string Data, bool EvenOdd)>();
        var sb = new StringBuilder();
        var open = 0;
        var saved = new Stack<int>();
        for (var i = 0; i < d.Count;)
        {
            switch ((Graphics.Op)(int)d[i])
            {
                case Graphics.Op.Save: saved.Push(open); i += 1; break;
                case Graphics.Op.Restore:
                    var to = saved.Count > 0 ? saved.Pop() : 0;
                    while (open > to) { sb.Append("</g>"); open--; }
                    i += 1;
                    break;
                case Graphics.Op.Translate: sb.Append($"<g transform=\"translate({N(d[i + 1])} {N(d[i + 2])})\">"); open++; i += 3; break;
                case Graphics.Op.Rotate: sb.Append($"<g transform=\"rotate({N(d[i + 1])})\">"); open++; i += 2; break;
                case Graphics.Op.Scale: sb.Append($"<g transform=\"scale({N(d[i + 1])} {N(d[i + 2])})\">"); open++; i += 3; break;
                case Graphics.Op.Concat:
                    sb.Append($"<g transform=\"matrix({N(d[i + 1])} {N(d[i + 2])} {N(d[i + 3])} {N(d[i + 4])} {N(d[i + 5])} {N(d[i + 6])})\">");
                    open++;
                    i += 7;
                    break;
                case Graphics.Op.DefinePath:
                {
                    int id = (int)d[i + 1], n = (int)d[i + 3];
                    var p = new Path();
                    p.Ops.AddRange(d.Skip(i + 4).Take(n));
                    paths[id] = (p.SvgData(), d[i + 2] > 0);
                    i += 4 + n;
                    break;
                }
                case Graphics.Op.ClipPath:
                {
                    var (data, evenOdd) = paths.GetValueOrDefault((int)d[i + 1], ("", false));
                    var cid = $"tmclip{Interlocked.Increment(ref _clip)}";
                    sb.Append($"<clipPath id=\"{cid}\"><path d=\"{data}\" clip-rule=\"{(evenOdd ? "evenodd" : "nonzero")}\"/></clipPath><g clip-path=\"url(#{cid})\">");
                    open++;
                    i += 2;
                    break;
                }
                case Graphics.Op.ClipRect:
                {
                    var cid = $"tmclip{Interlocked.Increment(ref _clip)}";
                    sb.Append($"<clipPath id=\"{cid}\"><rect x=\"{N(d[i + 1])}\" y=\"{N(d[i + 2])}\" width=\"{N(d[i + 3] - d[i + 1])}\" height=\"{N(d[i + 4] - d[i + 2])}\"/></clipPath><g clip-path=\"url(#{cid})\">");
                    open++;
                    i += 5;
                    break;
                }
                case Graphics.Op.Path:
                {
                    var (data, evenOdd) = paths.GetValueOrDefault((int)d[i + 1], ("", false));
                    int style = (int)d[i + 2], dashes = (int)d[i + 7];
                    var dash = dashes > 0 ? $" stroke-dasharray=\"{string.Join(" ", d.Skip(i + 8).Take(dashes).Select(N))}\" stroke-dashoffset=\"{N(d[i + 8 + dashes])}\"" : "";
                    var extra = $" stroke-linecap=\"{Cap((int)d[i + 5])}\" stroke-linejoin=\"{Join((int)d[i + 6])}\"{dash}";
                    sb.Append($"<path d=\"{data}\" fill-rule=\"{(evenOdd ? "evenodd" : "nonzero")}\"{Fill(style, d[i + 3], d[i + 4], extra)}/>");
                    i += 9 + dashes;
                    break;
                }
                case Graphics.Op.Line:
                    sb.Append($"<line x1=\"{N(d[i + 1])}\" y1=\"{N(d[i + 2])}\" x2=\"{N(d[i + 3])}\" y2=\"{N(d[i + 4])}\"{Fill(1, d[i + 5], d[i + 6], $" stroke-linecap=\"{Cap((int)d[i + 7])}\"")}/>");
                    i += 8;
                    break;
                case Graphics.Op.Circle:
                    sb.Append($"<circle cx=\"{N(d[i + 1])}\" cy=\"{N(d[i + 2])}\" r=\"{N(Math.Max(0, d[i + 3]))}\"{Fill((int)d[i + 4], d[i + 5], d[i + 6])}/>");
                    i += 7;
                    break;
                case Graphics.Op.Rect:
                    sb.Append($"<rect x=\"{N(Math.Min(d[i + 1], d[i + 3]))}\" y=\"{N(Math.Min(d[i + 2], d[i + 4]))}\" width=\"{N(Math.Abs(d[i + 3] - d[i + 1]))}\" height=\"{N(Math.Abs(d[i + 4] - d[i + 2]))}\"{Fill((int)d[i + 5], d[i + 6], d[i + 7], $" stroke-linejoin=\"{Join((int)d[i + 8])}\"")}/>");
                    i += 9;
                    break;
                case Graphics.Op.RoundRect:
                    sb.Append($"<rect x=\"{N(d[i + 1])}\" y=\"{N(d[i + 2])}\" width=\"{N(d[i + 3] - d[i + 1])}\" height=\"{N(d[i + 4] - d[i + 2])}\" rx=\"{N(d[i + 5])}\" ry=\"{N(d[i + 6])}\"{Fill((int)d[i + 7], d[i + 8], d[i + 9])}/>");
                    i += 10;
                    break;
                case Graphics.Op.Text:
                {
                    var s = canvas.Strings[(int)d[i + 1]];
                    var anchor = (int)d[i + 6] switch { 1 => "middle", 2 => "end", _ => "start" };
                    sb.Append($"<text x=\"{N(d[i + 2])}\" y=\"{N(d[i + 3])}\" font-size=\"{N(d[i + 4])}\" fill=\"{Col(d[i + 5])}\" fill-opacity=\"{Alpha(d[i + 5])}\" text-anchor=\"{anchor}\" font-family=\"{Esc(FontFamily)}\"{(d[i + 7] > 0 ? " font-weight=\"600\"" : "")}>{Esc(s)}</text>");
                    i += 8;
                    break;
                }
                case Graphics.Op.Tag:
                {
                    var s = canvas.Strings[(int)d[i + 1]];
                    double cx = d[i + 2], cy = d[i + 3], size = d[i + 4], pad = d[i + 7];
                    var w = s.Length * size * 0.56;
                    sb.Append($"<rect x=\"{N(cx - w / 2 - pad)}\" y=\"{N(cy - size * 0.65)}\" width=\"{N(w + 2 * pad)}\" height=\"{N(size * 1.3)}\" rx=\"{N(pad)}\"{Fill(0, d[i + 6], 0)}/>");
                    sb.Append($"<text x=\"{N(cx)}\" y=\"{N(cy)}\" font-size=\"{N(size)}\" fill=\"{Col(d[i + 5])}\" text-anchor=\"middle\" dominant-baseline=\"central\" font-family=\"{Esc(FontFamily)}\">{Esc(s)}</text>");
                    i += 8;
                    break;
                }
                case Graphics.Op.FillAll:
                    sb.Append($"<rect x=\"-100000\" y=\"-100000\" width=\"200000\" height=\"200000\"{Fill(0, d[i + 1], 0)}/>");
                    i += 2;
                    break;
                default:
                    i = d.Count;
                    break;
            }
        }
        while (open-- > 0) sb.Append("</g>");
        return sb.ToString();
    }

    /// <summary>Vollständiges SVG in Bildschirmeinheiten (Pixel), z. B. für Vorschaubilder.</summary>
    public static string Image(Canvas canvas, string? cssClass = null)
    {
        var inv = CultureInfo.InvariantCulture;
        var w = canvas.Width.ToString("0.##", inv);
        var h = canvas.Height.ToString("0.##", inv);
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\"{(cssClass is null ? "" : $" class=\"{cssClass}\"")} width=\"{w}\" height=\"{h}\" viewBox=\"0 0 {w} {h}\">{Body(canvas)}</svg>";
    }

    /// <summary>Druckseite: Zeichnung in PDF-Punkten (1/72 Zoll), Seitengröße in Millimetern.</summary>
    public static string Page(Canvas canvas, double widthMm, double heightMm)
    {
        var inv = CultureInfo.InvariantCulture;
        var w = canvas.Width.ToString("0.##", inv);
        var h = canvas.Height.ToString("0.##", inv);
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{widthMm.ToString(inv)}mm\" height=\"{heightMm.ToString(inv)}mm\" viewBox=\"0 0 {w} {h}\">{Body(canvas)}</svg>";
    }
}
