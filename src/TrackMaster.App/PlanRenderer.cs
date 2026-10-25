using System.Windows;
using System.Windows.Media;
using TrackMaster.Core;

namespace TrackMaster.App;

/// <summary>Was die Zeichnung zeigt (Zeichenfläche und Druck).</summary>
public sealed record RenderOptions(bool Scenery, bool Sections, IReadOnlySet<int> Selected, int? SelectedObject);

/// <summary>
/// Zeichnet den Plan in Weltkoordinaten (mm): Platte, Gelände, Gebäude, Gleise nach Höhe geschichtet (Brücken liegen oben),
/// Tunnel, Bäume und Elektrik. Die Geometrie wird einmal je Planänderung gebaut und dann für jedes Bild wiederverwendet.
/// </summary>
public sealed class PlanScene
{
    private sealed record Layer(double Height, StreamGeometry Bed, StreamGeometry Rails, StreamGeometry Stops, bool Tunnel, StreamGeometry? Walls);

    private readonly LayoutPlan _plan;
    private readonly double _gauge;
    private readonly List<Layer> _layers = [];
    private readonly List<(StreamGeometry Geometry, Brush Brush)> _sections = [];
    private readonly StreamGeometry _selected;

    public static readonly Color[] SectionColors =
    [
        Color.FromRgb(0x00, 0x67, 0xC0), Color.FromRgb(0xD8, 0x3B, 0x01), Color.FromRgb(0x10, 0x7C, 0x10), Color.FromRgb(0x88, 0x17, 0x98),
        Color.FromRgb(0xC2, 0x9A, 0x00), Color.FromRgb(0x00, 0x8B, 0x8B), Color.FromRgb(0xC3, 0x00, 0x52), Color.FromRgb(0x5C, 0x5C, 0x5C),
    ];

    public PlanScene(LayoutPlan plan, IReadOnlySet<int> selected, bool sections)
    {
        _plan = plan;
        _gauge = plan.Gauge.TrackWidth;
        // Gleise nach Höhe schichten, damit obere Strecken über unteren liegen; Tunnel zuunterst
        foreach (var g in plan.Pieces.GroupBy(p => (p.Tunnel, Math.Round(p.ConnectorHeights().Max() / 5) * 5)).OrderBy(g => g.Key.Tunnel ? 0 : 1).ThenBy(g => g.Key.Item2))
        {
            var legs = g.SelectMany(p => p.WorldLegs()).ToList();
            _layers.Add(new Layer(g.Key.Item2,
                TrackDrawing.Build(legs, 0),
                TrackDrawing.Build(legs, _gauge / 2, -_gauge / 2),
                TrackDrawing.Stops(g.Where(p => p.Def.Kind == PieceKind.Buffer).Select(p => p.WorldLegs()[0]), _gauge),
                g.Key.Tunnel,
                g.Key.Tunnel ? TrackDrawing.Build(legs, _gauge * 1.7, -_gauge * 1.7) : null));
        }
        _selected = TrackDrawing.Build(plan.Pieces.Where(p => selected.Contains(p.Id)).SelectMany(p => p.WorldLegs()), 0);
        if (sections)
        {
            var list = Electrics.Sections(plan);
            foreach (var s in list)
            {
                var set = s.PieceIds.ToHashSet();
                var brush = new SolidColorBrush(Color.FromArgb(0x70, SectionColors[(s.Number - 1) % SectionColors.Length].R, SectionColors[(s.Number - 1) % SectionColors.Length].G, SectionColors[(s.Number - 1) % SectionColors.Length].B));
                brush.Freeze();
                _sections.Add((TrackDrawing.Build(plan.Pieces.Where(p => set.Contains(p.Id)).SelectMany(p => p.WorldLegs()), 0), brush));
            }
        }
    }

    public static Brush TerrainBrush(double height)
    {
        var t = Math.Clamp(height / 250, 0, 1);
        byte L(byte a, byte b) => (byte)(a + (b - a) * t);
        var b = new SolidColorBrush(Color.FromArgb(0xE0, L(0xB4, 0x8A), L(0xC9, 0x9A), L(0x95, 0x62)));
        b.Freeze();
        return b;
    }

    private static readonly Brush WaterBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xE0, 0x86, 0xBC, 0xE6)));
    private static readonly Brush WaterStroke = Frozen(new SolidColorBrush(Color.FromRgb(0x3F, 0x86, 0xC0)));
    private static readonly Brush TerrainStroke = Frozen(new SolidColorBrush(Color.FromRgb(0x6E, 0x80, 0x52)));
    private static readonly Brush RoofBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xB8, 0x5C, 0x45)));
    private static readonly Brush RoofStroke = Frozen(new SolidColorBrush(Color.FromRgb(0x7A, 0x37, 0x28)));
    private static readonly Brush TreeBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xD8, 0x4E, 0x8B, 0x3A)));
    private static readonly Brush TreeStroke = Frozen(new SolidColorBrush(Color.FromRgb(0x2F, 0x5E, 0x22)));
    private static readonly Brush BridgeBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x60, 0x30, 0x30, 0x30)));
    private static readonly Brush TunnelWallBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x6B, 0x5A, 0x48)));
    private static readonly Brush FeederBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C)));
    private static readonly Brush IsolationBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x00)));

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }

    public static Geometry Polygon(IReadOnlyList<(double X, double Y)> pts)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            if (pts.Count > 0)
            {
                ctx.BeginFigure(new Point(pts[0].X, pts[0].Y), true, true);
                for (var i = 1; i < pts.Count; i++) ctx.LineTo(new Point(pts[i].X, pts[i].Y), true, true);
            }
        }
        g.Freeze();
        return g;
    }

    /// <summary>Umriss der Anlagenplatte (L-, U-, Ring-Form usw.; beim Ring mit offener Mitte).</summary>
    public static Geometry BoardOutline(LayoutPlan plan)
    {
        var g = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var ctx = g.Open())
            foreach (var outline in BoardGeometry.Outlines(plan))
            {
                ctx.BeginFigure(new Point(outline[0].X, outline[0].Y), true, true);
                ctx.PolyLineTo(outline.Skip(1).Select(q => new Point(q.X, q.Y)).ToList(), true, false);
            }
        g.Freeze();
        return g;
    }

    /// <summary>Zeichnet alles in Weltkoordinaten. <paramref name="zoom"/> = Pixel je mm, für Mindest-Strichstärken.</summary>
    public void Draw(DrawingContext dc, double zoom, RenderOptions o, Action<DrawingContext>? afterBoard = null)
    {
        var plan = _plan;
        double z = zoom, g = _gauge;
        dc.DrawGeometry(Theme.BoardBrush, new Pen(Theme.BoardStrokeBrush, 1.2 / z) { LineJoin = PenLineJoin.Round }, BoardOutline(plan));
        var selPen = new Pen(Theme.AccentBrush, Math.Max(2.5 / z, g * 0.25));

        if (o.Scenery)
        {
            foreach (var s in plan.Scenery.Where(s => s.Kind == SceneryKind.Terrain).OrderBy(s => s.Height))
                dc.DrawGeometry(TerrainBrush(s.Height), s.Id == o.SelectedObject ? selPen : new Pen(TerrainStroke, 1.2 / z) { DashStyle = DashStyles.Dash }, Polygon(s.Points));
            foreach (var s in plan.Scenery.Where(s => s.Kind == SceneryKind.Water))
                dc.DrawGeometry(WaterBrush, s.Id == o.SelectedObject ? selPen : new Pen(WaterStroke, 1.5 / z), Polygon(s.Points));
            foreach (var s in plan.Scenery.Where(s => s.Kind == SceneryKind.Building))
            {
                var outline = s.Outline();
                dc.DrawGeometry(RoofBrush, s.Id == o.SelectedObject ? selPen : new Pen(RoofStroke, 1.2 / z), Polygon(outline));
                // First entlang der längeren Seite
                var (a, b, c, d) = (outline[0], outline[1], outline[2], outline[3]);
                var ridge = s.Width >= s.Depth
                    ? (new Point((a.X + d.X) / 2, (a.Y + d.Y) / 2), new Point((b.X + c.X) / 2, (b.Y + c.Y) / 2))
                    : (new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2), new Point((d.X + c.X) / 2, (d.Y + c.Y) / 2));
                dc.DrawLine(new Pen(RoofStroke, 1.2 / z), ridge.Item1, ridge.Item2);
            }
        }
        afterBoard?.Invoke(dc);

        if (o.Selected.Count > 0)
            dc.DrawGeometry(null, new Pen(Theme.SelectionBrush, Math.Max(g * 4, 10 / z)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, _selected);
        foreach (var (geo, brush) in _sections)
            dc.DrawGeometry(null, new Pen(brush, Math.Max(g * 3.6, 8 / z)) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat }, geo);

        foreach (var layer in _layers)
        {
            if (layer.Tunnel)
            {
                // Tunnel: blass gezeichnet mit Tunnelwänden
                dc.PushOpacity(0.45);
                dc.DrawGeometry(null, TrackDrawing.BedPen(g), layer.Bed);
                dc.DrawGeometry(null, TrackDrawing.RailPen(g), layer.Rails);
                dc.Pop();
                dc.DrawGeometry(null, new Pen(TunnelWallBrush, Math.Max(g * 0.2, 1 / z)) { DashStyle = new DashStyle([4, 3], 0) }, layer.Walls);
                continue;
            }
            if (layer.Height > 1)
                dc.DrawGeometry(null, new Pen(BridgeBrush, g * 3.2), layer.Bed);
            dc.DrawGeometry(null, TrackDrawing.BedPen(g), layer.Bed);
            dc.DrawGeometry(null, TrackDrawing.SleeperPen(g), layer.Bed);
            dc.DrawGeometry(null, TrackDrawing.RailPen(g), layer.Rails);
            dc.DrawGeometry(null, TrackDrawing.StopPen(g), layer.Stops);
        }

        if (o.Scenery)
            foreach (var t in plan.Scenery.Where(s => s.Kind == SceneryKind.Tree))
                dc.DrawEllipse(TreeBrush, t.Id == o.SelectedObject ? selPen : new Pen(TreeStroke, 1 / z), new Point(t.X, t.Y), t.Width / 2, t.Width / 2);

        DrawElectrics(dc, z, o);
    }

    private void DrawElectrics(DrawingContext dc, double z, RenderOptions o)
    {
        var plan = _plan;
        var g = _gauge;
        var selPen = new Pen(Theme.AccentBrush, 2.5 / z);
        foreach (var i in plan.Isolations)
        {
            if (plan.Find(i.PieceId) is not { } p) continue;
            var conns = p.WorldConnectors();
            if (i.Index >= conns.Count) continue;
            var c = conns[i.Index];
            double nx = -Math.Sin(c.HeadingRad) * g * 1.6, ny = Math.Cos(c.HeadingRad) * g * 1.6;
            dc.DrawLine(new Pen(i.Id == o.SelectedObject ? Theme.AccentBrush : IsolationBrush, Math.Max(g * 0.45, 3 / z)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                new Point(c.X - nx, c.Y - ny), new Point(c.X + nx, c.Y + ny));
        }
        foreach (var f in plan.Feeders)
        {
            if (plan.Find(f.PieceId) is not { } p) continue;
            var m = p.Middle();
            var r = Math.Max(g * 0.9, 5 / z);
            var center = new Point(m.X - Math.Sin(m.HeadingRad) * g * 2.2, m.Y + Math.Cos(m.HeadingRad) * g * 2.2);
            dc.DrawLine(new Pen(FeederBrush, Math.Max(g * 0.15, 1 / z)), new Point(m.X, m.Y), center);
            dc.DrawEllipse(FeederBrush, f.Id == o.SelectedObject ? selPen : new Pen(Brushes.White, 1 / z), center, r, r);
            var bolt = new StreamGeometry();
            using (var ctx = bolt.Open())
            {
                ctx.BeginFigure(new Point(center.X + r * 0.15, center.Y - r * 0.65), false, false);
                ctx.LineTo(new Point(center.X - r * 0.3, center.Y + r * 0.05), true, false);
                ctx.LineTo(new Point(center.X + r * 0.25, center.Y + r * 0.05), true, false);
                ctx.LineTo(new Point(center.X - r * 0.15, center.Y + r * 0.65), true, false);
            }
            dc.DrawGeometry(null, new Pen(Brushes.White, r * 0.18) { LineJoin = PenLineJoin.Round }, bolt);
        }
        foreach (var s in plan.Signals)
        {
            var (x, y, heading) = Electrics.SignalPose(plan, s);
            var r = Math.Max(g * 0.7, 4 / z);
            var h = heading * Math.PI / 180;
            // Mast quer zum Gleis, Schirm zeigt dem ankommenden Zug entgegen
            var foot = new Point(x + Math.Sin(h) * g * 1.2, y - Math.Cos(h) * g * 1.2);
            dc.DrawLine(new Pen(Brushes.Black, Math.Max(g * 0.15, 1 / z)), foot, new Point(x, y));
            var head = SignalColors(s.Type);
            dc.DrawEllipse(Brushes.Black, s.Id == o.SelectedObject ? selPen : null, new Point(x, y), r * 1.25, r * 1.25);
            dc.DrawEllipse(head.Top, null, new Point(x - Math.Cos(h) * r * 0.45, y - Math.Sin(h) * r * 0.45), r * 0.5, r * 0.5);
            dc.DrawEllipse(head.Bottom, null, new Point(x + Math.Cos(h) * r * 0.45, y + Math.Sin(h) * r * 0.45), r * 0.5, r * 0.5);
        }
    }

    public static (Brush Top, Brush Bottom) SignalColors(SignalType t) => t switch
    {
        SignalType.Vorsignal => (Brushes.Gold, Brushes.LimeGreen),
        SignalType.Sperrsignal => (Brushes.White, Brushes.Red),
        SignalType.Blocksignal => (Brushes.LimeGreen, Brushes.Red),
        _ => (Brushes.Red, Brushes.LimeGreen),
    };
}
