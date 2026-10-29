using System.Text;
using TrackMaster.Core;
using TrackMaster.Web.Graphics;
using TrackMaster.Web.Plan;

namespace TrackMaster.Web.Shell;

/// <summary>
/// Druck im Browser: Übersicht auf einer Seite (A4 quer) oder Bauvorlage 1:1 auf A4 hoch mit Klebekante, Passmarken alle 100 mm
/// und Prüfmaß – wie das PDF der Android-Version. Die Seiten entstehen als SVG; der Druckdialog des Browsers kann sie auch als PDF speichern.
/// </summary>
public static class PrintPages
{
    private const float Pt = 72f / 25.4f; // Punkte je mm
    public const double Overlap = 10;

    public static IReadOnlyList<PrintTile> Tiles(LayoutPlan plan) => PrintTiles.Layout(plan.Bounds(), 200, 273, Overlap);

    private static Paint Text(float size, bool bold = false) => new(PaintFlags.AntiAlias) { Color = Color.Black, TextSize = size, FakeBoldText = bold };

    /// <summary>Druckfertiges HTML: entweder die Übersicht oder alle Seiten der Bauvorlage.</summary>
    public static string Html(LayoutPlan plan, bool pro, bool fullSize)
    {
        // Linienbreiten der Bildschirmdarstellung hängen an der Pixeldichte; für den Druck feine Linien
        var density = Ui.Density;
        Ui.Density = 0.6f;
        try
        {
            var pages = new List<string>();
            if (fullSize)
            {
                var tiles = Tiles(plan);
                int rows = tiles.Max(t => t.Row) + 1, cols = tiles.Max(t => t.Column) + 1;
                pages.AddRange(tiles.Select(t => Tile(plan, t, rows, cols, pro)));
            }
            else pages.Add(Overview(plan, pro));
            var size = fullSize ? "A4 portrait" : "A4 landscape";
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>").Append(System.Net.WebUtility.HtmlEncode(plan.Name)).Append("</title><style>")
              .Append("@page{size:").Append(size).Append(";margin:0}html,body{margin:0;padding:0;background:#fff}")
              .Append(".page{page-break-after:always;break-after:page;overflow:hidden}.page:last-child{page-break-after:auto;break-after:auto}svg{display:block}")
              .Append("</style></head><body>");
            foreach (var p in pages) sb.Append("<div class=\"page\">").Append(p).Append("</div>");
            sb.Append("</body></html>");
            return sb.ToString();
        }
        finally
        {
            Ui.Density = density;
        }
    }

    private static string Overview(LayoutPlan plan, bool pro)
    {
        const int w = 842, h = 595;
        var c = new Canvas(w, h);
        c.DrawText(plan.Name, 30, 40, Text(16, true));
        var stats = plan.Statistics();
        c.DrawText(Lang.F($"Spur {plan.GaugeId} ({plan.Gauge.Scale}) · Platte {BoardGeometry.Describe(plan)} · {stats.Pieces} Gleise, {Fmt.Num(stats.TrackLengthMm / 1000, 2)} m · {DateTime.Now:dd.MM.yyyy}"), 30, 56, Text(9));
        var (x0, y0, x1, y1) = plan.Bounds();
        double bw = Math.Max(x1 - x0, 1), bh = Math.Max(y1 - y0, 1);
        var scale = (float)Math.Min((w - 60) / bw, (h - 130) / bh);
        c.Save();
        c.Translate(30 + (float)((w - 60 - bw * scale) / 2 - x0 * scale), (float)(70 - y0 * scale));
        c.Scale(scale, scale);
        new PlanPainter(plan, new HashSet<int>(), false).Draw(c, scale, true, new HashSet<int>(), null);
        c.Restore();
        var tiles = Tiles(plan);
        c.DrawText(Lang.F($"Maßstab etwa 1:{Fmt.Num(Pt / scale, 0)} · Bauvorlage 1:1: {tiles.Count} Seiten A4 (beim Drucken „Tatsächliche Größe“ bzw. 100 % wählen) · TrackMaster Pro {(pro ? Lang.T("Pro") : Lang.T("Basis"))}"),
                   30, h - 24, Text(8));
        var bom = string.Join(" · ", BillOfMaterials.Build(plan).Take(14).Select(r => $"{r.Count}× {r.Def.Number}"));
        if (bom.Length > 0) c.DrawText((Lang.T("Stückliste: ") + bom)[..Math.Min(bom.Length + 12, 190)], 30, h - 38, Text(8));
        return SvgWriter.Page(c, 297, 210);
    }

    private static string Tile(LayoutPlan plan, PrintTile t, int rows, int cols, bool pro)
    {
        const int w = 595, h = 842;
        var c = new Canvas(w, h);
        float tw = (float)(t.X1 - t.X0) * Pt, th = (float)(t.Y1 - t.Y0) * Pt;
        float left = (w - tw) / 2, top = (h - th) / 2;
        c.Save();
        c.ClipRect(left, top, left + tw, top + th);
        c.Translate(left - (float)t.X0 * Pt, top - (float)t.Y0 * Pt);
        c.Scale(Pt, Pt);
        new PlanPainter(plan, new HashSet<int>(), false).Draw(c, Pt, true, new HashSet<int>(), null);
        var mark = new Paint(PaintFlags.AntiAlias) { Color = Color.Black, StrokeWidth = 0.25f };
        mark.SetStyle(Paint.Style.Stroke);
        foreach (var (x, y) in PrintTiles.Marks(t))
        {
            float fx = (float)x, fy = (float)y;
            c.DrawLine(fx - 4, fy, fx + 4, fy, mark);
            c.DrawLine(fx, fy - 4, fx, fy + 4, mark);
            c.DrawCircle(fx, fy, 1.5f, mark);
        }
        c.Restore();
        var glue = new Paint { Color = Color.Argb(40, 0, 0, 0) };
        var o = (float)Overlap * Pt;
        if (t.Column < cols - 1) c.DrawRect(left + tw - o, top, left + tw, top + th, glue);
        if (t.Row < rows - 1) c.DrawRect(left, top + th - o, left + tw, top + th, glue);
        var frame = new Paint { Color = Color.Black, StrokeWidth = 0.6f };
        frame.SetStyle(Paint.Style.Stroke);
        frame.SetPathEffect(new DashPathEffect([4, 3], 0));
        c.DrawRect(left, top, left + tw, top + th, frame);

        string? N(int r, int col) => r >= 0 && r < rows && col >= 0 && col < cols ? $"{PrintTiles.RowName(r)}{col + 1}" : null;
        var near = new[] { (Lang.T("oben"), N(t.Row - 1, t.Column)), (Lang.T("unten"), N(t.Row + 1, t.Column)), (Lang.T("links"), N(t.Row, t.Column - 1)), (Lang.T("rechts"), N(t.Row, t.Column + 1)) }
            .Where(x => x.Item2 is not null).Select(x => $"{x.Item1} {x.Item2}");
        c.DrawText(Lang.F($"Seite {t.Label}"), left, top - 8, Text(11, true));
        c.DrawText(Lang.F($"{plan.Name} · Bauvorlage 1:1 · {rows} × {cols} Seiten · {string.Join(", ", near)}"), left + 60, top - 8, Text(7));
        var line = new Paint { Color = Color.Black, StrokeWidth = 0.8f };
        line.SetStyle(Paint.Style.Stroke);
        var y0 = top + th + 14;
        c.DrawLine(left, y0, left + 100 * Pt, y0, line);
        for (var i = 0; i <= 10; i++) c.DrawLine(left + i * 10 * Pt, y0 - (i % 5 == 0 ? 4 : 2), left + i * 10 * Pt, y0, line);
        c.DrawText(Lang.F($"Prüfmaß 100 mm – bitte nachmessen · TrackMaster Pro {(pro ? Lang.T("Pro") : Lang.T("Basis"))}"), left + 100 * Pt + 8, y0 + 3, Text(7));
        return SvgWriter.Page(c, 210, 297);
    }
}
