namespace TrackMaster.Core;

/// <summary>Bauart der Beine.</summary>
public enum BenchLegStyle
{
    /// <summary>Ein Kantholz je Bein.</summary>
    Kantholz,
    /// <summary>L-Bein aus zwei verleimten Latten (leicht und sehr steif).</summary>
    LBein,
}

/// <summary>
/// Unterbau der Anlage aus Holz: Rahmen (Zargen und Querriegel) unter jedem Plattenteil, Beine, Fußtraversen und Deckplatte.
/// Alle Maße in mm; Höhe 0 ist die Oberkante der Platte.
/// </summary>
public sealed class BenchworkSpec
{
    /// <summary>Oberkante Platte über dem Fußboden.</summary>
    public double Height { get; set; } = 1000;
    /// <summary>Stärke der Deckplatte (0 = offene Rahmenbauweise ohne Platte).</summary>
    public double TopThickness { get; set; } = 10;
    /// <summary>Zarge und Querriegel: Stärke × Höhe der Latte.</summary>
    public double FrameThickness { get; set; } = 18;
    public double FrameHeight { get; set; } = 70;
    /// <summary>Querriegel höchstens in diesem Abstand.</summary>
    public double CrossSpacing { get; set; } = 500;
    public BenchLegStyle LegStyle { get; set; } = BenchLegStyle.Kantholz;
    /// <summary>Querschnitt der Beine (beim L-Bein: Schenkelbreite × Schenkelbreite, Stärke wie die Zarge).</summary>
    public double LegWidth { get; set; } = 70;
    public double LegDepth { get; set; } = 70;
    /// <summary>Beine höchstens in diesem Abstand entlang der Zargen.</summary>
    public double LegSpacing { get; set; } = 1200;
    /// <summary>Fußtraversen zwischen den Beinen.</summary>
    public bool Braces { get; set; } = true;
    public double BraceHeight { get; set; } = 150;
    /// <summary>Höhenverstellbare Stellfüße unter den Beinen.</summary>
    public bool Feet { get; set; } = true;
    public double FootHeight { get; set; } = 30;
    /// <summary>Handelslänge der Latten und Kanthölzer.</summary>
    public double StockLength { get; set; } = 2500;
    /// <summary>Plattenformat der Deckplatte.</summary>
    public double SheetWidth { get; set; } = 2500;
    public double SheetDepth { get; set; } = 1250;

    public BenchworkSpec Clone() => (BenchworkSpec)MemberwiseClone();

    /// <summary>Vorschlag je Spur: große Spuren bekommen kräftigere Latten.</summary>
    public static BenchworkSpec Defaults(Gauge g) => g.TrackWidth >= 30
        ? new BenchworkSpec { Height = 800, TopThickness = 15, FrameThickness = 22, FrameHeight = 95, CrossSpacing = 450, LegWidth = 90, LegDepth = 90, LegSpacing = 1000 }
        : new BenchworkSpec();

    public string FrameSection => $"{Fmt.Num(FrameThickness, 0)} × {Fmt.Num(FrameHeight, 0)}";
}

public enum BenchKind { Zarge, Querriegel, Bein, Traverse, Platte }

/// <summary>Ein Holzteil als achsparalleler Quader (x, y wie im Plan, z nach oben, 0 = Oberkante Platte).</summary>
public sealed record BenchPiece(BenchKind Kind, string Section, double Length, double X0, double Y0, double Z0, double X1, double Y1, double Z1);

/// <summary>Zuschnitt: so viele gleich lange Teile eines Querschnitts.</summary>
public sealed record BenchCut(string Use, string Section, double Length, int Count);

/// <summary>Zeile der Materialliste.</summary>
public sealed record BenchMaterial(string Item, string Size, double Quantity, string Unit, string Note);

/// <summary>Ergebnis: alle Teile, Zuschnittliste, Materialliste und Hinweise.</summary>
public sealed record BenchworkPlan(BenchworkSpec Spec, IReadOnlyList<BenchPiece> Pieces, IReadOnlyList<BenchCut> Cuts, IReadOnlyList<BenchMaterial> Materials, IReadOnlyList<string> Notes)
{
    public int LegCount => Pieces.Count(p => p.Kind == BenchKind.Bein) / (Spec.LegStyle == BenchLegStyle.LBein ? 2 : 1);

    public string ToText()
    {
        var lines = new List<string> { Lang.T("Unterbau – Materialliste"), "" };
        foreach (var m in Materials)
            lines.Add($"{Fmt.Num(m.Quantity, m.Unit == "m" || m.Unit == "m²" ? 1 : 0)} {m.Unit}  {m.Item} {m.Size}{(m.Note.Length > 0 ? "  (" + m.Note + ")" : "")}");
        lines.Add("");
        lines.Add(Lang.T("Zuschnitt"));
        foreach (var c in Cuts) lines.Add($"{c.Count} × {Fmt.Num(c.Length, 0)} mm  {c.Use} {c.Section}");
        if (Notes.Count > 0) { lines.Add(""); lines.AddRange(Notes); }
        return string.Join(Environment.NewLine, lines);
    }

    public string ToCsv()
    {
        static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        var lines = new List<string> { Lang.T("Material;Maß;Menge;Einheit;Hinweis") };
        foreach (var m in Materials) lines.Add($"{Q(m.Item)};{Q(m.Size)};{Fmt.Num(m.Quantity, 2)};{m.Unit};{Q(m.Note)}");
        lines.Add("");
        lines.Add(Lang.T("Zuschnitt;Querschnitt;Länge (mm);Anzahl"));
        foreach (var c in Cuts) lines.Add($"{Q(c.Use)};{Q(c.Section)};{Fmt.Num(c.Length, 0)};{c.Count}");
        lines.Add("");
        lines.AddRange(Notes.Select(Q));
        return string.Join("\r\n", lines);
    }
}

/// <summary>Berechnet den Holz-Unterbau unter den Plattenteilen (Rechtecke der Plattenform bzw. Module).</summary>
public static class Benchwork
{
    /// <summary>Sägeschnitt beim Ablängen.</summary>
    public const double Kerf = 3;

    public static string UseName(BenchKind k) => k switch
    {
        BenchKind.Zarge => Lang.T("Zarge"),
        BenchKind.Querriegel => Lang.T("Querriegel"),
        BenchKind.Bein => Lang.T("Bein"),
        BenchKind.Traverse => Lang.T("Fußtraverse"),
        _ => Lang.T("Deckplatte"),
    };

    /// <summary>Prüft die Maße; liefert Fehlermeldungen (leer = in Ordnung).</summary>
    public static List<string> Validate(BenchworkSpec s)
    {
        var e = new List<string>();
        void Range(double v, string name, double min, double max)
        {
            if (!double.IsFinite(v) || v < min || v > max) e.Add(Lang.F($"{name}: zwischen {Fmt.Num(min, 0)} und {Fmt.Num(max, 0)} mm."));
        }
        Range(s.Height, Lang.T("Höhe Oberkante"), 200, 2500);
        Range(s.TopThickness, Lang.T("Plattenstärke"), 0, 50);
        Range(s.FrameThickness, Lang.T("Lattenstärke"), 8, 100);
        Range(s.FrameHeight, Lang.T("Lattenhöhe"), 20, 300);
        Range(s.CrossSpacing, Lang.T("Abstand der Querriegel"), 150, 2000);
        Range(s.LegWidth, Lang.T("Beinbreite"), 20, 200);
        Range(s.LegDepth, Lang.T("Beintiefe"), 20, 200);
        Range(s.LegSpacing, Lang.T("Beinabstand"), 300, 4000);
        Range(s.BraceHeight, Lang.T("Höhe der Fußtraversen"), 50, 1000);
        Range(s.FootHeight, Lang.T("Höhe der Stellfüße"), 0, 200);
        Range(s.StockLength, Lang.T("Lattenlänge im Handel"), 500, 6000);
        Range(s.SheetWidth, Lang.T("Plattenformat Breite"), 300, 5000);
        Range(s.SheetDepth, Lang.T("Plattenformat Tiefe"), 300, 5000);
        if (e.Count == 0 && s.TopThickness + s.FrameHeight + s.BraceHeight + s.FrameHeight + (s.Feet ? s.FootHeight : 0) > s.Height)
            e.Add(Lang.T("Die Höhe reicht nicht für Platte, Zarge, Fußtraverse und Stellfüße."));
        return e;
    }

    public static BenchworkPlan Build(LayoutPlan plan, BenchworkSpec spec)
    {
        var s = spec;
        var t = s.FrameThickness;
        var zTop = -s.TopThickness;                 // Unterkante Deckplatte = Oberkante Rahmen
        var zFrame = zTop - s.FrameHeight;          // Unterkante Rahmen
        var zFloor = -s.Height;
        var zLegBottom = zFloor + (s.Feet ? s.FootHeight : 0);
        var legLength = zTop - zLegBottom;
        var pieces = new List<BenchPiece>();
        var notes = new List<string>();
        var legs = new List<(double X0, double Y0, double X1, double Y1)>();
        var frameSection = s.FrameSection;
        var legSection = s.LegStyle == BenchLegStyle.LBein ? frameSection : $"{Fmt.Num(s.LegWidth, 0)} × {Fmt.Num(s.LegDepth, 0)}";
        var rects = BoardGeometry.Rects(plan);

        void Add(BenchKind kind, string section, double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var length = Math.Max(Math.Max(x1 - x0, y1 - y0), z1 - z0);
            if (length < 1) return;
            pieces.Add(new BenchPiece(kind, section, Math.Round(length), x0, y0, z0, x1, y1, z1));
        }

        void Leg(double x0, double y0)
        {
            double x1 = x0 + s.LegWidth, y1 = y0 + s.LegDepth;
            // Beine benachbarter Plattenteile an derselben Stelle nur einmal
            if (legs.Any(l => x0 < l.X1 && x1 > l.X0 && y0 < l.Y1 && y1 > l.Y0)) return;
            legs.Add((x0, y0, x1, y1));
            if (s.LegStyle == BenchLegStyle.Kantholz)
            {
                Add(BenchKind.Bein, legSection, x0, y0, zLegBottom, x1, y1, zTop);
                return;
            }
            // L-Bein: eine Latte über die volle Breite, die zweite rechtwinklig daran
            Add(BenchKind.Bein, frameSection, x0, y0, zLegBottom, x1, y0 + t, zTop);
            Add(BenchKind.Bein, $"{Fmt.Num(t, 0)} × {Fmt.Num(s.LegDepth - t, 0)}", x0, y0 + t, zLegBottom, x0 + t, y1, zTop);
        }

        foreach (var r in rects)
        {
            double w = r.X1 - r.X0, d = r.Y1 - r.Y0;
            if (w < 2 * t + 1 || d < 2 * t + 1) continue;
            var alongX = w >= d;
            // In Längsrichtung (u) und Querrichtung (v) rechnen, dann zurück in x, y
            double u0 = alongX ? r.X0 : r.Y0, u1 = alongX ? r.X1 : r.Y1, v0 = alongX ? r.Y0 : r.X0, v1 = alongX ? r.Y1 : r.X1;
            void Box(BenchKind kind, string section, double a0, double b0, double z0, double a1, double b1, double z1)
            {
                if (alongX) Add(kind, section, a0, b0, z0, a1, b1, z1);
                else Add(kind, section, b0, a0, z0, b1, a1, z1);
            }
            void LegAt(double a, double b)
            {
                if (alongX) Leg(a, b);
                else Leg(b, a);
            }
            var legU = alongX ? s.LegWidth : s.LegDepth;
            var legV = alongX ? s.LegDepth : s.LegWidth;
            // Zargen: lange Seiten durchgehend, Stirnseiten dazwischen
            Box(BenchKind.Zarge, frameSection, u0, v0, zFrame, u1, v0 + t, zTop);
            Box(BenchKind.Zarge, frameSection, u0, v1 - t, zFrame, u1, v1, zTop);
            Box(BenchKind.Zarge, frameSection, u0, v0 + t, zFrame, u0 + t, v1 - t, zTop);
            Box(BenchKind.Zarge, frameSection, u1 - t, v0 + t, zFrame, u1, v1 - t, zTop);
            // Querriegel gleichmäßig verteilt
            var inner = u1 - u0 - t;
            var fields = Math.Max(1, (int)Math.Ceiling(inner / s.CrossSpacing - 1e-9));
            for (var i = 1; i < fields; i++)
            {
                var c = u0 + t / 2 + inner * i / fields;
                Box(BenchKind.Querriegel, frameSection, c - t / 2, v0 + t, zFrame, c + t / 2, v1 - t, zTop);
            }
            // Beine innen in den Ecken und entlang der langen Seiten
            var span = u1 - u0 - 2 * t - legU;
            var gaps = Math.Max(1, (int)Math.Ceiling(span / s.LegSpacing - 1e-9));
            var bothRows = v1 - v0 - 2 * t >= 2 * legV;
            var positions = new List<double>();
            for (var i = 0; i <= gaps; i++) positions.Add(u0 + t + span * i / gaps);
            foreach (var a in positions)
            {
                LegAt(a, v0 + t);
                if (bothRows) LegAt(a, v1 - t - legV);
            }
            // Sehr tiefe Teile: auch an den Stirnseiten Zwischenbeine
            var vSpan = v1 - v0 - 2 * t - legV;
            var vGaps = (int)Math.Ceiling(vSpan / s.LegSpacing - 1e-9);
            for (var j = 1; j < vGaps; j++)
            {
                LegAt(u0 + t, v0 + t + vSpan * j / vGaps);
                LegAt(u1 - t - legU, v0 + t + vSpan * j / vGaps);
            }
            if (s.Braces && bothRows)
            {
                var zb = zFloor + s.BraceHeight;
                // quer zwischen jedem Beinpaar, außen an den Beinen
                foreach (var a in positions)
                {
                    var side = a + legU / 2 < (u0 + u1) / 2 ? a + legU : a - t;
                    Box(BenchKind.Traverse, frameSection, side, v0 + t, zb, side + t, v1 - t, zb + s.FrameHeight);
                }
                // längs in der Mitte, auf den Quertraversen
                var vm = (v0 + v1) / 2;
                Box(BenchKind.Traverse, frameSection, positions[0] + legU, vm - t / 2, zb + s.FrameHeight, positions[^1], vm + t / 2, zb + 2 * s.FrameHeight);
            }
            if (s.TopThickness > 0)
                Box(BenchKind.Platte, $"{Fmt.Num(s.TopThickness, 0)} mm", u0, v0, zTop, u1, v1, 0);
        }

        // Zuschnitt
        var cuts = pieces.Where(p => p.Kind != BenchKind.Platte)
            .GroupBy(p => (p.Kind, p.Section, p.Length))
            .OrderBy(g => g.Key.Kind).ThenByDescending(g => g.Key.Length)
            .Select(g => new BenchCut(UseName(g.Key.Kind), g.Key.Section, g.Key.Length, g.Count())).ToList();
        foreach (var c in cuts.Where(c => c.Length > s.StockLength))
            notes.Add(Lang.F($"{c.Use} {Fmt.Num(c.Length, 0)} mm ist länger als die Handelslänge ({Fmt.Num(s.StockLength, 0)} mm): aus zwei Stücken mit Lasche stoßen."));

        // Materialliste
        var materials = new List<BenchMaterial>();
        foreach (var bySection in pieces.Where(p => p.Kind != BenchKind.Platte).GroupBy(p => p.Section).OrderBy(g => g.Key))
        {
            var lengths = bySection.Select(p => p.Length).ToList();
            var bars = Bars(lengths, s.StockLength);
            var kind = bySection.All(p => p.Kind == BenchKind.Bein) && s.LegStyle == BenchLegStyle.Kantholz ? Lang.T("Kantholz") : Lang.T("Latte");
            var uses = string.Join(", ", bySection.Select(p => UseName(p.Kind)).Distinct());
            materials.Add(new BenchMaterial(kind, Lang.F($"{bySection.Key} mm, {Fmt.Num(s.StockLength / 1000, 2)} m lang"), bars, Lang.T("Stück"),
                Lang.F($"{uses}; {Fmt.Num(lengths.Sum() / 1000, 1)} m Zuschnitt")));
        }
        var tops = pieces.Where(p => p.Kind == BenchKind.Platte).ToList();
        if (tops.Count > 0)
        {
            var area = tops.Sum(p => (p.X1 - p.X0) * (p.Y1 - p.Y0)) / 1e6;
            var sheets = Sheets(tops.Select(p => (p.X1 - p.X0, p.Y1 - p.Y0)).ToList(), s.SheetWidth, s.SheetDepth);
            materials.Add(new BenchMaterial(Lang.T("Sperrholzplatte"), Lang.F($"{Fmt.Num(s.TopThickness, 0)} mm, {Fmt.Num(s.SheetWidth, 0)} × {Fmt.Num(s.SheetDepth, 0)} mm"), sheets, Lang.T("Stück"),
                Lang.F($"{Fmt.Num(area, 2)} m² Plattenfläche")));
            foreach (var p in tops.Where(p => Math.Max(p.X1 - p.X0, p.Y1 - p.Y0) > Math.Max(s.SheetWidth, s.SheetDepth) || Math.Min(p.X1 - p.X0, p.Y1 - p.Y0) > Math.Min(s.SheetWidth, s.SheetDepth)))
                notes.Add(Lang.F($"Deckplatte {Fmt.Num(p.X1 - p.X0, 0)} × {Fmt.Num(p.Y1 - p.Y0, 0)} mm ist größer als das Plattenformat: über einem Querriegel stoßen."));
        }
        var legCount = legs.Count;
        var frameJoints = pieces.Count(p => p.Kind is BenchKind.Querriegel) + pieces.Count(p => p.Kind == BenchKind.Zarge) / 2;
        var braces = pieces.Count(p => p.Kind == BenchKind.Traverse);
        var topScrews = tops.Count == 0 ? 0 : (int)Math.Ceiling(pieces.Where(p => p.Kind is BenchKind.Zarge or BenchKind.Querriegel).Sum(p => p.Length) / 200);
        materials.Add(new BenchMaterial(Lang.T("Holzschrauben"), "4 × 50 mm", Round10(frameJoints * 4 + braces * 4 + (s.LegStyle == BenchLegStyle.LBein ? legCount * Math.Ceiling(legLength / 150) : 0)), Lang.T("Stück"), Lang.T("Rahmen, Traversen, L-Beine")));
        materials.Add(new BenchMaterial(Lang.T("Schlossschrauben mit Mutter"), "M8 × 80 mm", legCount * 2, Lang.T("Stück"), Lang.T("Beine an der Zarge")));
        if (topScrews > 0) materials.Add(new BenchMaterial(Lang.T("Spanplattenschrauben"), "3,5 × 30 mm", Round10(topScrews), Lang.T("Stück"), Lang.T("Deckplatte, alle 20 cm")));
        if (s.Feet) materials.Add(new BenchMaterial(Lang.T("Stellfüße"), Lang.F($"M8, {Fmt.Num(s.FootHeight, 0)} mm"), legCount, Lang.T("Stück"), Lang.T("mit Einschlagmutter")));
        materials.Add(new BenchMaterial(Lang.T("Holzleim"), "D3", Math.Max(1, Math.Ceiling(pieces.Count / 60.0)), Lang.T("Flasche"), ""));

        notes.Insert(0, Lang.F($"Plattenteile: {rects.Count}, Beine: {legCount} je {Fmt.Num(legLength, 0)} mm lang, Oberkante {Fmt.Num(s.Height, 0)} mm über dem Boden."));
        if (s.TopThickness == 0) notes.Add(Lang.T("Ohne Deckplatte: offene Rahmenbauweise, Trassen kommen auf die Querriegel (siehe Spantenbau)."));
        return new BenchworkPlan(s.Clone(), pieces, cuts, materials, notes);
    }

    private static double Round10(double n) => Math.Ceiling(n / 10) * 10;

    /// <summary>Anzahl Handelslängen nach „größtes Stück zuerst“; zu lange Teile zählen anteilig.</summary>
    public static int Bars(IReadOnlyList<double> lengths, double stock)
    {
        var free = new List<double>();
        var count = 0;
        foreach (var l in lengths.OrderByDescending(x => x))
        {
            var rest = l;
            while (rest > stock) { count++; rest -= stock; }
            var i = free.FindIndex(f => f >= rest);
            if (i >= 0) free[i] -= rest + Kerf;
            else { count++; free.Add(stock - rest - Kerf); }
        }
        return count;
    }

    /// <summary>Plattenbedarf nach Fläche plus 10 % Verschnitt.</summary>
    public static int Sheets(IReadOnlyList<(double W, double D)> parts, double sheetW, double sheetD)
    {
        var area = parts.Sum(p => p.W * p.D);
        return Math.Max(1, (int)Math.Ceiling(area * 1.1 / (sheetW * sheetD)));
    }
}
