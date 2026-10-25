using System.Globalization;
using System.Windows;
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
}
