using System.Globalization;
using System.Text;

namespace TrackMaster.Core;

/// <summary>
/// Zuschnitt der Wendeltrasse als Zeichnung: Ringsegmente, gerade Bretter (Oval) und Stoßlaschen in Originalgröße,
/// jedes Teil als eigene SVG-Datei. Alle Maße in mm und vor dem Export änderbar; vorgeschlagen werden die Werte der Holzliste.
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

    private sealed record Piece(string Label, string Size, double W, double H, Func<Func<double, string>, string> Draw);

    /// <summary>Jedes Teil (Segment, gerades Brett, Stoßlasche) als eigene SVG-Datei in Originalgröße.</summary>
    public IReadOnlyList<SvgFile> ToSvgFiles(string title)
    {
        var pieces = new List<Piece>();

        // Ringsegment: Symmetrieachse senkrecht, Außenbogen oben; Mittelpunkt des Kreises unterhalb
        var a = SegmentAngle * Math.PI / 180;
        var half = a / 2;
        var segW = half >= Math.PI / 2 ? 2 * OuterRadius : 2 * OuterRadius * Math.Sin(half);
        var segH = OuterRadius - InnerRadius * Math.Cos(half);
        string Sector(Func<double, string> n)
        {
            double cx = segW / 2, cy = OuterRadius;
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
        string Rect(double w, double h, Func<double, string> n, IEnumerable<double>? marks = null)
        {
            var sb = new StringBuilder($"<rect x=\"0\" y=\"0\" width=\"{n(w)}\" height=\"{n(h)}\" class=\"cut\"/>");
            foreach (var m in marks ?? [])
                sb.Append($"<path d=\"M0 {n(m)}L{n(w)} {n(m)}\" class=\"mark\"/>");
            return sb.ToString();
        }

        var segSize = Lang.F($"R {Fmt.Num(InnerRadius, 0)}–{Fmt.Num(OuterRadius, 0)} · {Fmt.Num(SegmentAngle, 1)}°");
        for (var i = 0; i < Segments; i++)
            pieces.Add(new($"S{i + 1}", segSize, segW, segH, Sector));
        var boardMarks = TrackRadii.Select(r => r - InnerRadius).Where(m => m > 0 && m < BoardWidth).ToList();
        for (var i = 0; i < Boards; i++)
            pieces.Add(new($"G{i + 1}", $"{Fmt.Num(BoardLength, 0)} × {Fmt.Num(BoardWidth, 0)}", BoardLength, BoardWidth, n => Rect(BoardLength, BoardWidth, n, boardMarks)));
        for (var i = 0; i < Laschen; i++)
            pieces.Add(new($"L{i + 1}", $"{Fmt.Num(LascheLength, 0)} × {Fmt.Num(LascheWidth, 0)}", LascheLength, LascheWidth, n => Rect(LascheLength, LascheWidth, n)));

        var files = new List<SvgFile>();
        foreach (var p in pieces)
        {
            var body = new StringBuilder(p.Draw(SvgFiles.N));
            var size = Math.Clamp(Math.Min(p.W, p.H) / 5, 8, 40);
            var tx = p.W / 2;
            var ty = p.H * (p.Label.StartsWith('S') ? 0.42 : 0.5);
            body.Append($"<text x=\"{SvgFiles.N(tx)}\" y=\"{SvgFiles.N(ty)}\" font-size=\"{SvgFiles.N(size)}\" class=\"label\">{SvgFiles.Esc(p.Label)}</text>");
            body.Append($"<text x=\"{SvgFiles.N(tx)}\" y=\"{SvgFiles.N(ty + size * 0.9)}\" font-size=\"{SvgFiles.N(size * 0.45)}\" class=\"label\">{SvgFiles.Esc(p.Size)}</text>");
            var kind = p.Label[0] switch { 'S' => Lang.T("Segment"), 'G' => Lang.T("Gerades Brett"), _ => Lang.T("Stoßlasche") };
            var caption = Lang.F($"{title} · {kind} {p.Label} · {p.Size} · {Fmt.Num(Thickness, 0)} mm · Maßstab 1:1") + (p.Label[0] == 'S' && TrackRadii.Count > 0 ? Lang.T(" · gestrichelt: Gleismitte") : "");
            var number = SvgFiles.Number(int.Parse(p.Label[1..], CultureInfo.InvariantCulture), Math.Max(Segments, Math.Max(Boards, Laschen)));
            files.Add(new($"{Lang.T("Gleiswendel")} {p.Label[0]}{number}.svg", SvgFiles.Document($"{title} – {p.Label}", caption, p.W, p.H, body.ToString())));
        }
        return files;
    }
}
