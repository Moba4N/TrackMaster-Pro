using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Views;
using Android.Widget;
using TrackMaster.Core;

namespace TrackMaster.Mobile;

/// <summary>Farben und Bausteine der Oberfläche im Stil von Windows 11 (wie die Windows-Version); Akzent und Zeichenfläche aus dem Design.</summary>
public static class Ui
{
    /// <summary>Gewähltes Design (Farben von Akzent und Zeichenfläche); MainActivity setzt es vor dem Aufbau der Oberfläche.</summary>
    public static Design D { get; set; } = Design.Logo;
    /// <summary>Schrift der Oberfläche (Android-Familienname) und Faktor für die Schriftgrößen.</summary>
    public static string FontFamily { get; set; } = "sans-serif";
    public static double FontScale { get; set; } = 1;

    private static Color C(Rgb c) => Color.Rgb(c.R, c.G, c.B);
    private static Color A(int alpha, Rgb c) => Color.Argb(alpha, c.R, c.G, c.B);

    public static Color Accent => C(D.Accent);
    public static Color AccentDark => C(D.AccentDark);
    public static Color AccentLight => C(D.AccentLight);
    public static Color AccentBorder => C(D.AccentBorder);
    public static readonly Color Text = Color.Rgb(0x1B, 0x1B, 0x1B);
    public static readonly Color TextSecondary = Color.Rgb(0x55, 0x55, 0x55);
    public static readonly Color Window = Color.Rgb(0xF3, 0xF3, 0xF3);
    public static readonly Color Panel = Color.White;
    public static readonly Color Border = Color.Rgb(0xE1, 0xE1, 0xE1);
    public static readonly Color Success = Color.Rgb(0x0F, 0x7B, 0x0F);
    public static readonly Color Danger = Color.Rgb(0xC4, 0x2B, 0x1C);
    public static readonly Color OpenEnd = Color.Rgb(0xD4, 0x6B, 0x00);
    public static readonly Color Warning = Color.Rgb(0x6B, 0x4E, 0x00);
    public static readonly Color WarningBack = Color.Rgb(0xFF, 0xF4, 0xCE);
    public static Color TipBack => C(D.TipBack);
    public static readonly Color Pro = Color.Rgb(0x88, 0x17, 0x98);

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

    /// <summary>Schrift der Oberfläche in normaler oder fetter Ausführung.</summary>
    public static Typeface Font(bool bold = false) => Typeface.Create(FontFamily, bold ? TypefaceStyle.Bold : TypefaceStyle.Normal)!;

    public static float Density { get; set; } = 1;

    public static int Dp(double v) => (int)Math.Round(v * Density);

    public static GradientDrawable Rounded(Color fill, Color? stroke = null, double radius = 6, double strokeWidth = 1)
    {
        var d = new GradientDrawable();
        d.SetColor(fill);
        d.SetCornerRadius(Dp(radius));
        if (stroke is { } s) d.SetStroke(Math.Max(1, Dp(strokeWidth)), s);
        return d;
    }

    public static TextView Label(Context c, string text, double size = 14, bool bold = false, Color? color = null)
    {
        var t = new TextView(c) { Text = text };
        t.SetTextSize(Android.Util.ComplexUnitType.Sp, (float)(size * FontScale));
        t.SetTextColor(color ?? Text);
        t.SetTypeface(bold && FontFamily == "sans-serif" ? Typeface.Create("sans-serif-medium", TypefaceStyle.Normal) : Font(bold), TypefaceStyle.Normal);
        return t;
    }

    /// <summary>Schaltfläche wie im Ribbon: Text, optional hervorgehoben oder aktiv (Werkzeug/Schalter).</summary>
    public static Button Button(Context c, string text, Action click, bool accent = false)
    {
        var b = new Button(c) { Text = text };
        b.SetAllCaps(false);
        b.SetTextSize(Android.Util.ComplexUnitType.Sp, (float)(14 * FontScale));
        b.SetTypeface(Font(), TypefaceStyle.Normal);
        b.SetMinHeight(0);
        b.SetMinimumHeight(Dp(40));
        b.SetMinWidth(0);
        b.SetMinimumWidth(Dp(44));
        b.SetPadding(Dp(12), Dp(6), Dp(12), Dp(6));
        b.StateListAnimator = null;
        SetButtonState(b, accent, false);
        b.Click += (_, _) => click();
        return b;
    }

    public static void SetButtonState(Button b, bool accent, bool active)
    {
        if (accent)
        {
            b.Background = Rounded(Accent, AccentDark);
            b.SetTextColor(Color.White);
        }
        else if (active)
        {
            b.Background = Rounded(AccentLight, AccentBorder);
            b.SetTextColor(Text);
        }
        else
        {
            b.Background = Rounded(Color.Rgb(0xFD, 0xFD, 0xFD), Color.Rgb(0xD1, 0xD1, 0xD1));
            b.SetTextColor(b.Enabled ? Text : Color.Rgb(0xA0, 0xA0, 0xA0));
        }
    }

    public static LinearLayout Row(Context c, int spacing = 8)
    {
        var l = new LinearLayout(c) { Orientation = Orientation.Horizontal };
        l.SetGravity(GravityFlags.CenterVertical);
        if (spacing > 0) l.ShowDividers = ShowDividers.Middle;
        if (spacing > 0) l.SetDividerDrawable(new SpaceDivider(Dp(spacing)));
        return l;
    }

    public static LinearLayout Column(Context c, int spacing = 8)
    {
        var l = new LinearLayout(c) { Orientation = Orientation.Vertical };
        if (spacing > 0) l.ShowDividers = ShowDividers.Middle;
        if (spacing > 0) l.SetDividerDrawable(new SpaceDivider(Dp(spacing)));
        return l;
    }

    public static LinearLayout.LayoutParams Weight(float weight = 1, bool vertical = true)
        => vertical ? new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, weight) : new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, weight);

    public static LinearLayout.LayoutParams Wrap() => new(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);

    public static LinearLayout.LayoutParams Fill() => new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);

    /// <summary>Zahlenfeld mit deutscher Schreibweise (Komma) und Minuszeichen.</summary>
    public static EditText Number(Context c, double value, int decimals = 0, bool signed = false)
    {
        var e = new EditText(c) { Text = Core.Fmt.Num(value, decimals).Replace(".", "") };
        e.InputType = InputTypes.ClassNumber | InputTypes.NumberFlagDecimal | (signed ? InputTypes.NumberFlagSigned : 0);
        e.KeyListener = Android.Text.Method.DigitsKeyListener.GetInstance("0123456789,.-");
        e.SetSingleLine(true);
        return e;
    }

    public static EditText TextField(Context c, string value, string? hint = null)
    {
        var e = new EditText(c) { Text = value, Hint = hint };
        e.SetSingleLine(true);
        return e;
    }

    /// <summary>Liest eine Zahl mit Komma oder Punkt; leer oder ungültig = <paramref name="fallback"/>.</summary>
    public static double Parse(EditText e, double fallback) => Core.Fmt.Parse(e.Text) ?? fallback;

    /// <summary>Beschriftetes Feld für Formulare in Dialogen.</summary>
    public static View Field(Context c, string label, View input)
    {
        var col = Column(c, 2);
        col.AddView(Label(c, label, 12.5, color: TextSecondary));
        col.AddView(input, Fill());
        return col;
    }

    public static ScrollView Scroll(Context c, View content, int padding = 20)
    {
        var s = new ScrollView(c);
        content.SetPadding(Dp(padding), Dp(padding * 0.6), Dp(padding), Dp(padding * 0.3));
        s.AddView(content);
        return s;
    }
}

/// <summary>Unsichtbarer Abstand zwischen Kindelementen eines LinearLayout.</summary>
public sealed class SpaceDivider(int size) : Drawable
{
    public override int IntrinsicWidth => size;
    public override int IntrinsicHeight => size;
    public override void Draw(Canvas canvas) { }
    public override void SetAlpha(int alpha) { }
    public override void SetColorFilter(ColorFilter? colorFilter) { }
    public override int Opacity => (int)Format.Transparent;
}
