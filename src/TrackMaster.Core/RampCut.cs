using System.Text;

namespace TrackMaster.Core;

/// <summary>Maße der Trasse unter einer markierten Steigung (alle in mm).</summary>
public sealed record RampSpec(double Width, double Thickness, double MaxPartLength, double LascheLength)
{
    /// <summary>Fehler in den Maßen, leer wenn alles passt.</summary>
    public List<string> Problems()
    {
        var errors = new List<string>();
        if (Width < 10 || Width > 1000) errors.Add(Lang.T("Trassenbreite: zwischen 10 und 1000 mm."));
        if (Thickness < 1 || Thickness > 50) errors.Add(Lang.T("Holzstärke: zwischen 1 und 50 mm."));
        if (MaxPartLength < 100 || MaxPartLength > Helix.SheetLength) errors.Add(Lang.F($"Größte Teillänge: zwischen 100 und {Fmt.Num(Helix.SheetLength, 0)} mm."));
        if (LascheLength < 0 || LascheLength > 1000) errors.Add(Lang.T("Laschenlänge: zwischen 0 (keine Laschen) und 1000 mm."));
        return errors;
    }
}

/// <summary>Ein Stoß zwischen zwei Gleisen auf einem Trassenteil: Querstrich in Teilkoordinaten und Höhe der Schienenoberkante.</summary>
public sealed record RampTick(double X0, double Y0, double X1, double Y1, double Z);

/// <summary>
/// Ein Trassenteil, schon für den Zuschnitt gedreht: Umriss, Gleismitte und Stöße in Teilkoordinaten (ab 0, 0),
/// <see cref="From"/> und <see cref="To"/> als Weg entlang der Gleismitte ab dem Anfang der Gleisfolge.
/// </summary>
public sealed record RampPart(int Number, int Chain, double From, double To, double HeightFrom, double HeightTo,
    IReadOnlyList<(double X, double Y)> Outline, IReadOnlyList<(double X, double Y)> Center, IReadOnlyList<RampTick> Ticks,
    double Width, double Height)
{
    public double Length => To - From;
    public string Label => $"T{Number}";
}

/// <summary>Trasse einer Steigung: Teile entlang der Gleise und Stoßlaschen darunter.</summary>
public sealed record RampCut(RampSpec Spec, int Chains, double TotalLength, IReadOnlyList<RampPart> Parts, int Laschen, IReadOnlyList<string> Notes)
{
    public string ToText()
    {
        var lines = new List<string>
        {
            Lang.F($"Trasse {Fmt.Num(Spec.Width, 0)} mm breit aus {Fmt.Num(Spec.Thickness, 0)} mm Sperrholz, {Fmt.Num(TotalLength, 0)} mm entlang der Gleismitte, {Parts.Count} Teil(e)"),
        };
        if (Chains > 1) lines.Add(Lang.F($"{Chains} getrennte Gleisfolgen, jede mit eigener Trasse."));
        lines.Add("");
        foreach (var p in Parts)
            lines.Add(Lang.F($"{p.Label}: {Fmt.Num(p.From, 0)}–{Fmt.Num(p.To, 0)} mm, Länge {Fmt.Num(p.Length, 0)} mm, Höhe {Fmt.Num(p.HeightFrom, 0)}–{Fmt.Num(p.HeightTo, 0)} mm, Zuschnitt {Fmt.Num(p.Width, 0)} × {Fmt.Num(p.Height, 0)} mm"));
        if (Laschen > 0)
            lines.Add(Lang.F($"Stoßlaschen: {Laschen} × {Fmt.Num(Spec.LascheLength, 0)} × {Fmt.Num(Spec.Width, 0)} mm, je eine unter jedem Stoß"));
        if (Notes.Count > 0)
        {
            lines.Add("");
            lines.AddRange(Notes);
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Jedes Trassenteil und jede Stoßlasche als eigene SVG-Datei in Originalgröße.</summary>
    public IReadOnlyList<SvgFile> ToSvgFiles(string title)
    {
        string N(double v) => SvgFiles.N(v);
        var files = new List<SvgFile>();
        var count = Math.Max(Parts.Count, Laschen);
        foreach (var p in Parts)
        {
            var body = new StringBuilder();
            body.Append($"<path d=\"{SvgFiles.Polygon(p.Outline)}\" class=\"cut\"/>");
            body.Append($"<path d=\"{SvgFiles.Polyline(p.Center)}\" class=\"mark\"/>");
            foreach (var t in p.Ticks)
            {
                body.Append($"<path d=\"M{N(t.X0)} {N(t.Y0)}L{N(t.X1)} {N(t.Y1)}\" class=\"tick\"/>");
                body.Append($"<text x=\"{N(t.X0)}\" y=\"{N(t.Y0 - 2)}\" font-size=\"6\" class=\"label\">{SvgFiles.Esc(Lang.F($"H {Fmt.Num(t.Z, 0)}"))}</text>");
            }
            var mid = p.Center[p.Center.Count / 2];
            var size = Math.Clamp(Spec.Width / 3, 8, 30);
            body.Append($"<text x=\"{N(mid.X)}\" y=\"{N(mid.Y + size * 0.35)}\" font-size=\"{N(size)}\" class=\"label\">{p.Label}</text>");
            var caption = Lang.F($"{title} · Trasse {p.Label} · Länge {Fmt.Num(p.Length, 0)} mm · Höhe {Fmt.Num(p.HeightFrom, 0)}–{Fmt.Num(p.HeightTo, 0)} mm · {Fmt.Num(Spec.Thickness, 0)} mm · Maßstab 1:1 · gestrichelt: Gleismitte, Querstriche: Gleisstöße mit Höhe");
            files.Add(new($"{Lang.T("Trasse")} T{SvgFiles.Number(p.Number, count)}.svg", SvgFiles.Document($"{title} – {p.Label}", caption, p.Width, p.Height, body.ToString())));
        }
        for (var i = 1; i <= Laschen; i++)
        {
            var body = $"<rect x=\"0\" y=\"0\" width=\"{N(Spec.LascheLength)}\" height=\"{N(Spec.Width)}\" class=\"cut\"/>"
                       + $"<text x=\"{N(Spec.LascheLength / 2)}\" y=\"{N(Spec.Width / 2 + 4)}\" font-size=\"{N(Math.Clamp(Spec.Width / 4, 8, 30))}\" class=\"label\">L{i}</text>";
            var caption = Lang.F($"{title} · Stoßlasche L{i} · {Fmt.Num(Spec.LascheLength, 0)} × {Fmt.Num(Spec.Width, 0)} × {Fmt.Num(Spec.Thickness, 0)} mm · Maßstab 1:1");
            files.Add(new($"{Lang.T("Trasse")} L{SvgFiles.Number(i, count)}.svg", SvgFiles.Document($"{title} – L{i}", caption, Spec.LascheLength, Spec.Width, body)));
        }
        return files;
    }
}

/// <summary>
/// Trasse für markierte Gleise, vor allem Steigungen: Die Gleisfolge wird von ihrem unteren Ende aus abgegangen und die Trasse
/// (Gleismitte ± halbe Breite) in gleich lange Teile bis zur größten Teillänge geteilt. Jedes Teil wird für den Zuschnitt gerade gedreht.
/// </summary>
public static class RampTrasse
{
    public static RampSpec Defaults(Gauge g)
    {
        var f = FrameBuild.Defaults(g);
        return new RampSpec(f.TrasseWidth, f.Thickness, 1000, Math.Max(100, Math.Round(f.TrasseWidth * 1.5 / 10) * 10));
    }

    private readonly record struct Sample(double X, double Y, double Heading, double Z, double S, bool Joint);

    public static (RampCut? Cut, string? Error) Build(LayoutPlan plan, IReadOnlySet<int> ids, RampSpec spec)
    {
        var sel = ids.Where(id => plan.Find(id) is { } p && !p.Def.IsTable).ToHashSet();
        if (sel.Count == 0) return (null, Lang.T("Bitte zuerst die Gleise der Steigung markieren."));
        if (spec.Problems() is { Count: > 0 } errors) return (null, string.Join(Environment.NewLine, errors));
        var conn = Connectivity.Analyze(plan);
        int Degree(int id) => conn.Of(id).Count(c => c.Link is { } l && sel.Contains(l.PieceId));
        var visited = new HashSet<int>();
        var chains = new List<List<Sample>>();
        while (visited.Count < sel.Count)
        {
            // Am unteren Ende einer Folge beginnen; ein Gleis mit höchstens einem markierten Nachbarn ist ein echtes Ende
            var start = conn.All
                .Where(c => sel.Contains(c.PieceId) && !visited.Contains(c.PieceId) && !(c.Link is { } l && sel.Contains(l.PieceId)))
                .OrderBy(c => Degree(c.PieceId) <= 1 ? 0 : 1).ThenBy(c => c.Z).ThenBy(c => c.PieceId).ThenBy(c => c.Index)
                .FirstOrDefault()
                ?? conn.Find(sel.First(id => !visited.Contains(id)), 0)!;
            chains.Add(Walk(plan, conn, sel, visited, start));
        }

        var parts = new List<RampPart>();
        var notes = new List<string>();
        var laschen = 0;
        double total = 0, maxGrade = 0;
        for (var c = 0; c < chains.Count; c++)
        {
            var samples = chains[c];
            var length = samples[^1].S;
            if (length < 1) continue;
            total += length;
            maxGrade = Math.Max(maxGrade, Math.Abs(samples[^1].Z - samples[0].Z) / length);
            var n = Math.Max(1, (int)Math.Ceiling(length / spec.MaxPartLength - 1e-6));
            for (var j = 0; j < n; j++)
                parts.Add(Part(samples, length * j / n, length * (j + 1) / n, parts.Count + 1, c + 1, spec.Width));
            var closed = n > 1 && Math.Abs(samples[0].X - samples[^1].X) + Math.Abs(samples[0].Y - samples[^1].Y) < 1 && Math.Abs(samples[0].Z - samples[^1].Z) < 2;
            if (spec.LascheLength > 0) laschen += closed ? n : n - 1;
        }
        if (parts.Count == 0) return (null, Lang.T("Die markierten Gleise ergeben keine Trasse."));

        double sl = Helix.SheetLength - 2 * SvgFiles.Margin, sw = Helix.SheetWidth - 2 * SvgFiles.Margin;
        foreach (var p in parts.Where(p => !(p.Width <= sl && p.Height <= sw) && !(p.Width <= sw && p.Height <= sl)))
            notes.Add(Lang.F($"Achtung {p.Label}: {Fmt.Num(p.Width, 0)} × {Fmt.Num(p.Height, 0)} mm passt nicht auf eine Platte {Fmt.Num(Helix.SheetLength, 0)} × {Fmt.Num(Helix.SheetWidth, 0)} mm – bitte die größte Teillänge verkleinern."));
        if (maxGrade > 0.0005)
            notes.Add(Lang.F($"Die Längen sind waagerecht gemessen. Auf der Schräge ist die Trasse bei {Fmt.Num(maxGrade * 100, 1)} % Steigung {Fmt.Num((Math.Sqrt(1 + maxGrade * maxGrade) - 1) * 1000, 1)} mm je Meter länger – beim Zuschnitt vernachlässigbar."));
        return (new RampCut(spec, chains.Count, total, parts, laschen, notes), null);
    }

    /// <summary>Geht die markierte Gleisfolge ab <paramref name="start"/> ab und tastet die Gleismitte mit Höhe ab.</summary>
    private static List<Sample> Walk(LayoutPlan plan, Connectivity conn, HashSet<int> sel, HashSet<int> visited, ConnectorRef start)
    {
        var samples = new List<Sample>();
        var piece = plan.Find(start.PieceId)!;
        var entry = start.Index;
        double s = 0;
        while (true)
        {
            visited.Add(piece.Id);
            var d = piece.Def;
            var count = TrackGeometry.LocalConnectors(d, piece.Flipped).Count;
            ConnectorRef? Next(int k) => conn.Find(piece.Id, k)?.Link is { } l && sel.Contains(l.PieceId) && !visited.Contains(l.PieceId) ? l : null;
            int exit, path;
            bool reverse;
            if (d.Kind == PieceKind.Crossing)
            {
                exit = entry switch { 0 => 1, 1 => 0, 2 => 3, _ => 2 };
                path = entry < 2 ? 0 : 1;
                reverse = entry is 1 or 3;
            }
            else if (entry == 0)
            {
                exit = Enumerable.Range(1, Math.Max(count - 1, 0)).Where(k => Next(k) is not null).DefaultIfEmpty(count > 1 ? 1 : 0).First();
                path = Math.Max(exit - 1, 0);
                reverse = false;
            }
            else
            {
                exit = 0;
                path = entry - 1;
                reverse = true;
            }
            var paths = piece.WorldPaths();
            if (path >= paths.Count) path = 0;
            var legs = reverse ? paths[path].Reverse().ToList() : paths[path].ToList();
            var heights = piece.ConnectorHeights();
            double z0 = heights[Math.Min(entry, heights.Count - 1)], z1 = heights[Math.Min(exit, heights.Count - 1)];
            var pathLength = legs.Sum(l => l.TrackLength);
            double local = 0;
            foreach (var leg in legs)
            {
                var steps = leg.IsArc ? Math.Max(2, (int)Math.Ceiling(leg.TrackLength / 5)) : 1;
                for (var i = 0; i <= steps; i++)
                {
                    var t = (double)i / steps;
                    var pose = leg.At(reverse ? 1 - t : t);
                    var dist = local + leg.TrackLength * t;
                    var z = pathLength > 0 ? z0 + (z1 - z0) * dist / pathLength : z0;
                    var sample = new Sample(pose.X, pose.Y, reverse ? pose.Heading + 180 : pose.Heading, z, s + dist, false);
                    if (samples.Count > 0 && Math.Abs(samples[^1].X - sample.X) + Math.Abs(samples[^1].Y - sample.Y) < 0.01)
                    {
                        // Übergang zwischen zwei Gleisen: als Stoß markieren, nicht doppelt aufnehmen
                        if (i == 0 && local == 0 && samples.Count > 1) samples[^1] = samples[^1] with { Joint = true };
                        continue;
                    }
                    samples.Add(sample);
                }
                local += leg.TrackLength;
            }
            s += pathLength;
            if (exit == entry || Next(exit) is not { } next) break;
            piece = plan.Find(next.PieceId)!;
            entry = next.Index;
        }
        return samples;
    }

    private static Sample At(List<Sample> samples, double s)
    {
        if (s <= samples[0].S) return samples[0];
        for (var k = 1; k < samples.Count; k++)
        {
            if (samples[k].S < s) continue;
            var a = samples[k - 1];
            var b = samples[k];
            var f = b.S - a.S < 1e-9 ? 0 : (s - a.S) / (b.S - a.S);
            return new Sample(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f, a.Heading + Angles.Normalize(b.Heading - a.Heading) * f, a.Z + (b.Z - a.Z) * f, s, false);
        }
        return samples[^1];
    }

    private static RampPart Part(List<Sample> samples, double from, double to, int number, int chain, double width)
    {
        var pts = new List<Sample> { At(samples, from) };
        pts.AddRange(samples.Where(x => x.S > from + 0.01 && x.S < to - 0.01));
        pts.Add(At(samples, to));
        var half = width / 2;
        (double X, double Y) Side(Sample p, double off)
        {
            var h = p.Heading * Math.PI / 180;
            return (p.X - off * Math.Sin(h), p.Y + off * Math.Cos(h));
        }
        var outline = pts.Select(p => Side(p, half)).Concat(pts.AsEnumerable().Reverse().Select(p => Side(p, -half))).ToList();
        var center = pts.Select(p => (p.X, p.Y)).ToList();
        var ticks = pts.Where(p => p.Joint).Select(p => (A: Side(p, half), B: Side(p, -half), p.Z)).ToList();

        // Gerade drehen: Sehne vom Anfang zum Ende waagerecht
        double dx = pts[^1].X - pts[0].X, dy = pts[^1].Y - pts[0].Y;
        var angle = Math.Sqrt(dx * dx + dy * dy) < 1 ? 0 : Math.Atan2(dy, dx);
        double cos = Math.Cos(-angle), sin = Math.Sin(-angle);
        (double X, double Y) Rot((double X, double Y) q) => (q.X * cos - q.Y * sin, q.X * sin + q.Y * cos);
        var rotated = outline.Select(Rot).ToList();
        double minX = rotated.Min(q => q.X), minY = rotated.Min(q => q.Y);
        (double X, double Y) Local((double X, double Y) q) { var r = Rot(q); return (r.X - minX, r.Y - minY); }
        var localOutline = rotated.Select(q => (q.X - minX, q.Y - minY)).ToList();
        return new RampPart(number, chain, from, to, pts[0].Z, pts[^1].Z,
            localOutline,
            center.Select(Local).ToList(),
            ticks.Select(t => { var a = Local(t.A); var b = Local(t.B); return new RampTick(a.X, a.Y, b.X, b.Y, t.Z); }).ToList(),
            localOutline.Max(q => q.Item1), localOutline.Max(q => q.Item2));
    }
}
