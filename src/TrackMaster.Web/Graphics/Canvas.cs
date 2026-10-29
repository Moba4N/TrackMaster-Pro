namespace TrackMaster.Web.Graphics;

/// <summary>Befehle des Zeichenprotokolls; JavaScript (trackmaster.js) und <see cref="SvgWriter"/> lesen sie.</summary>
public enum Op
{
    Save = 1, Restore, Translate, Rotate, Scale, Concat, ClipPath, DefinePath, Path, Line, Circle, Rect, RoundRect, Text, Tag, FillAll, ClipRect,
}

/// <summary>
/// Zeichenfläche mit der Schnittstelle von Android.Graphics.Canvas. Sie zeichnet nicht selbst, sondern schreibt ein
/// kompaktes Protokoll aus Zahlen (und Texten), das im Browser auf ein &lt;canvas&gt; übertragen oder als SVG ausgegeben wird.
/// </summary>
public sealed class Canvas
{
    public List<double> Data { get; } = [];
    public List<string> Strings { get; } = [];

    /// <summary>Pfade, die JavaScript aus dem letzten Bild noch kennt.</summary>
    private readonly HashSet<int> _known;
    private readonly HashSet<int> _used = [];

    public float Width { get; }
    public float Height { get; }

    public Canvas(float width, float height, HashSet<int>? known = null)
    {
        Width = width;
        Height = height;
        _known = known ?? [];
    }

    /// <summary>Kennungen der in diesem Bild benutzten Pfade (für das nächste Bild).</summary>
    public HashSet<int> UsedPaths => _used;

    private void Add(Op op, params double[] values)
    {
        Data.Add((int)op);
        Data.AddRange(values);
    }

    private int Text(string s)
    {
        Strings.Add(s);
        return Strings.Count - 1;
    }

    public void Save() => Add(Op.Save);
    public void Restore() => Add(Op.Restore);
    public void Translate(float x, float y) => Add(Op.Translate, x, y);
    public void Rotate(float degrees) => Add(Op.Rotate, degrees);

    public void Rotate(float degrees, float px, float py)
    {
        Translate(px, py);
        Rotate(degrees);
        Translate(-px, -py);
    }

    public void Scale(float sx, float sy) => Add(Op.Scale, sx, sy);

    /// <summary>Matrix wie im Canvas des Browsers: x' = a·x + c·y + e, y' = b·x + d·y + f.</summary>
    public void Concat(double a, double b, double c, double d, double e, double f) => Add(Op.Concat, a, b, c, d, e, f);

    private int Define(Path path)
    {
        if (!_known.Contains(path.Id) && !_used.Contains(path.Id))
        {
            Data.Add((int)Op.DefinePath);
            Data.Add(path.Id);
            Data.Add(path.EvenOdd ? 1 : 0);
            Data.Add(path.Ops.Count);
            Data.AddRange(path.Ops);
        }
        _used.Add(path.Id);
        return path.Id;
    }

    public void ClipPath(Path path) => Add(Op.ClipPath, Define(path));
    public void ClipRect(float l, float t, float r, float b) => Add(Op.ClipRect, l, t, r, b);

    private static int StyleOf(Paint p) => p.PaintStyle switch { Paint.Style.Fill => 0, Paint.Style.Stroke => 1, _ => 2 };

    public void DrawPath(Path path, Paint p)
    {
        if (path.IsEmpty) return;
        var id = Define(path);
        Data.Add((int)Op.Path);
        Data.Add(id);
        Data.Add(StyleOf(p));
        Data.Add(p.Color.Packed);
        Data.Add(p.StrokeWidth);
        Data.Add((int)p.StrokeCap);
        Data.Add((int)p.StrokeJoin);
        var dash = p.PathEffect?.Intervals ?? [];
        Data.Add(dash.Length);
        foreach (var d in dash) Data.Add(d);
        Data.Add(p.PathEffect?.Phase ?? 0);
    }

    public void DrawLine(float x1, float y1, float x2, float y2, Paint p)
        => Add(Op.Line, x1, y1, x2, y2, p.Color.Packed, p.StrokeWidth, (int)p.StrokeCap);

    public void DrawCircle(float cx, float cy, float r, Paint p)
        => Add(Op.Circle, cx, cy, r, StyleOf(p), p.Color.Packed, p.StrokeWidth);

    public void DrawRect(float l, float t, float r, float b, Paint p)
        => Add(Op.Rect, l, t, r, b, StyleOf(p), p.Color.Packed, p.StrokeWidth, (int)p.StrokeJoin);

    public void DrawRect(RectF rect, Paint p) => DrawRect(rect.Left, rect.Top, rect.Right, rect.Bottom, p);

    public void DrawRoundRect(RectF rect, float rx, float ry, Paint p)
        => Add(Op.RoundRect, rect.Left, rect.Top, rect.Right, rect.Bottom, rx, ry, StyleOf(p), p.Color.Packed, p.StrokeWidth);

    public void DrawText(string s, float x, float y, Paint p)
        => Add(Op.Text, Text(s), x, y, p.TextSize, p.Color.Packed, (int)p.TextAlign, p.FakeBoldText ? 1 : 0);

    /// <summary>Beschriftung mit hellem, abgerundetem Hintergrund, mittig um (cx, cy); die Breite misst der Browser.</summary>
    public void DrawTag(string s, float cx, float cy, float size, Color color, Color back, float pad)
        => Add(Op.Tag, Text(s), cx, cy, size, color.Packed, back.Packed, pad);

    public void DrawColor(Color c) => Add(Op.FillAll, c.Packed);
}
