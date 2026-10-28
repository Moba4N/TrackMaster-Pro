using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using TrackMaster.Core;

namespace TrackMaster.App;

/// <summary>Druckt den Plan auf eine Seite: Kopfzeile, Plan passend verkleinert, Maßstab und Stückliste in Kurzform.</summary>
public static class PlanPrint
{
    public static DrawingVisual Render(LayoutPlan plan, double pageWidth, double pageHeight, bool pro)
    {
        const double margin = 40;
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();
        var culture = CultureInfo.GetCultureInfo("de-DE");
        var face = new Typeface(Theme.Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var regular = new Typeface(Theme.Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        FormattedText T(string s, double size, Typeface tf, Brush? b = null) => new(s, culture, FlowDirection.LeftToRight, tf, size, b ?? Brushes.Black, 1.0);

        var title = T(plan.Name, 18, face);
        dc.DrawText(title, new Point(margin, margin));
        var stats = plan.Statistics();
        var sub = T($"Spur {plan.GaugeId} ({plan.Gauge.Scale}) · Platte {BoardGeometry.Describe(plan)} · {stats.Pieces} Gleise, " +
                    $"{Fmt.Num(stats.TrackLengthMm / 1000, 2)} m · {DateTime.Now:dd.MM.yyyy}", 10, regular, Brushes.DimGray);
        dc.DrawText(sub, new Point(margin, margin + title.Height + 2));

        var top = margin + title.Height + sub.Height + 16;
        var bottom = pageHeight - margin - 40;
        var (x0, y0, x1, y1) = plan.Bounds();
        double w = Math.Max(x1 - x0, 1), h = Math.Max(y1 - y0, 1);
        // Seiteneinheiten sind 1/96 Zoll: 1 mm = 96 / 25,4
        var scale = Math.Min((pageWidth - 2 * margin) / w, (bottom - top) / h);
        var ratio = 96 / 25.4 / scale;
        dc.PushTransform(new TranslateTransform(margin + ((pageWidth - 2 * margin) - w * scale) / 2 - x0 * scale, top - y0 * scale));
        dc.PushTransform(new ScaleTransform(scale, scale));
        new PlanScene(plan, new HashSet<int>(), false).Draw(dc, scale, new RenderOptions(pro, false, new HashSet<int>(), null));
        dc.Pop();
        dc.Pop();

        var footer = T($"Maßstab etwa 1:{Fmt.Num(ratio, 0)} · Raster der Platte nicht gedruckt · TrackMaster Pro {(pro ? "Pro" : "Basis")}", 9, regular, Brushes.DimGray);
        dc.DrawText(footer, new Point(margin, pageHeight - margin - footer.Height));
        var bom = string.Join(" · ", BillOfMaterials.Build(plan).Take(12).Select(r => $"{r.Count}× {r.Def.Number}"));
        if (bom.Length > 0)
        {
            var list = T("Stückliste: " + bom, 9, regular);
            list.MaxTextWidth = pageWidth - 2 * margin;
            dc.DrawText(list, new Point(margin, pageHeight - margin - footer.Height - list.Height - 4));
        }
        return visual;
    }

    /// <summary>
    /// Eine Seite der Bauvorlage im Maßstab 1:1: Planausschnitt, Passmarken auf dem 100-mm-Raster, Klebekanten,
    /// Seitenbezeichnung mit den Nachbarseiten und ein Prüfmaß von 100 mm.
    /// </summary>
    public static DrawingVisual RenderTile(LayoutPlan plan, PrintTile tile, int rows, int cols, double pageWidth, double pageHeight, bool pro, double overlap)
    {
        const double mm = 96 / 25.4;
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();
        var culture = CultureInfo.GetCultureInfo("de-DE");
        var regular = new Typeface(Theme.Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var bold = new Typeface(Theme.Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        FormattedText T(string s, double size, Typeface tf) => new(s, culture, FlowDirection.LeftToRight, tf, size, Brushes.Black, 1.0);

        // Ausschnitt mittig auf die Seite
        double w = (tile.X1 - tile.X0) * mm, h = (tile.Y1 - tile.Y0) * mm;
        double left = (pageWidth - w) / 2, top = (pageHeight - h) / 2;
        dc.PushClip(new RectangleGeometry(new Rect(left, top, w, h)));
        dc.PushTransform(new TranslateTransform(left - tile.X0 * mm, top - tile.Y0 * mm));
        dc.PushTransform(new ScaleTransform(mm, mm));
        new PlanScene(plan, new HashSet<int>(), false).Draw(dc, mm, new RenderOptions(true, false, new HashSet<int>(), null));
        // Passmarken: Kreuze auf dem festen 100-mm-Raster, auf Nachbarseiten an derselben Stelle
        var markPen = new Pen(Brushes.Black, 0.25);
        foreach (var (x, y) in PrintTiles.Marks(tile))
        {
            dc.DrawLine(markPen, new Point(x - 4, y), new Point(x + 4, y));
            dc.DrawLine(markPen, new Point(x, y - 4), new Point(x, y + 4));
            dc.DrawEllipse(null, markPen, new Point(x, y), 1.5, 1.5);
        }
        dc.Pop();
        dc.Pop();
        // Klebekanten: grau hinterlegt, wo die rechte bzw. untere Nachbarseite überlappt
        var glue = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));
        if (tile.Column < cols - 1) dc.DrawRectangle(glue, null, new Rect(left + w - overlap * mm, top, overlap * mm, h));
        if (tile.Row < rows - 1) dc.DrawRectangle(glue, null, new Rect(left, top + h - overlap * mm, w, overlap * mm));
        dc.Pop();
        dc.DrawRectangle(null, new Pen(Brushes.Black, 0.6) { DashStyle = DashStyles.Dash }, new Rect(left, top, w, h));

        // Kopfzeile: Seite, Nachbarn, Planname
        string? Neighbour(int r, int c) => r >= 0 && r < rows && c >= 0 && c < cols ? $"{PrintTiles.RowName(r)}{c + 1}" : null;
        var label = T($"Seite {tile.Label}", 11, bold);
        dc.DrawText(label, new Point(left, top - label.Height - 3));
        var info = T($"{plan.Name} · Bauvorlage 1:1 · {rows} × {cols} Seiten · " +
                     string.Join(", ", new[] { ("oben", Neighbour(tile.Row - 1, tile.Column)), ("unten", Neighbour(tile.Row + 1, tile.Column)),
                                               ("links", Neighbour(tile.Row, tile.Column - 1)), ("rechts", Neighbour(tile.Row, tile.Column + 1)) }
                         .Where(n => n.Item2 is not null).Select(n => $"{n.Item1} {n.Item2}")), 8, regular);
        dc.DrawText(info, new Point(left + label.Width + 10, top - info.Height - 4));
        // Prüfmaß 100 mm unten links
        var y0 = top + h + 6;
        var pen = new Pen(Brushes.Black, 0.8);
        dc.DrawLine(pen, new Point(left, y0), new Point(left + 100 * mm, y0));
        for (var i = 0; i <= 10; i++)
            dc.DrawLine(pen, new Point(left + i * 10 * mm, y0 - (i % 5 == 0 ? 4 : 2)), new Point(left + i * 10 * mm, y0));
        var check = T($"Prüfmaß 100 mm – bitte nachmessen · TrackMaster Pro {(pro ? "Pro" : "Basis")}", 8, regular);
        dc.DrawText(check, new Point(left + 100 * mm + 8, y0 - check.Height / 2));
        return visual;
    }
}

/// <summary>Mehrseitiger Druck der Bauvorlage: jede Seite wird erst beim Drucken gezeichnet.</summary>
public sealed class TilePaginator(LayoutPlan plan, IReadOnlyList<PrintTile> tiles, Size page, bool pro, double overlap) : DocumentPaginator
{
    private readonly int _rows = tiles.Max(t => t.Row) + 1;
    private readonly int _cols = tiles.Max(t => t.Column) + 1;

    public override DocumentPage GetPage(int pageNumber)
        => new(PlanPrint.RenderTile(plan, tiles[pageNumber], _rows, _cols, page.Width, page.Height, pro, overlap), page, new Rect(page), new Rect(page));

    public override bool IsPageCountValid => true;
    public override int PageCount => tiles.Count;
    public override Size PageSize { get => page; set { } }
    public override IDocumentPaginatorSource? Source => null;
}
