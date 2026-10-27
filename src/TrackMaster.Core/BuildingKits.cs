namespace TrackMaster.Core;

/// <summary>
/// Gebäudevorlage nach einem handelsüblichen Bausatz: Grundmaß (Länge × Breite) und Höhe in mm, wie vom Hersteller angegeben.
/// </summary>
public sealed record BuildingKit(string Maker, string Number, string Name, string Category, double Length, double Width, double Height, string Scale)
{
    /// <summary>Für eine andere Baugröße umgerechnet (kein Bausatz in dieser Baugröße): Maße mit dem Maßstabsverhältnis.</summary>
    public bool Converted { get; init; }

    public string Label => Neutral ? Name : Converted ? $"{Name} (nach {Maker} {Number}, {Scale} umgerechnet)" : $"{Name} ({Maker} {Number})";

    /// <summary>Kurzbeschreibung für die Auswahlliste.</summary>
    public string Details => $"{Fmt.Num(Length, 0)} × {Fmt.Num(Width, 0)} mm, Höhe {Fmt.Num(Height, 0)} mm";

    /// <summary>Ohne Hersteller und Artikelnummer anzeigen (Bildschirmfotos für das Handbuch).</summary>
    public static bool Neutral { get; set; }
}

/// <summary>
/// Gebäudevorlagen nach Faller-Bausätzen (H0, N, Z und Pola G). Für Spuren ohne eigene Bausätze (TT, 0, Spur 1)
/// werden die Vorlagen der nächstliegenden Baugröße maßstäblich umgerechnet.
/// Maße: Herstellerangaben Länge × Breite × Höhe in mm (Pola G: aus Zoll-Angaben gerundet).
/// </summary>
public static class BuildingKits
{
    private const string Faller = "Faller";
    private const string Station = "Bahnhöfe";
    private const string Railway = "Bahnbauten";
    private const string Houses = "Wohnhäuser";
    private const string Trade = "Gewerbe und Handwerk";
    private const string Public = "Kirchen und öffentliche Gebäude";

    private static BuildingKit K(string scale, string number, string name, string category, double l, double w, double h) => new(Faller, number, name, category, l, w, h, scale);

    public static IReadOnlyList<BuildingKit> All { get; } =
    [
        // H0 1:87
        K("H0", "110113", "Bahnhof Bonn", Station, 700, 160, 130),
        K("H0", "110115", "Bahnhof Mittelstadt", Station, 446, 160, 130),
        K("H0", "110099", "Bahnhof Waldbrunn", Station, 240, 125, 135),
        K("H0", "110097", "Kleinstation Blumenfeld", Station, 250, 140, 120),
        K("H0", "110095", "Haltepunkt Sonnefeld", Station, 199, 191, 98),
        K("H0", "110091", "Haltepunkt Hintertupfingen", Station, 121, 78, 49),
        K("H0", "120120", "Stellwerk Mittelstadt", Railway, 83, 62, 101),
        K("H0", "120154", "Güterbahnhof", Railway, 209, 82, 90),
        K("H0", "120095", "Güterschuppen", Railway, 205, 162, 102),
        K("H0", "120281", "Ringlokschuppen, 12° Gleiswinkel", Railway, 377, 494, 129),
        K("H0", "130222", "Einfamilienhaus mit Fachwerk", Houses, 93, 84, 115),
        K("H0", "130223", "Einfamilienhaus", Houses, 93, 84, 115),
        K("H0", "130489", "Metzgerei/Bäckerei", Trade, 190, 125, 125),
        K("H0", "130288", "Holzlager", Trade, 130, 86, 56),
        // N 1:160
        K("N", "212104", "Bahnhof Reichenbach", Station, 163, 71, 72),
        K("N", "212106", "Bahnhof Hochdorf", Station, 250, 120, 88),
        K("N", "212107", "Bahnhof Güglingen", Station, 165, 78, 78),
        K("N", "222109", "Stellwerk Zweckel", Railway, 67, 32, 42),
        K("N", "222118", "Ringlokschuppen, 2-ständig", Railway, 206, 199, 96),
        K("N", "222187", "Baracke", Railway, 62, 40, 30),
        K("N", "232237", "Haus Enzian", Houses, 65, 64, 48),
        K("N", "232245", "Apotheke und Bäckerei (je Haus)", Trade, 80, 70, 60),
        K("N", "222181", "Sägewerk", Trade, 170, 100, 150),
        K("N", "232263", "Kirche Sils-Maria", Public, 79, 62, 64),
        // Z 1:220
        K("Z", "282707", "Bahnhof Güglingen", Station, 119, 56, 60),
        K("Z", "282726", "Bahnhofshalle", Station, 167, 94, 52),
        K("Z", "282733", "Lokwerkstatt", Railway, 118, 83, 123),
        K("Z", "282760", "Fachwerkhaus", Houses, 58, 40, 39),
        K("Z", "282764", "Siedlungshaus", Houses, 48, 41, 38),
        // Pola G 1:22,5
        K("G", "330901", "Bahnhof Schönweiler", Station, 457, 391, 470),
        K("G", "330896", "Bahnhof Altenholz", Station, 521, 366, 279),
        K("G", "330895", "Haltepunkt Hammerstein", Station, 323, 140, 130),
        K("G", "330882", "Blockstelle", Railway, 231, 236, 366),
        K("G", "330877", "Bahnwärterhaus", Railway, 363, 333, 269),
        K("G", "330880", "Lagerschuppen mit Büro", Railway, 371, 259, 246),
        K("G", "331092", "Einfamilienhaus", Houses, 343, 295, 402),
        K("G", "331053", "Berghütte", Houses, 389, 384, 244),
        K("G", "331115", "Wassermühle mit Fachwerk", Trade, 575, 590, 500),
        K("G", "331075", "Kirche", Public, 328, 224, 569),
    ];

    /// <summary>Maßstabszahlen der Baugrößen mit eigenen Bausätzen.</summary>
    private static readonly (string Scale, double Divisor)[] Sources = [("H0", 87), ("N", 160), ("Z", 220), ("G", 22.5)];

    public static IReadOnlyList<string> Categories { get; } = [Station, Railway, Houses, Trade, Public];

    /// <summary>
    /// Vorlagen für eine Spur: die Bausätze der passenden Baugröße, sonst die der nächstliegenden Baugröße umgerechnet
    /// (TT aus N, 0 aus H0, Spur 1 aus G). H0m und H0e verwenden die H0-Bausätze (gleicher Maßstab).
    /// </summary>
    public static IReadOnlyList<BuildingKit> For(Gauge gauge)
    {
        var divisor = RidePath.ScaleDivisor(gauge.Scale);
        var (scale, source) = Sources.MinBy(s => Math.Abs(Math.Log(s.Divisor / divisor)));
        var kits = All.Where(k => k.Scale == scale);
        if (Math.Abs(source - divisor) < 0.01) return kits.ToList();
        var f = source / divisor;
        return kits.Select(k => k with
        {
            Length = Math.Round(k.Length * f), Width = Math.Round(k.Width * f), Height = Math.Round(k.Height * f), Converted = true,
        }).ToList();
    }
}
