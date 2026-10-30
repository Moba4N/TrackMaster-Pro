using System.Globalization;
using System.Text;

namespace TrackMaster.Core;

/// <summary>
/// Zuschnitt der Wendeltrasse als Zeichnung: Ringsegmente, gerade Bretter (Oval) und Stoßlaschen in Originalgröße,
/// auf Platten verteilt. Alle Maße in mm und vor dem Export änderbar; vorgeschlagen werden die Werte der Holzliste.
/// </summary>
public sealed record HelixCut(
    double InnerRadius, double OuterRadius, int SegmentsPerCircle, int Segments,
    int Boards, double BoardLength, double BoardWidth,
    int Laschen, double LascheLength, double LascheWidth,
    double Thickness, IReadOnlyList<double> TrackRadii)
{
    public double SegmentAngle => 360.0 / SegmentsPerCircle;

    /// <summary>Fehler in den Maßen, leer wenn alles passt.</summary>
    public List<string> Problems()
    {
        var errors = new List<string>();
        if (InnerRadius < 0) errors.Add(Lang.T("Innenradius: nicht negativ."));
        if (OuterRadius <= InnerRadius + 10) errors.Add(Lang.T("Außenradius: mindestens 10 mm größer als der Innenradius."));
        if (SegmentsPerCircle < 2 || SegmentsPerCircle > 36) errors.Add(Lang.T("Segmente je Kreis: zwischen 2 und 36."));
        if (Segments < 0 || Segments > 500) errors.Add(Lang.T("Anzahl Segmente: zwischen 0 und 500."));
        if (Boards < 0 || Boards > 200) errors.Add(Lang.T("Anzahl gerade Bretter: zwischen 0 und 200."));
        if (Boards > 0 && (BoardLength < 10 || BoardWidth < 10)) errors.Add(Lang.T("Gerade Bretter: Länge und Breite mindestens 10 mm."));
        if (Laschen < 0 || Laschen > 500) errors.Add(Lang.T("Anzahl Stoßlaschen: zwischen 0 und 500."));
        if (Laschen > 0 && (LascheLength < 10 || LascheWidth < 10)) errors.Add(Lang.T("Stoßlaschen: Länge und Breite mindestens 10 mm."));
        return errors;
    }

    private const double Gap = 10, Margin = 10;

    private sealed record Piece(string Label, string Size, double W, double H, Func<double, double, Func<double, string>, string> Draw);

    public string ToSvg(string title)
    {
        var inv = CultureInfo.InvariantCulture;
        string N(double v) => Math.Round(v, 2).ToString(inv);
        string Esc(string s) => System.Security.SecurityElement.Escape(s) ?? "";
        var pieces = new List<Piece>();

        // Ringsegment: Symmetrieachse senkrecht, Außenbogen oben; Mittelpunkt des Kreises unterhalb
        var a = SegmentAngle * Math.PI / 180;
        var half = a / 2;
        var segW = half >= Math.PI / 2 ? 2 * OuterRadius : 2 * OuterRadius * Math.Sin(half);
        var segH = OuterRadius - InnerRadius * Math.Cos(half);
        string Sector(double x, double y, Func<double, string> n)
        {
            double cx = x + segW / 2, cy = y + OuterRadius;
            (double X, double Y) P(double r, double ang) => (cx + r * Math.Sin(ang), cy - r * Math.Cos(ang));
            var large = a > Math.PI ? 1 : 0;
            var o1 = P(OuterRadius, -half); var o2 = P(OuterRadius, half);
            var i1 = P(InnerRadius, half); var i2 = P(InnerRadius, -half);
            var sb = new StringBuilder();
            sb.Append($"<path d=\"M{n(o1.X)} {n(o1.Y)}A{n(OuterRadius)} {n(OuterRadius)} 0 {large} 1 {n(o2.X)} {n(o2.Y)}L{n(i1.X)} {n(i1.Y)}");
            sb.Append(InnerRadius > 0.5 ? $"A{n(InnerRadius)} {n(InnerRadius)} 0 {large} 0 {n(i2.X)} {n(i2.Y)}Z\"" : "Z\"");
            sb.Append(" class=\"cut\"/>");
            foreach (var r in TrackRadii.Where(r => r > InnerRadius && r < OuterRadius))
            {
                var t1 = P(r, -half); var t2 = P(r, half);
                sb.Append($"<path d=\"M{n(t1.X)} {n(t1.Y)}A{n(r)} {n(r)} 0 {large} 1 {n(t2.X)} {n(t2.Y)}\" class=\"mark\"/>");
            }
            return sb.ToString();
        }
        string Rect(double x, double y, double w, double h, Func<double, string> n, IEnumerable<double>? marks = null)
        {
            var sb = new StringBuilder($"<rect x=\"{n(x)}\" y=\"{n(y)}\" width=\"{n(w)}\" height=\"{n(h)}\" class=\"cut\"/>");
            foreach (var m in marks ?? [])
                sb.Append($"<path d=\"M{n(x)} {n(y + m)}L{n(x + w)} {n(y + m)}\" class=\"mark\"/>");
            return sb.ToString();
        }

        for (var i = 0; i < Segments; i++)
            pieces.Add(new(Lang.F($"S{i + 1}"), Lang.F($"R {Fmt.Num(InnerRadius, 0)}–{Fmt.Num(OuterRadius, 0)} · {Fmt.Num(SegmentAngle, 1)}°"), segW, segH, Sector));
        var boardMarks = TrackRadii.Select(r => r - InnerRadius).Where(m => m > 0 && m < BoardWidth).ToList();
        for (var i = 0; i < Boards; i++)
            pieces.Add(new(Lang.F($"G{i + 1}"), $"{Fmt.Num(BoardLength, 0)} × {Fmt.Num(BoardWidth, 0)}", BoardLength, BoardWidth, (x, y, n) => Rect(x, y, BoardLength, BoardWidth, n, boardMarks)));
        for (var i = 0; i < Laschen; i++)
            pieces.Add(new(Lang.F($"L{i + 1}"), $"{Fmt.Num(LascheLength, 0)} × {Fmt.Num(LascheWidth, 0)}", LascheLength, LascheWidth, (x, y, n) => Rect(x, y, LascheLength, LascheWidth, n)));

        // Auf Platten verteilen: Reihen (Regalverfahren), höchste Teile zuerst
        var sheets = new List<List<(Piece P, double X, double Y)>>();
        var oversize = 0;
        double sw = Helix.SheetLength, sh = Helix.SheetWidth;
        List<(Piece P, double X, double Y)>? sheet = null;
        double rowX = 0, rowY = 0, rowH = 0;
        foreach (var p in pieces.OrderByDescending(p => p.H).ThenByDescending(p => p.W))
        {
            if (p.W > sw - 2 * Margin || p.H > sh - 2 * Margin)
            {
                oversize++;
                sheets.Add([(p, Margin, Margin)]);
                sheet = null;
                continue;
            }
            if (sheet is null) { sheet = []; sheets.Add(sheet); rowX = Margin; rowY = Margin; rowH = 0; }
            if (rowX + p.W > sw - Margin) { rowX = Margin; rowY += rowH + Gap; rowH = 0; }
            if (rowY + p.H > sh - Margin) { sheet = []; sheets.Add(sheet); rowX = Margin; rowY = Margin; rowH = 0; }
            sheet.Add((p, rowX, rowY));
            rowX += p.W + Gap;
            rowH = Math.Max(rowH, p.H);
        }

        var body = new StringBuilder();
        const double sheetGap = 120;
        double y0 = 0, width = sw;
        for (var s = 0; s < sheets.Count; s++)
        {
            var items = sheets[s];
            double w = Math.Max(sw, items.Max(i => i.X + i.P.W) + Margin), h = Math.Max(sh, items.Max(i => i.Y + i.P.H) + Margin);
            width = Math.Max(width, w);
            body.Append($"<text x=\"0\" y=\"{N(y0 - 10)}\" class=\"head\">{Esc(Lang.F($"Platte {s + 1} von {sheets.Count} · {Fmt.Num(w, 0)} × {Fmt.Num(h, 0)} × {Fmt.Num(Thickness, 0)} mm"))}</text>");
            body.Append($"<rect x=\"0\" y=\"{N(y0)}\" width=\"{N(w)}\" height=\"{N(h)}\" class=\"sheet\"/>");
            foreach (var (p, x, y) in items)
            {
                body.Append(p.Draw(x, y0 + y, N));
                var tx = x + p.W / 2;
                var ty = y0 + y + p.H * (p.Label.StartsWith('S') ? 0.42 : 0.5);
                var size = Math.Clamp(Math.Min(p.W, p.H) / 5, 8, 40);
                body.Append($"<text x=\"{N(tx)}\" y=\"{N(ty)}\" font-size=\"{N(size)}\" class=\"label\">{Esc(p.Label)}</text>");
                body.Append($"<text x=\"{N(tx)}\" y=\"{N(ty + size * 0.9)}\" font-size=\"{N(size * 0.45)}\" class=\"label\">{Esc(p.Size)}</text>");
            }
            y0 += h + sheetGap;
        }
        const double m = 40;
        var height = Math.Max(y0 - sheetGap, 10);
        var info = Lang.F($"Gleiswendel · Zuschnitt 1:1 · {Segments} Segmente {Fmt.Num(SegmentAngle, 1)}° (R {Fmt.Num(InnerRadius, 0)}–{Fmt.Num(OuterRadius, 0)} mm)")
                   + (Boards > 0 ? Lang.F($" · {Boards} gerade Bretter") : "") + (Laschen > 0 ? Lang.F($" · {Laschen} Stoßlaschen") : "")
                   + Lang.T(" · gestrichelt: Gleismitte") + (oversize > 0 ? Lang.F($" · {oversize} Teil(e) größer als die Platte") : "");
        return $"""
<svg xmlns="http://www.w3.org/2000/svg" width="{N(width + 2 * m)}mm" height="{N(height + 2 * m + 30)}mm" viewBox="{N(-m)} {N(-m - 30)} {N(width + 2 * m)} {N(height + 2 * m + 30)}">
<title>{Esc(title)}</title>
<style>
.cut {"{"} fill: #F7E9D0; stroke: #000; stroke-width: 0.5; {"}"}
.mark {"{"} fill: none; stroke: #C42B1C; stroke-width: 0.4; stroke-dasharray: 6 4; {"}"}
.sheet {"{"} fill: none; stroke: #8FA67A; stroke-width: 1; stroke-dasharray: 12 6; {"}"}
.head {"{"} font-family: 'Segoe UI', sans-serif; font-size: 18px; fill: #333; {"}"}
.label {"{"} font-family: 'Segoe UI', sans-serif; fill: #1F4E79; text-anchor: middle; {"}"}
</style>
<text x="0" y="-36" class="head">{Esc(title)} · {Esc(info)}</text>
{body}
</svg>
""";
    }
}
