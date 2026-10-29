using System.Globalization;
using System.Text;

namespace TrackMaster.Web.Graphics;

/// <summary>Farbe mit Deckkraft (wie Android.Graphics.Color), damit die Zeichenroutinen der Android-Version unverändert bleiben.</summary>
public readonly record struct Color(byte A, byte R, byte G, byte B)
{
    public static Color Rgb(int r, int g, int b) => new(0xFF, (byte)r, (byte)g, (byte)b);
    public static Color Argb(int a, int r, int g, int b) => new((byte)a, (byte)r, (byte)g, (byte)b);

    public static readonly Color White = Rgb(0xFF, 0xFF, 0xFF);
    public static readonly Color Black = Rgb(0, 0, 0);
    public static readonly Color DarkGray = Rgb(0x44, 0x44, 0x44);
    public static readonly Color Transparent = Argb(0, 0, 0, 0);

    /// <summary>ARGB als Zahl (für die Übergabe an JavaScript).</summary>
    public double Packed => ((uint)A << 24) | ((uint)R << 16) | ((uint)G << 8) | B;

    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    public string Css => A == 0xFF ? Hex : $"rgba({R},{G},{B},{(A / 255.0).ToString("0.###", CultureInfo.InvariantCulture)})";
}

public sealed class RectF(float left, float top, float right, float bottom)
{
    public float Left { get; set; } = left;
    public float Top { get; set; } = top;
    public float Right { get; set; } = right;
    public float Bottom { get; set; } = bottom;
    public float Width => Right - Left;
    public float Height => Bottom - Top;
    public bool Contains(float x, float y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
}

public sealed class DashPathEffect(float[] intervals, float phase)
{
    public float[] Intervals { get; } = intervals;
    public float Phase { get; } = phase;
}

[Flags]
public enum PaintFlags { None = 0, AntiAlias = 1 }

/// <summary>Stift bzw. Füllung wie Android.Graphics.Paint (nur die genutzten Eigenschaften).</summary>
public sealed class Paint : IDisposable
{
    public enum Style { Fill, Stroke, FillAndStroke }
    public enum Cap { Butt, Round, Square }
    public enum Join { Miter, Round, Bevel }
    public enum Align { Left, Center, Right }

    public Paint() { }
    public Paint(PaintFlags flags) => AntiAlias = flags.HasFlag(PaintFlags.AntiAlias);

    public bool AntiAlias { get; set; } = true;
    public Color Color { get; set; } = Color.Black;
    public float StrokeWidth { get; set; }
    public Cap StrokeCap { get; set; } = Cap.Butt;
    public Join StrokeJoin { get; set; } = Join.Miter;
    public float TextSize { get; set; } = 12;
    public Align TextAlign { get; set; } = Align.Left;
    public bool FakeBoldText { get; set; }
    public Style PaintStyle { get; private set; } = Style.Fill;
    public DashPathEffect? PathEffect { get; private set; }

    public void SetStyle(Style style) => PaintStyle = style;
    public void SetPathEffect(DashPathEffect? effect) => PathEffect = effect;
    public void SetTypeface(bool bold) => FakeBoldText = bold;

    public void Reset()
    {
        AntiAlias = true;
        Color = Color.Black;
        StrokeWidth = 0;
        StrokeCap = Cap.Butt;
        StrokeJoin = Join.Miter;
        TextSize = 12;
        TextAlign = Align.Left;
        FakeBoldText = false;
        PaintStyle = Style.Fill;
        PathEffect = null;
    }

    public void Dispose() { }
}

/// <summary>
/// Pfad wie Android.Graphics.Path. Jede Änderung vergibt eine neue Kennung, damit JavaScript unveränderte Pfade
/// zwischen zwei Bildern wiederverwenden kann.
/// </summary>
public sealed class Path : IDisposable
{
    public enum FillType { Winding, EvenOdd }

    private static int _next;
    internal readonly List<double> Ops = [];

    public int Id { get; private set; } = Interlocked.Increment(ref _next);
    public bool EvenOdd { get; private set; }
    public bool IsEmpty => Ops.Count == 0;

    private void Touch() => Id = Interlocked.Increment(ref _next);

    public void SetFillType(FillType type) { EvenOdd = type == FillType.EvenOdd; Touch(); }
    public void MoveTo(float x, float y) { Ops.Add(0); Ops.Add(x); Ops.Add(y); Touch(); }
    public void LineTo(float x, float y) { Ops.Add(1); Ops.Add(x); Ops.Add(y); Touch(); }
    public void Close() { Ops.Add(2); Touch(); }
    public void QuadTo(float cx, float cy, float x, float y) { Ops.Add(3); Ops.Add(cx); Ops.Add(cy); Ops.Add(x); Ops.Add(y); Touch(); }
    public void AddCircle(float cx, float cy, float r) { Ops.Add(4); Ops.Add(cx); Ops.Add(cy); Ops.Add(r); Touch(); }
    public void AddPath(Path other) { Ops.AddRange(other.Ops); Touch(); }

    public void Dispose() { }

    /// <summary>SVG-Pfaddaten.</summary>
    public string SvgData()
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        string N(double v) => Math.Round(v, 3).ToString(inv);
        for (var i = 0; i < Ops.Count;)
        {
            switch ((int)Ops[i])
            {
                case 0: sb.Append('M').Append(N(Ops[i + 1])).Append(' ').Append(N(Ops[i + 2])); i += 3; break;
                case 1: sb.Append('L').Append(N(Ops[i + 1])).Append(' ').Append(N(Ops[i + 2])); i += 3; break;
                case 2: sb.Append('Z'); i += 1; break;
                case 3: sb.Append('Q').Append(N(Ops[i + 1])).Append(' ').Append(N(Ops[i + 2])).Append(' ').Append(N(Ops[i + 3])).Append(' ').Append(N(Ops[i + 4])); i += 5; break;
                case 4:
                    double cx = Ops[i + 1], cy = Ops[i + 2], r = Ops[i + 3];
                    sb.Append($"M{N(cx + r)} {N(cy)}A{N(r)} {N(r)} 0 1 1 {N(cx - r)} {N(cy)}A{N(r)} {N(r)} 0 1 1 {N(cx + r)} {N(cy)}Z");
                    i += 4;
                    break;
                default: i = Ops.Count; break;
            }
        }
        return sb.ToString();
    }
}
