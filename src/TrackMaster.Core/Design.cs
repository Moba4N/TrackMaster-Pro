namespace TrackMaster.Core;

/// <summary>
/// Farbschema der Oberfläche und der Zeichenfläche. Windows und Android lesen dieselben Werte;
/// die Bausteine (Theme bzw. Ui) bauen daraus ihre Pinsel und Farben.
/// </summary>
public sealed record Design(
    string Id, string Name, string Description,
    Rgb Accent, Rgb AccentDark, Rgb AccentLight, Rgb AccentBorder, Rgb TipBack,
    Rgb Canvas, Rgb CanvasText, Rgb Board, Rgb BoardStroke, Rgb Grid,
    Rgb Bed, Rgb Sleeper, Rgb Rail)
{
    /// <summary>Grün aus dem Logo (Hervorhebungen, Handbuch).</summary>
    public static readonly Rgb LogoGreen = new(0x76, 0xAD, 0x33);
    /// <summary>Anthrazit aus dem Logo.</summary>
    public static readonly Rgb LogoDark = new(0x1F, 0x24, 0x28);

    public static readonly Design TrackPlan = new("trackplan", "Logo-Grün", "Standard: Grün und Anthrazit aus dem Logo, heller Arbeitsbereich.",
        new(0x43, 0x7E, 0x1E), new(0x36, 0x66, 0x18), new(0xDC, 0xEB, 0xC9), new(0xB3, 0xD2, 0x92), new(0xEE, 0xF5, 0xE4),
        new(0xF4, 0xF5, 0xF2), new(0x55, 0x5A, 0x55), new(0xE3, 0xEB, 0xD7), new(0x8F, 0xA6, 0x7A), new(0x5A, 0x6E, 0x46),
        new(0xCF, 0xC3, 0xAF), new(0x8C, 0x7B, 0x66), new(0x4E, 0x52, 0x57));

    public static readonly Design Classic = new("klassisch", "Klassisch Blau", "Blaue Akzente im Stil von Windows, heller Arbeitsbereich.",
        new(0x00, 0x67, 0xC0), new(0x00, 0x5A, 0xA8), new(0xCC, 0xE4, 0xF7), new(0x99, 0xC9, 0xEF), new(0xE5, 0xF1, 0xFB),
        new(0xF4, 0xF4, 0xF0), new(0x55, 0x55, 0x55), new(0xE3, 0xEB, 0xD7), new(0x8F, 0xA6, 0x7A), new(0x5A, 0x6E, 0x46),
        new(0xCF, 0xC3, 0xAF), new(0x8C, 0x7B, 0x66), new(0x4E, 0x52, 0x57));

    public static readonly Design Blueprint = new("blaupause", "Blaupause", "Der Plan als technische Zeichnung: weiße Gleise auf blauem Grund.",
        new(0x1F, 0x5F, 0xA8), new(0x17, 0x4C, 0x88), new(0xD3, 0xE3, 0xF5), new(0x9F, 0xC0, 0xE6), new(0xE6, 0xEF, 0xF9),
        new(0x16, 0x32, 0x52), new(0xC8, 0xD8, 0xEC), new(0x1F, 0x45, 0x70), new(0xA8, 0xC4, 0xE6), new(0xC8, 0xDC, 0xF4),
        new(0x3A, 0x64, 0x94), new(0x8E, 0xAE, 0xD4), new(0xF4, 0xF8, 0xFF));

    public static readonly Design Contrast = new("kontrast", "Hoher Kontrast", "Schwarze Gleise auf weißer Platte, gut lesbar und sparsam beim Drucken.",
        new(0x0B, 0x4F, 0x8A), new(0x08, 0x3D, 0x6B), new(0xD5, 0xE4, 0xF1), new(0x8C, 0xB2, 0xD4), new(0xEB, 0xF1, 0xF7),
        new(0xFF, 0xFF, 0xFF), new(0x22, 0x22, 0x22), new(0xFF, 0xFF, 0xFF), new(0x22, 0x22, 0x22), new(0x60, 0x60, 0x60),
        new(0xE4, 0xE4, 0xE4), new(0x70, 0x70, 0x70), new(0x00, 0x00, 0x00));

    public static readonly Design Workshop = new("werkstatt", "Werkstatt", "Warme Holztöne: Platte wie Sperrholz, Akzente in Kupfer.",
        new(0x9A, 0x55, 0x1C), new(0x7D, 0x44, 0x15), new(0xF2, 0xDF, 0xC9), new(0xDF, 0xBC, 0x95), new(0xF8, 0xEE, 0xE2),
        new(0xEF, 0xE9, 0xDF), new(0x5E, 0x50, 0x42), new(0xE6, 0xCC, 0xA1), new(0xA9, 0x83, 0x4F), new(0x7A, 0x5A, 0x34),
        new(0xCD, 0xBF, 0xA6), new(0x6E, 0x52, 0x38), new(0x3A, 0x3A, 0x3A));

    public static IReadOnlyList<Design> All { get; } = [TrackPlan, Classic, Blueprint, Contrast, Workshop];

    public static Design Find(string? id) => All.FirstOrDefault(d => d.Id == id) ?? TrackPlan;
}

/// <summary>Schriftart und -größe der Oberfläche (Name der Schrift je Plattform).</summary>
public sealed record FontChoice(string Id, string Name, string Windows, string Android);

public static class UiFonts
{
    public static IReadOnlyList<FontChoice> All { get; } =
    [
        new("standard", "Standard (Segoe UI / Roboto)", "Segoe UI Variable Text, Segoe UI", "sans-serif"),
        new("bahnschrift", "Bahnschrift (schmal, technisch)", "Bahnschrift", "sans-serif-condensed"),
        new("calibri", "Calibri (rund, weich)", "Calibri", "sans-serif-light"),
        new("arial", "Arial (klassisch)", "Arial", "sans-serif"),
        new("verdana", "Verdana (breit, gut lesbar)", "Verdana", "sans-serif-medium"),
        new("georgia", "Georgia (mit Serifen)", "Georgia", "serif"),
        new("consolas", "Consolas (Schreibmaschine)", "Consolas", "monospace"),
    ];

    /// <summary>Schriftgrößen als Faktor auf die normale Größe.</summary>
    public static IReadOnlyList<(string Name, double Scale)> Sizes { get; } = [("Klein", 0.92), ("Normal", 1.0), ("Groß", 1.12), ("Sehr groß", 1.25)];

    public static FontChoice Find(string? id) => All.FirstOrDefault(f => f.Id == id) ?? All[0];
}

/// <summary>Gewählte Darstellung: Design, Schrift und Schriftgröße.</summary>
public sealed record AppearanceSettings(string Design = "trackplan", string Font = "standard", double FontScale = 1.0)
{
    public Design Palette => Core.Design.Find(Design);
    public FontChoice FontChoice => UiFonts.Find(Font);
    public double Scale => Math.Clamp(FontScale, 0.8, 1.4);
}
