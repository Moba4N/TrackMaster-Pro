using TrackMaster.Core;
using TrackMaster.Web.Graphics;
using TrackMaster.Web.Plan;

namespace TrackMaster.Web.Shell;

/// <summary>Kleine Bilder als SVG: Signalbilder, Design-Vorschau, Form der Anlagenplatte.</summary>
public static class Pictures
{
    public static string Signal(SignalType type, float width = 36, float height = 64)
    {
        var c = new Canvas(width, height);
        PlanPainter.SignalIcon(c, type, width, height);
        return SvgWriter.Image(c);
    }

    private static Color C(Rgb v) => Color.Rgb(v.R, v.G, v.B);

    public static string DesignPreview(Design d, float w = 96, float h = 48)
    {
        var c = new Canvas(w, h);
        using var p = new Paint { AntiAlias = true };
        p.Color = C(d.Canvas);
        c.DrawRect(0, 0, w, h, p);
        p.Color = C(d.Accent);
        c.DrawRect(0, 0, w, h * 0.12f, p);
        p.Color = C(d.Board);
        c.DrawRect(w * 0.08f, h * 0.24f, w * 0.92f, h * 0.9f, p);
        p.SetStyle(Paint.Style.Stroke);
        p.Color = C(d.BoardStroke);
        p.StrokeWidth = 1;
        c.DrawRect(w * 0.08f, h * 0.24f, w * 0.92f, h * 0.9f, p);
        var path = new Graphics.Path();
        path.MoveTo(w * 0.2f, h * 0.84f);
        path.QuadTo(w * 0.3f, h * 0.42f, w * 0.62f, h * 0.48f);
        path.LineTo(w * 0.86f, h * 0.48f);
        p.StrokeCap = Paint.Cap.Butt;
        p.StrokeJoin = Paint.Join.Round;
        foreach (var (color, width) in new[] { (d.Bed, h * 0.12f), (d.Sleeper, h * 0.08f), (d.Rail, Math.Max(1.2f, h * 0.025f)) })
        {
            p.Color = C(color);
            p.StrokeWidth = width;
            c.DrawPath(path, p);
        }
        return SvgWriter.Image(c);
    }

    public static string Board(IReadOnlyList<IReadOnlyList<(double X, double Y)>> outlines, float width = 320, float height = 150)
    {
        var c = new Canvas(width, height);
        if (outlines.Count == 0) return SvgWriter.Image(c);
        var pts = outlines.SelectMany(o => o).ToList();
        double maxX = Math.Max(1, pts.Max(p => p.X)), maxY = Math.Max(1, pts.Max(p => p.Y));
        const float pad = 8;
        var scale = (float)Math.Min((width - 2 * pad) / maxX, (height - 2 * pad) / maxY);
        float ox = (float)((width - maxX * scale) / 2), oy = (float)((height - maxY * scale) / 2);
        var path = new Graphics.Path();
        path.SetFillType(Graphics.Path.FillType.EvenOdd);
        foreach (var o in outlines)
        {
            path.MoveTo(ox + (float)o[0].X * scale, oy + (float)o[0].Y * scale);
            foreach (var q in o.Skip(1)) path.LineTo(ox + (float)q.X * scale, oy + (float)q.Y * scale);
            path.Close();
        }
        using var p = new Paint { AntiAlias = true };
        p.Color = Ui.Board;
        c.DrawPath(path, p);
        p.SetStyle(Paint.Style.Stroke);
        p.Color = Ui.BoardStroke;
        p.StrokeWidth = 1.5f;
        p.StrokeJoin = Paint.Join.Round;
        c.DrawPath(path, p);
        return SvgWriter.Image(c);
    }
}
