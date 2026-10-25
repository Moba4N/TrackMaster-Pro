using System.Windows;
using System.Windows.Media;
using TrackMaster.Core;

namespace TrackMaster.App;

/// <summary>Baut WPF-Geometrien aus Gleisabschnitten (Gleisbett, Schienen, Prellböcke).</summary>
public static class TrackDrawing
{
    public static void AddLeg(StreamGeometryContext ctx, Leg leg, double offset)
    {
        var (x0, y0, x1, y1, r) = leg.Offset(offset);
        ctx.BeginFigure(new Point(x0, y0), false, false);
        if (leg.IsArc)
            ctx.ArcTo(new Point(x1, y1), new Size(r, r), 0, leg.Angle > 180, leg.Turn > 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
        else
            ctx.LineTo(new Point(x1, y1), true, false);
    }

    public static StreamGeometry Build(IEnumerable<Leg> legs, params double[] offsets)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
            foreach (var l in legs)
                foreach (var o in offsets)
                    AddLeg(ctx, l, o);
        g.Freeze();
        return g;
    }

    /// <summary>Querbalken am Ende eines Prellbocks.</summary>
    public static StreamGeometry Stops(IEnumerable<Leg> bufferLegs, double gauge)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
            foreach (var l in bufferLegs)
            {
                var e = l.End;
                double nx = -Math.Sin(e.HeadingRad) * gauge * 1.1, ny = Math.Cos(e.HeadingRad) * gauge * 1.1;
                ctx.BeginFigure(new Point(e.X - nx, e.Y - ny), false, false);
                ctx.LineTo(new Point(e.X + nx, e.Y + ny), true, false);
            }
        g.Freeze();
        return g;
    }

    public static Pen BedPen(double gauge) => Frozen(new Pen(Theme.BedBrush, gauge * 2.4) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat });
    public static Pen SleeperPen(double gauge)
    {
        var thickness = gauge * 1.9;
        // WPF misst Strichlängen in Vielfachen der Strichstärke
        return Frozen(new Pen(Theme.SleeperBrush, thickness) { DashStyle = new DashStyle([gauge * 0.16 / thickness, gauge * 0.3 / thickness], 0), DashCap = PenLineCap.Flat });
    }
    public static Pen RailPen(double gauge) => Frozen(new Pen(Theme.RailBrush, Math.Max(gauge * 0.11, 0.5)));
    public static Pen StopPen(double gauge) => Frozen(new Pen(Theme.DangerBrush, gauge * 0.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

    private static Pen Frozen(Pen p)
    {
        p.Freeze();
        return p;
    }

    /// <summary>Vorschaubild eines Katalogteils für die Bibliothek.</summary>
    public static ImageSource Thumbnail(TrackPieceDef def)
    {
        var gauge = Catalog.GetGauge(def.GaugeId).TrackWidth;
        var legs = TrackGeometry.LocalLegs(def, false);
        var size = Math.Max(legs.SelectMany(l => new[] { l.Start, l.At(0.5), l.End }).Max(p => Math.Abs(p.X)), gauge * 6);
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(null, new Pen(Theme.BedBrush, gauge * 2.4), Build(legs, 0)));
        group.Children.Add(new GeometryDrawing(null, new Pen(Theme.RailBrush, Math.Max(gauge * 0.18, size / 90)), Build(legs, gauge / 2, -gauge / 2)));
        if (def.Kind == PieceKind.Buffer)
            group.Children.Add(new GeometryDrawing(null, StopPen(gauge), Stops(legs, gauge)));
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
