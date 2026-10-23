using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Views;
using Android.Widget;

namespace TrackMaster.Mobile;

/// <summary>Farben und Bausteine der Oberfläche im Stil von Windows 11 (wie die Windows-Version).</summary>
public static class Ui
{
    public static readonly Color Accent = Color.Rgb(0x00, 0x67, 0xC0);
    public static readonly Color AccentLight = Color.Rgb(0xCC, 0xE4, 0xF7);
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
    public static readonly Color TipBack = Color.Rgb(0xE5, 0xF1, 0xFB);
    public static readonly Color Pro = Color.Rgb(0x88, 0x17, 0x98);

    public static readonly Color CanvasBack = Color.Rgb(0xF4, 0xF4, 0xF0);
    public static readonly Color Board = Color.Rgb(0xE3, 0xEB, 0xD7);
    public static readonly Color BoardStroke = Color.Rgb(0x8F, 0xA6, 0x7A);
    public static readonly Color GridMinor = Color.Argb(0x24, 0x5A, 0x6E, 0x46);
    public static readonly Color GridMajor = Color.Argb(0x48, 0x5A, 0x6E, 0x46);
    public static readonly Color Bed = Color.Rgb(0xCF, 0xC3, 0xAF);
    public static readonly Color Sleeper = Color.Rgb(0x8C, 0x7B, 0x66);
    public static readonly Color Rail = Color.Rgb(0x4E, 0x52, 0x57);
    public static readonly Color Selection = Color.Argb(0x5A, 0x00, 0x67, 0xC0);
    public static readonly Color Band = Color.Argb(0x22, 0x00, 0x67, 0xC0);

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
        t.SetTextSize(Android.Util.ComplexUnitType.Sp, (float)size);
        t.SetTextColor(color ?? Text);
        if (bold) t.SetTypeface(Typeface.Create("sans-serif-medium", TypefaceStyle.Normal), TypefaceStyle.Normal);
        return t;
    }

    /// <summary>Schaltfläche wie im Ribbon: Text, optional hervorgehoben oder aktiv (Werkzeug/Schalter).</summary>
    public static Button Button(Context c, string text, Action click, bool accent = false)
    {
        var b = new Button(c) { Text = text };
        b.SetAllCaps(false);
        b.SetTextSize(Android.Util.ComplexUnitType.Sp, 14);
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
            b.Background = Rounded(Accent, Color.Rgb(0x00, 0x5A, 0xA8));
            b.SetTextColor(Color.White);
        }
        else if (active)
        {
            b.Background = Rounded(AccentLight, Color.Rgb(0x99, 0xC9, 0xEF));
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
