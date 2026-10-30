using System.Globalization;
using System.Text;

namespace TrackMaster.Core;

/// <summary>Lage der Spanten: quer (parallel zur Tiefe der Anlage, hintereinander entlang der Breite) oder längs.</summary>
public enum FrameDirection { Quer, Laengs }

/// <summary>
/// Angaben für den Spantenbau der Anlage (offene Rahmenbauweise): Spanten stehen im Abstand <see cref="Spacing"/> auf dem Rahmen,
/// die Trassenbretter (<see cref="TrasseWidth"/> breit, <see cref="Thickness"/> stark) liegen oben auf den Spanten.
/// <see cref="BaseHeight"/> ist die Höhe der Schienenoberkante bei Gleishöhe 0 über der Rahmenoberkante (= Spantfuß).
/// </summary>
public sealed record FrameSpec(FrameDirection Direction, double Spacing, double TrasseWidth, double Thickness, double BaseHeight);

/// <summary>Auflage einer Trasse auf dem Spant: von–bis entlang des Spants, Höhe der Oberkante des Spants dort (ab Spantfuß).</summary>
public sealed record FrameSupport(double From, double To, double HeightFrom, double HeightTo, double RailHeight)
{
    public double HeightAt(double u) => To - From < 0.01 ? HeightFrom : HeightFrom + (HeightTo - HeightFrom) * Math.Clamp((u - From) / (To - From), 0, 1);
    public double Mid => (HeightFrom + HeightTo) / 2;
}

/// <summary>Ausschnitt im Spant, durch den eine untere Trasse unter einer oberen hindurchführt.</summary>
public sealed record FrameCutout(double From, double To, double BottomFrom, double BottomTo, double Top);

/// <summary>
/// Ein Spant: senkrechtes Brett von <see cref="Start"/> bis <see cref="End"/> (entlang des Spants) an der Stelle <see cref="Position"/>.
/// <see cref="Outline"/> ist der Umriss in Spant-Koordinaten (u entlang des Spants ab <see cref="Start"/>, h nach oben ab Spantfuß).
/// </summary>
public sealed record FrameRib(int Number, double Position, double Start, double End, double Height,
    IReadOnlyList<FrameSupport> Supports, IReadOnlyList<FrameCutout> Cutouts, IReadOnlyList<(double U, double H)> Outline)
{
    public double Length => End - Start;
}

/// <summary>Ergebnis des Spantenbaus: alle Spanten mit Aufmaßen, Hinweise, Ausgabe als Text, CSV und SVG.</summary>
public sealed record FramePlan(FrameSpec Spec, string GaugeId, IReadOnlyList<FrameRib> Ribs, IReadOnlyList<string> Notes)
{
    private string Axis => Spec.Direction == FrameDirection.Quer ? "x" : "y";
    private string Along => Spec.Direction == FrameDirection.Quer ? "y" : "x";

    public string ToText()
    {
        var lines = new List<string> { Lang.T("Spantenbau"), "" };
        lines.AddRange(Notes);
        foreach (var r in Ribs)
        {
            lines.Add("");
            lines.Add(Lang.F($"Spant {r.Number}: {Axis} = {Fmt.Num(r.Position, 0)} mm, Länge {Fmt.Num(r.Length, 0)} mm ({Along} {Fmt.Num(r.Start, 0)} bis {Fmt.Num(r.End, 0)}), Höhe {Fmt.Num(r.Height, 0)} mm"));
            foreach (var s in r.Supports)
                lines.Add(Lang.F($"  Auflage {Fmt.Num(s.From - r.Start, 0)}–{Fmt.Num(s.To - r.Start, 0)} mm: Höhe {FrameBuild.HeightText(s)} mm (Schienenoberkante {Fmt.Num(s.RailHeight, 0)} mm)"));
            foreach (var c in r.Cutouts)
                lines.Add(Lang.F($"  Ausschnitt {Fmt.Num(c.From - r.Start, 0)}–{Fmt.Num(c.To - r.Start, 0)} mm: von {Fmt.Num(Math.Min(c.BottomFrom, c.BottomTo), 0)} bis {Fmt.Num(c.Top, 0)} mm"));
        }
        return string.Join(Environment.NewLine, lines);
    }

    public string ToCsv()
    {
        static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        var lines = new List<string> { Lang.T("Spant;Lage;Länge;Höhe;Art;von;bis;Höhe von;Höhe bis;Schienenoberkante") };
        foreach (var r in Ribs)
        {
            lines.Add($"{r.Number};{Q(Axis + " = " + Fmt.Num(r.Position, 0))};{Fmt.Num(r.Length, 0)};{Fmt.Num(r.Height, 0)};;;;;;");
            foreach (var s in r.Supports)
                lines.Add($"{r.Number};;;;{Q(Lang.T("Auflage"))};{Fmt.Num(s.From - r.Start, 0)};{Fmt.Num(s.To - r.Start, 0)};{Fmt.Num(s.HeightFrom, 1)};{Fmt.Num(s.HeightTo, 1)};{Fmt.Num(s.RailHeight, 1)}");
            foreach (var c in r.Cutouts)
                lines.Add($"{r.Number};;;;{Q(Lang.T("Ausschnitt"))};{Fmt.Num(c.From - r.Start, 0)};{Fmt.Num(c.To - r.Start, 0)};{Fmt.Num(c.BottomFrom, 1)};{Fmt.Num(c.Top, 1)};");
        }
        lines.Add("");
        lines.AddRange(Notes.Select(Q));
        return string.Join("\r\n", lines);
    }

    /// <summary>
    /// Zeichnung im Maßstab 1:1 (mm): oben die Übersicht mit Plattenumriss, Gleisen und Lage der Spanten,
    /// darunter jeder Spant als Schablone mit Auflagen, Ausschnitten und Maßen.
    /// </summary>
    public string ToSvg(LayoutPlan plan, string title)
    {
        var inv = CultureInfo.InvariantCulture;
        string N(double v) => Math.Round(v, 2).ToString(inv);
        string Esc(string s) => System.Security.SecurityElement.Escape(s) ?? "";
        const double m = 40, gap = 40;
        var body = new StringBuilder();
        var (x0, y0, x1, y1) = plan.Bounds();
        var overviewW = x1 - x0;
        var overviewH = y1 - y0;
        var width = overviewW;

        // Übersicht
        body.Append($"<g transform=\"translate({N(-x0)} {N(-y0)})\">");
        body.Append($"<path d=\"{string.Concat(BoardGeometry.Outlines(plan).Select(o => "M" + string.Join("L", o.Select(q => $"{N(q.X)} {N(q.Y)}")) + "Z"))}\" fill=\"#F3F1EA\" fill-rule=\"evenodd\" stroke=\"#8FA67A\" stroke-width=\"1\"/>");
        var tracks = new StringBuilder();
        foreach (var p in plan.Pieces)
            foreach (var l in p.WorldLegs())
            {
                var (ax, ay, bx, by, r) = l.Offset(0);
                tracks.Append(l.IsArc ? $"M{N(ax)} {N(ay)}A{N(r)} {N(r)} 0 0 {(l.Turn > 0 ? 1 : 0)} {N(bx)} {N(by)}" : $"M{N(ax)} {N(ay)}L{N(bx)} {N(by)}");
            }
        body.Append($"<path d=\"{tracks}\" fill=\"none\" stroke=\"#C8B79E\" stroke-width=\"{N(Spec.TrasseWidth)}\" stroke-opacity=\"0.55\"/>");
        body.Append($"<path d=\"{tracks}\" fill=\"none\" stroke=\"#4E5257\" stroke-width=\"1\"/>");
        foreach (var r in Ribs)
        {
            var (ax, ay, bx, by) = Spec.Direction == FrameDirection.Quer ? (r.Position, r.Start, r.Position, r.End) : (r.Start, r.Position, r.End, r.Position);
            body.Append($"<path d=\"M{N(ax)} {N(ay)}L{N(bx)} {N(by)}\" stroke=\"#1F4E79\" stroke-width=\"3\"/>");
            body.Append($"<text x=\"{N(ax)}\" y=\"{N(ay - 6)}\" font-size=\"18\" text-anchor=\"middle\" fill=\"#1F4E79\" font-family=\"Segoe UI, sans-serif\">{r.Number}</text>");
        }
        body.Append("</g>");

        // Schablonen der Spanten untereinander
        var y = overviewH + 2 * gap;
        foreach (var r in Ribs)
        {
            width = Math.Max(width, r.Length);
            var top = y + r.Height;
            string P(double u, double h) => $"{N(u)} {N(top - h)}";
            body.Append($"<text x=\"0\" y=\"{N(y - 8)}\" font-size=\"16\" font-family=\"Segoe UI, sans-serif\" fill=\"#222\">{Esc(Lang.F($"Spant {r.Number} · {Axis} = {Fmt.Num(r.Position, 0)} mm · Länge {Fmt.Num(r.Length, 0)} mm · Höhe {Fmt.Num(r.Height, 0)} mm"))}</text>");
            body.Append($"<path d=\"M{string.Join("L", r.Outline.Select(o => P(o.U, o.H)))}Z\" fill=\"#F7E9D0\" stroke=\"#000\" stroke-width=\"0.5\"/>");
            foreach (var c in r.Cutouts)
                body.Append($"<path d=\"M{P(c.From - r.Start, c.BottomFrom)}L{P(c.To - r.Start, c.BottomTo)}L{P(c.To - r.Start, c.Top)}L{P(c.From - r.Start, c.Top)}Z\" fill=\"#fff\" stroke=\"#000\" stroke-width=\"0.5\"/>");
            foreach (var s in r.Supports)
            {
                var u = (s.From + s.To) / 2 - r.Start;
                body.Append($"<text x=\"{N(u)}\" y=\"{N(top - s.HeightAt(u + r.Start) + 14)}\" font-size=\"10\" text-anchor=\"middle\" fill=\"#1F4E79\" font-family=\"Segoe UI, sans-serif\">{Esc(FrameBuild.HeightText(s))}</text>");
                body.Append($"<text x=\"{N(u)}\" y=\"{N(top + 12)}\" font-size=\"9\" text-anchor=\"middle\" fill=\"#666\" font-family=\"Segoe UI, sans-serif\">{N(Math.Round(s.From - r.Start))}–{N(Math.Round(s.To - r.Start))}</text>");
            }
            y = top + gap + 20;
        }
        var height = y;
        return $"""
<svg xmlns="http://www.w3.org/2000/svg" width="{N(width + 2 * m)}mm" height="{N(height + 2 * m)}mm" viewBox="{N(-m)} {N(-m)} {N(width + 2 * m)} {N(height + 2 * m)}">
<title>{Esc(title)}</title>
<text x="0" y="-12" font-family="Segoe UI, sans-serif" font-size="16" fill="#333">{Esc(title)} · {Esc(Lang.F($"Spantenbau Spur {GaugeId} · Spanten alle {Fmt.Num(Spec.Spacing, 0)} mm · Trasse {Fmt.Num(Spec.TrasseWidth, 0)} × {Fmt.Num(Spec.Thickness, 0)} mm · Maßstab 1:1"))}</text>
{body}
</svg>
""";
    }
}

/// <summary>
/// Spantenbau für die ganze Anlage (offene Rahmenbauweise wie in Wintrack): Spanten quer oder längs in festem Abstand,
/// mit den Auflagen der Trassen aus den Gleishöhen. Liegt eine Trasse unter einer anderen, bekommt der Spant dort
/// einen Ausschnitt mit der Durchfahrtshöhe der Spur, so dass mehrere Ebenen übereinander auf denselben Spanten ruhen.
/// </summary>
public static class FrameBuild
{
    /// <summary>Gleis und Bettung über der Trasse bis Schienenoberkante (H0: 7 mm).</summary>
    public static double TrackBuildHeight(Gauge g) => Math.Round(g.TrackWidth * 0.4);

    public static FrameSpec Defaults(Gauge g)
    {
        var spacing = Math.Max(100, Math.Round(g.TrackWidth * 18 / 50) * 50);
        var width = Helix.TrackBedWidth(g) + 2 * Math.Round(g.TrackWidth * 1.2);
        var thickness = g.TrackWidth < 10 ? 8.0 : g.TrackWidth < 25 ? 10.0 : 15.0;
        var baseHeight = Math.Max(50, Math.Round(g.TrackWidth * 6 / 10) * 10);
        return new FrameSpec(FrameDirection.Quer, spacing, width, thickness, baseHeight);
    }

    internal static string HeightText(FrameSupport s)
        => Math.Abs(s.HeightFrom - s.HeightTo) < 0.5 ? Fmt.Num(s.Mid, 0) : $"{Fmt.Num(s.HeightFrom, 0)} → {Fmt.Num(s.HeightTo, 0)}";

    private sealed record Hit(double U, double HalfWidth, double Height, double Slope, double Rail);

    public static FramePlan Build(LayoutPlan plan, FrameSpec spec)
    {
        var gauge = plan.Gauge;
        var build = TrackBuildHeight(gauge);
        var quer = spec.Direction == FrameDirection.Quer;
        var rects = BoardGeometry.Rects(plan);
        var notes = new List<string>();
        var ribs = new List<FrameRib>();
        if (rects.Count == 0 || spec.Spacing <= 0) return new FramePlan(spec, gauge.Id, ribs, notes);
        double a0 = rects.Min(r => quer ? r.X0 : r.Y0), a1 = rects.Max(r => quer ? r.X1 : r.Y1);
        var n = Math.Max(1, (int)Math.Ceiling((a1 - a0) / spec.Spacing - 1e-6));
        var step = (a1 - a0) / n;

        // Gleise als feine Polylinien mit Höhe der Schienenoberkante und Steigung
        var lines = new List<(double X, double Y, double H)[]>();
        foreach (var p in plan.Pieces)
        {
            if (p.Def.IsTable) continue;
            var legs = p.WorldLegs();
            for (var li = 0; li < legs.Count; li++)
            {
                var l = legs[li];
                var k = Math.Max(1, (int)Math.Ceiling(l.TrackLength / 5));
                var pts = new (double, double, double)[k + 1];
                for (var i = 0; i <= k; i++)
                {
                    var pose = l.At((double)i / k);
                    pts[i] = (pose.X, pose.Y, p.HeightAt(li, (double)i / k));
                }
                lines.Add(pts);
            }
        }

        var tooSteep = false;
        var tight = new List<int>();
        for (var i = 0; i < n; i++)
        {
            var c = a0 + step * (i + 0.5);
            // Abschnitte der Platte entlang dieser Linie (L- und U-Formen können mehrere ergeben)
            var spans = rects.Where(r => quer ? r.X0 <= c && c <= r.X1 : r.Y0 <= c && c <= r.Y1)
                             .Select(r => quer ? (r.Y0, r.Y1) : (r.X0, r.X1)).OrderBy(s => s.Item1).ToList();
            var merged = new List<(double A, double B)>();
            foreach (var s in spans)
            {
                if (merged.Count > 0 && s.Item1 <= merged[^1].B + 0.5) merged[^1] = (merged[^1].A, Math.Max(merged[^1].B, s.Item2));
                else merged.Add((s.Item1, s.Item2));
            }

            var hits = new List<Hit>();
            for (var li = 0; li < lines.Count; li++)
            {
                var pts = lines[li];
                for (var k = 0; k + 1 < pts.Length; k++)
                {
                    var (ax, ay, ah) = pts[k];
                    var (bx, by, bh) = pts[k + 1];
                    double pa = quer ? ax : ay, pb = quer ? bx : by;
                    if ((pa - c) * (pb - c) > 0 || Math.Abs(pb - pa) < 1e-9) continue;
                    if (pb == c && k + 2 < pts.Length) continue; // Punkt genau auf der Linie nur einmal zählen
                    var t = (c - pa) / (pb - pa);
                    var u = quer ? ay + (by - ay) * t : ax + (bx - ax) * t;
                    var rail = ah + (bh - ah) * t;
                    double dx = bx - ax, dy = by - ay, len = Math.Sqrt(dx * dx + dy * dy);
                    // cos: Anteil der Gleisrichtung quer zum Spant; beim fast parallelen Gleis wird die Auflage begrenzt
                    var across = Math.Abs(quer ? dx : dy) / len;
                    var along = (quer ? dy : dx) / len;
                    var half = spec.TrasseWidth / 2 / Math.Max(across, 0.25);
                    if (across < 0.25) tight.Add(i + 1);
                    // Höhe der Trasse ändert sich entlang des Spants um die Steigung mal dem Weg in Gleisrichtung
                    var slope = (bh - ah) / len * along;
                    var top = spec.BaseHeight + rail - build - spec.Thickness;
                    hits.Add(new Hit(u, half, top, slope, rail));
                }
            }

            foreach (var (sa, sb) in merged)
            {
                var own = hits.Where(h => h.U >= sa - h.HalfWidth && h.U <= sb + h.HalfWidth).OrderBy(h => h.U).ToList();
                if (own.Count == 0) continue;
                // Gleich hohe, sich überlappende Auflagen (Weichen, Parallelgleise) zusammenfassen
                var supports = new List<FrameSupport>();
                foreach (var h in own)
                {
                    double from = Math.Max(sa, h.U - h.HalfWidth), to = Math.Min(sb, h.U + h.HalfWidth);
                    if (to - from < 1) continue;
                    var s = new FrameSupport(from, to, h.Height + h.Slope * (from - h.U), h.Height + h.Slope * (to - h.U), h.Rail);
                    var same = supports.FindIndex(o => o.From <= s.To + 1 && s.From <= o.To + 1 && Math.Abs(o.HeightAt(Math.Clamp(s.From, o.From, o.To)) - s.HeightAt(Math.Clamp(o.From, s.From, s.To))) < spec.Thickness);
                    if (same >= 0)
                    {
                        var o = supports[same];
                        double f = Math.Min(o.From, s.From), t = Math.Max(o.To, s.To);
                        double hf = o.From <= s.From ? o.HeightFrom : s.HeightFrom, ht = o.To >= s.To ? o.HeightTo : s.HeightTo;
                        supports[same] = new FrameSupport(f, t, hf, ht, Math.Max(o.RailHeight, s.RailHeight));
                    }
                    else supports.Add(s);
                }
                if (supports.Count == 0) continue;
                supports = supports.OrderBy(s => s.From).ToList();
                supports = supports.Select(s => s with { HeightFrom = Round(s.HeightFrom), HeightTo = Round(s.HeightTo) }).ToList();
                if (supports.Any(s => s.HeightFrom < 5 || s.HeightTo < 5)) tooSteep = true;

                // Ausschnitte: Auflagen, über denen eine höhere Auflage liegt
                var cutouts = new List<FrameCutout>();
                var vehicle = gauge.ClearanceHeight - build - spec.Thickness;
                foreach (var s in supports)
                {
                    var above = supports.Where(o => o != s && o.From < s.To && s.From < o.To && o.Mid > s.Mid + spec.Thickness).ToList();
                    if (above.Count == 0) continue;
                    var top = Round(Math.Max(s.HeightFrom, s.HeightTo) + spec.Thickness + build + vehicle);
                    var lowestAbove = above.Min(o => Math.Min(o.HeightFrom, o.HeightTo));
                    if (lowestAbove - top < 10)
                        notes.Add(Lang.F($"Achtung Spant {ribs.Count + 1}: Zwischen Ausschnitt und oberer Trasse bleiben nur {Fmt.Num(lowestAbove - top, 0)} mm – Ebenenabstand prüfen (mindestens {Fmt.Num(gauge.ClearanceHeight + 10, 0)} mm Schienenoberkante zu Schienenoberkante)."));
                    cutouts.Add(new FrameCutout(s.From, s.To, s.HeightFrom, s.HeightTo, Math.Min(top, lowestAbove - 1)));
                }

                var outline = Outline(sa, sb, supports, MinRibHeight(gauge));
                var height = Math.Ceiling(outline.Max(o => o.H));
                ribs.Add(new FrameRib(ribs.Count + 1, Round(c), Round(sa), Round(sb), height, supports, cutouts, outline));
            }
        }

        var axis = quer ? Lang.T("quer (parallel zur Tiefe)") : Lang.T("längs (parallel zur Breite)");
        notes.Insert(0, Lang.F($"{ribs.Count} Spanten {axis}, Abstand {Fmt.Num(step, 0)} mm. Trassenbretter {Fmt.Num(spec.TrasseWidth, 0)} mm breit, {Fmt.Num(spec.Thickness, 0)} mm stark."));
        notes.Insert(1, Lang.F($"Höhen ab Spantfuß (= Oberkante Rahmen). Schienenoberkante bei Gleishöhe 0 liegt {Fmt.Num(spec.BaseHeight, 0)} mm darüber; Gleis und Bettung {Fmt.Num(build, 0)} mm."));
        notes.Insert(2, Lang.T("Auflagen mit zwei Höhen (a → b) sind schräg: Die Oberkante des Spants fällt dort mit der Steigung der Trasse. Längen und Lagen gemessen ab dem Anfang des Spants."));
        if (ribs.Any(r => r.Cutouts.Count > 0))
            notes.Add(Lang.F($"Ausschnitte: Dort führt eine untere Trasse durch den Spant; die Öffnung lässt {Fmt.Num(gauge.ClearanceHeight - build - spec.Thickness, 0)} mm über Schienenoberkante frei (lichte Höhe Spur {gauge.Id})."));
        if (tight.Count > 0)
            notes.Add(Lang.F($"Spant {string.Join(", ", tight.Distinct())}: Ein Gleis läuft fast parallel zum Spant; die Auflage ist auf {Fmt.Num(spec.TrasseWidth * 2, 0)} mm begrenzt. Dort besser einen zusätzlichen Querspant oder eine Längsleiste setzen."));
        if (tooSteep)
            notes.Add(Lang.T("Achtung: Einige Auflagen liegen fast auf dem Rahmen – die Höhe der Schienenoberkante bei Gleishöhe 0 vergrößern."));
        if (ribs.Count > 0 && ribs.Count < n)
            notes.Add(Lang.T("Stellen, an denen kein Gleis über den Spant führt, brauchen keinen Spant und fehlen in der Liste."));
        if (ribs.Count == 0)
            notes.Add(Lang.T("Im Plan liegen keine Gleise über der Anlagenplatte – es gibt keine Spanten."));
        return new FramePlan(spec with { Spacing = step }, gauge.Id, ribs, notes);
    }

    private static double Round(double v) => Math.Round(v * 2, MidpointRounding.AwayFromZero) / 2;

    /// <summary>Niedrigste Höhe des Spants zwischen den Auflagen, damit er steif bleibt (H0: 40 mm).</summary>
    public static double MinRibHeight(Gauge g) => Math.Max(30, Math.Round(g.TrackWidth * 2.5 / 10) * 10);

    /// <summary>
    /// Umriss des Spants: unten gerade auf dem Rahmen, oben an jeder Auflage auf der Höhe der obersten Trasse.
    /// Neben einer Auflage fällt die Oberkante unter 45° bis auf <paramref name="minHeight"/> ab; zwischen zwei Auflagen
    /// steht sie nie höher als die niedrigere der beiden, damit nichts neben dem Gleis hochsteht.
    /// </summary>
    private static List<(double U, double H)> Outline(double start, double end, List<FrameSupport> supports, double minHeight)
    {
        var cuts = supports.SelectMany(s => new[] { s.From, s.To }).Append(start).Append(end).Where(u => u >= start && u <= end).Distinct().OrderBy(u => u).ToList();
        var top = new List<(double U, double H)>();
        for (var i = 0; i + 1 < cuts.Count; i++)
        {
            double a = cuts[i], b = cuts[i + 1];
            if (b - a < 0.01) continue;
            var mid = (a + b) / 2;
            var cover = supports.Where(s => s.From <= mid && mid <= s.To).ToList();
            if (cover.Count > 0)
            {
                var s = cover.MaxBy(s => s.HeightAt(mid))!;
                top.Add((a - start, Round(s.HeightAt(a))));
                top.Add((b - start, Round(s.HeightAt(b))));
                continue;
            }
            // Lücke: Flanken unter 45° von den Rändern der Nachbar-Auflagen, nicht höher als die niedrigere
            var left = supports.Where(s => s.To <= a + 0.01).MaxBy(s => s.To);
            var right = supports.Where(s => s.From >= b - 0.01).MinBy(s => s.From);
            double hl = left is null ? double.NaN : TopAt(supports, a), hr = right is null ? double.NaN : TopAt(supports, b);
            var floor = Math.Min(minHeight, Math.Min(double.IsNaN(hl) ? double.MaxValue : hl, double.IsNaN(hr) ? double.MaxValue : hr));
            var cap = Math.Min(double.IsNaN(hl) ? double.MaxValue : hl, double.IsNaN(hr) ? double.MaxValue : hr);
            double H(double u)
            {
                var fl = double.IsNaN(hl) ? double.MinValue : hl - (u - a);
                var fr = double.IsNaN(hr) ? double.MinValue : hr - (b - u);
                return Math.Max(floor, Math.Min(Math.Max(fl, fr), cap));
            }
            var k = Math.Max(1, (int)Math.Ceiling((b - a) / 2.5));
            for (var j = 0; j <= k; j++)
            {
                var u = a + (b - a) * j / k;
                top.Add((u - start, Round(H(u))));
            }
        }
        var outline = new List<(double U, double H)> { (0, 0) };
        foreach (var p in top)
        {
            if (outline[^1] == p) continue;
            // Punkte auf einer Geraden weglassen
            if (outline.Count >= 2)
            {
                var (u1, h1) = outline[^2];
                var (u2, h2) = outline[^1];
                if (Math.Abs((u2 - u1) * (p.H - h1) - (h2 - h1) * (p.U - u1)) < 0.5 && u2 > u1 && p.U > u2) outline[^1] = p;
                else outline.Add(p);
            }
            else outline.Add(p);
        }
        outline.Add((end - start, 0));
        return outline;
    }

    private static double TopAt(List<FrameSupport> supports, double u)
        => supports.Where(s => s.From - 0.01 <= u && u <= s.To + 0.01).Select(s => s.HeightAt(u)).DefaultIfEmpty(0).Max();
}
