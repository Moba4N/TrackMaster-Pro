using TrackMaster.Core;
using TrackMaster.Web.Graphics;

namespace TrackMaster.Web.Plan;

/// <summary>Farben der Zeichenfläche aus dem gewählten Design (wie Ui in der Android-Version).</summary>
public static class Ui
{
    public static Design D { get; set; } = Design.Logo;
    public static double FontScale { get; set; } = 1;

    /// <summary>Linienbreiten in CSS-Pixeln; die Bildschirmauflösung gleicht das Zeichenprotokoll in JavaScript aus.</summary>
    public static float Density { get; set; } = 1;

    private static Color C(Rgb c) => Color.Rgb(c.R, c.G, c.B);
    private static Color A(int alpha, Rgb c) => Color.Argb(alpha, c.R, c.G, c.B);

    public static Color Accent => C(D.Accent);
    public static readonly Color Text = Color.Rgb(0x1B, 0x1B, 0x1B);
    public static readonly Color TextSecondary = Color.Rgb(0x55, 0x55, 0x55);
    public static readonly Color Panel = Color.White;
    public static readonly Color Success = Color.Rgb(0x0F, 0x7B, 0x0F);
    public static readonly Color Danger = Color.Rgb(0xC4, 0x2B, 0x1C);
    public static readonly Color OpenEnd = Color.Rgb(0xD4, 0x6B, 0x00);

    public static Color CanvasBack => C(D.Canvas);
    public static Color CanvasText => C(D.CanvasText);
    public static Color Board => C(D.Board);
    public static Color BoardStroke => C(D.BoardStroke);
    public static Color GridMinor => A(0x24, D.Grid);
    public static Color GridMajor => A(0x48, D.Grid);
    public static Color Bed => C(D.Bed);
    public static Color Sleeper => C(D.Sleeper);
    public static Color Rail => C(D.Rail);
    public static Color Selection => A(0x5A, D.Accent);
    public static Color Band => A(0x22, D.Accent);

    public static float Dp(double v) => (float)(v * Density);
}
