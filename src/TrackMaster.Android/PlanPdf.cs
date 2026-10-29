using Android.Graphics;
using Android.Graphics.Pdf;
using TrackMaster.Core;

namespace TrackMaster.Mobile;

/// <summary>
/// Plan als PDF zum Drucken oder Weitergeben: Seite 1 Übersicht (A4 quer), danach die Bauvorlage 1:1 auf A4 hoch
/// mit Klebekante, Passmarken alle 100 mm und Prüfmaß – wie der Druck unter Windows.
/// </summary>
public static class PlanPdf
{
    private const float Pt = 72f / 25.4f; // PDF-Punkte je mm
    public const double Overlap = 10;

    public static IReadOnlyList<PrintTile> Tiles(LayoutPlan plan) => PrintTiles.Layout(plan.Bounds(), 200, 273, Overlap);

    public static byte[] Build(LayoutPlan plan, bool pro)
    {
        var doc = new PdfDocument();
        // Linienbreiten der Bildschirmdarstellung hängen an der Pixeldichte; für den Druck feine Linien
        var density = Ui.Density;
        Ui.Density = 0.6f;
        try
        {
            Overview(doc, plan, pro);
            var tiles = Tiles(plan);
            int rows = tiles.Max(t => t.Row) + 1, cols = tiles.Max(t => t.Column) + 1;
            var n = 2;
            foreach (var t in tiles) Tile(doc, plan, t, rows, cols, n++, pro);
            using var stream = new MemoryStream();
            doc.WriteTo(stream);
            return stream.ToArray();
        }
        finally
        {
            Ui.Density = density;
            doc.Close();
        }
    }

    private static Paint Text(float size, bool bold = false) => new(PaintFlags.AntiAlias) { Color = Color.Black, TextSize = size, FakeBoldText = bold };

    private static void Overview(PdfDocument doc, LayoutPlan plan, bool pro)
    {
        const int w = 842, h = 595;
        var page = doc.StartPage(new PdfDocument.PageInfo.Builder(w, h, 1).Create());
        var c = page.Canvas!;
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
        c.DrawText(Lang.F($"Maßstab etwa 1:{Fmt.Num(Pt / scale, 0)} · Ab Seite 2: Bauvorlage 1:1 auf {tiles.Count} Seiten A4 (beim Drucken „Tatsächliche Größe“ wählen) · TrackMaster Pro {(pro ? Lang.T("Pro") : Lang.T("Basis"))}"),
                   30, h - 24, Text(8));
        var bom = string.Join(" · ", BillOfMaterials.Build(plan).Take(14).Select(r => $"{r.Count}× {r.Def.Number}"));
        if (bom.Length > 0) c.DrawText((Lang.T("Stückliste: ") + bom)[..Math.Min(bom.Length + 12, 190)], 30, h - 38, Text(8));
        doc.FinishPage(page);
    }

    private static void Tile(PdfDocument doc, LayoutPlan plan, PrintTile t, int rows, int cols, int number, bool pro)
    {
        const int w = 595, h = 842;
        var page = doc.StartPage(new PdfDocument.PageInfo.Builder(w, h, number).Create());
        var c = page.Canvas!;
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
        var y0 = top + th + 14;
        c.DrawLine(left, y0, left + 100 * Pt, y0, line);
        for (var i = 0; i <= 10; i++) c.DrawLine(left + i * 10 * Pt, y0 - (i % 5 == 0 ? 4 : 2), left + i * 10 * Pt, y0, line);
        c.DrawText(Lang.F($"Prüfmaß 100 mm – bitte nachmessen · TrackMaster Pro {(pro ? Lang.T("Pro") : Lang.T("Basis"))}"), left + 100 * Pt + 8, y0 + 3, Text(7));
        doc.FinishPage(page);
    }
}
