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
    private readonly List<TableShape> _tables;
    private static readonly Brush PitBrush = Frozen(Color.FromRgb(0x7A, 0x70, 0x62));
    private static readonly Brush RimBrush = Frozen(Color.FromRgb(0xC9, 0xC2, 0xB6));
    private static readonly Brush DeckBrush = Frozen(Color.FromRgb(0x4E, 0x46, 0x3E));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

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
        _tables = plan.Pieces.Where(p => p.Def.IsTable).Select(p => TableShape.Of(p)!).ToList();
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
        var c = TerrainField.TintAt(height);
        var b = new SolidColorBrush(Color.FromArgb(0xE8, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    private static readonly Brush ShoreBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xC0, 0xB8, 0xA4, 0x78)));
    private static readonly Pen ContourPen = FrozenPen(new SolidColorBrush(Color.FromArgb(0x90, 0x8A, 0x72, 0x50)), 1);
    private static readonly Brush ShadowBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x38, 0x20, 0x28, 0x18)));
    private static readonly Brush ConiferBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x2F, 0x62, 0x30)));
    private static readonly Brush LeafHighlight = Frozen(new SolidColorBrush(Color.FromArgb(0x90, 0x8C, 0xC0, 0x5C)));

    private static Pen FrozenPen(Brush b, double w)
    {
        var p = new Pen(b, w);
        p.Freeze();
        return p;
    }

    /// <summary>Hügel wie auf einer Landkarte: Höhenstufen mit zunehmend erdigem Farbton und Höhenlinien.</summary>
    private static void DrawHill(DrawingContext dc, SceneryItem s, double z, Pen? selected)
    {
        var outline = Polygon(s.Points);
        var f = TerrainField.For(s);
        dc.DrawGeometry(TerrainBrush(0), null, outline);
        var contour = new Pen(ContourPen.Brush, 0.9 / z);
        var index = new Pen(ContourPen.Brush, 1.8 / z);
        var n = 0;
        foreach (var (level, loops) in f.Layers())
        {
            n++;
            var geo = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (var ctx = geo.Open())
                foreach (var loop in loops)
                {
                    ctx.BeginFigure(new Point(loop[0].X, loop[0].Y), true, true);
                    for (var i = 1; i < loop.Count; i++) ctx.LineTo(new Point(loop[i].X, loop[i].Y), true, true);
                }
            geo.Freeze();
            dc.DrawGeometry(TerrainBrush(level), n % 5 == 0 ? index : contour, geo);
        }
        dc.DrawGeometry(null, selected ?? new Pen(TerrainStroke, 1.2 / z) { DashStyle = DashStyles.Dash }, outline);
    }

    /// <summary>Baum von oben: Schatten, Krone mit Lichtkante; Nadelbäume dunkler mit Zweigen.</summary>
    private static void DrawTree(DrawingContext dc, SceneryItem t, double z, Pen? selected)
    {
        var r = t.Width / 2;
        var c = new Point(t.X, t.Y);
        dc.DrawEllipse(ShadowBrush, null, new Point(t.X + r * 0.3, t.Y + r * 0.3), r, r);
        if (Scene3DModel.IsConifer(t))
        {
            dc.DrawEllipse(ConiferBrush, selected ?? new Pen(TreeStroke, 1 / z), c, r, r);
            var twig = new Pen(TreeStroke, Math.Max(r * 0.08, 0.6 / z));
            for (var k = 0; k < 8; k++)
            {
                var a = k * Math.PI / 4;
                dc.DrawLine(twig, c, new Point(t.X + Math.Cos(a) * r * 0.85, t.Y + Math.Sin(a) * r * 0.85));
            }
            return;
        }
        dc.DrawEllipse(TreeBrush, selected ?? new Pen(TreeStroke, 1 / z), c, r, r);
        dc.DrawEllipse(LeafHighlight, null, new Point(t.X - r * 0.25, t.Y - r * 0.25), r * 0.5, r * 0.5);
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
                DrawHill(dc, s, z, s.Id == o.SelectedObject ? selPen : null);
            foreach (var s in plan.Scenery.Where(s => s.Kind == SceneryKind.Water))
            {
                var water = Polygon(s.Points);
                dc.DrawGeometry(null, new Pen(ShoreBrush, Math.Max(g * 1.4, 3 / z)) { LineJoin = PenLineJoin.Round }, water);
                dc.DrawGeometry(WaterBrush, s.Id == o.SelectedObject ? selPen : new Pen(WaterStroke, 1.5 / z), water);
            }
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

        foreach (var t in _tables) DrawTable(dc, t, g, z);

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
                DrawTree(dc, t, z, t.Id == o.SelectedObject ? selPen : null);

        DrawElectrics(dc, z, o);
        DrawAnnotations(dc, plan, z, o.SelectedObject);
    }

    private static readonly Brush DimensionBrush = Frozen(Color.FromRgb(0x1F, 0x4E, 0x79));

    /// <summary>Beschriftungen und Maßlinien, unabhängig von „Gelände zeigen“.</summary>
    public static void DrawAnnotations(DrawingContext dc, LayoutPlan plan, double z, int? selected)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        var face = new Typeface(Theme.Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        FormattedText Text(string s, double size, Brush b) => new(s, culture, FlowDirection.LeftToRight, face, Math.Max(size, 0.5), b, 1.0);
        void Centered(FormattedText t, double x, double y, double angle)
        {
            dc.PushTransform(new RotateTransform(angle, x, y));
            dc.DrawText(t, new Point(x - t.Width / 2, y - t.Height / 2));
            dc.Pop();
        }
        foreach (var s in plan.Scenery.Where(s => s.IsAnnotation))
        {
            var isSel = s.Id == selected;
            if (s.Kind == SceneryKind.Label)
            {
                if (isSel) dc.DrawGeometry(null, new Pen(Theme.AccentBrush, 1.5 / z) { DashStyle = DashStyles.Dash }, Polygon(s.Outline()));
                Centered(Text(s.Name, s.Height, isSel ? Theme.AccentBrush : Brushes.Black), s.X, s.Y, s.Rotation);
                continue;
            }
            if (s.Points.Count < 2) continue;
            var d = Annotation.Dimension(s, s.Height);
            var brush = isSel ? Theme.AccentBrush : DimensionBrush;
            var pen = new Pen(brush, Math.Max(d.TextSize * 0.06, 0.8 / z));
            var thin = new Pen(brush, Math.Max(d.TextSize * 0.03, 0.6 / z));
            Point Pt((double X, double Y) q) => new(q.X, q.Y);
            // Hilfslinien vom Messpunkt bis etwas über die Maßlinie, dann Maßlinie mit Schrägstrichen
            double ex = d.A.X - d.P1.X, ey = d.A.Y - d.P1.Y, el = Math.Max(Math.Sqrt(ex * ex + ey * ey), 1e-9), over = d.TextSize * 0.4;
            dc.DrawLine(thin, Pt(d.P1), new Point(d.A.X + ex / el * over, d.A.Y + ey / el * over));
            dc.DrawLine(thin, Pt(d.P2), new Point(d.B.X + ex / el * over, d.B.Y + ey / el * over));
            dc.DrawLine(pen, Pt(d.A), Pt(d.B));
            // Schrägstriche unter 45° zur Maßlinie
            double ux = d.B.X - d.A.X, uy = d.B.Y - d.A.Y, ul = Math.Max(Math.Sqrt(ux * ux + uy * uy), 1e-9);
            double tx = (ux / ul - uy / ul) * 0.7071 * d.TextSize * 0.45, ty = (uy / ul + ux / ul) * 0.7071 * d.TextSize * 0.45;
            foreach (var q in new[] { d.A, d.B })
                dc.DrawLine(pen, new Point(q.X - tx, q.Y - ty), new Point(q.X + tx, q.Y + ty));
            Centered(Text(d.Text, d.TextSize, brush), d.TextAt.X, d.TextAt.Y, d.TextAngle);
        }
    }

    /// <summary>Drehscheibe: Ring, Grube und Bühne in Richtung Anschluss 0; Schiebebühne: Grube und Bühne.</summary>
    private static void DrawTable(DrawingContext dc, TableShape t, double g, double z)
    {
        var edge = new Pen(Brushes.DimGray, Math.Max(g * 0.12, 0.8 / z));
        if (t.Round)
        {
            var c = new Point(t.Center.X, t.Center.Y);
            dc.DrawEllipse(RimBrush, edge, c, t.RimRadius, t.RimRadius);
            dc.DrawEllipse(PitBrush, edge, c, t.PitRadius, t.PitRadius);
        }
        else dc.DrawGeometry(PitBrush, edge, Polygon(t.Pit));
        var (a, b) = (t.BridgeA, t.BridgeB);
        var bridge = new Leg(a.X, a.Y, Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI, false, Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)));
        dc.DrawGeometry(null, new Pen(DeckBrush, g * 2.6) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat }, TrackDrawing.Build([bridge], 0));
        dc.DrawGeometry(null, TrackDrawing.RailPen(g), TrackDrawing.Build([bridge], g / 2, -g / 2));
        if (t.Round) dc.DrawEllipse(DeckBrush, null, new Point(t.Center.X, t.Center.Y), g * 0.9, g * 0.9);
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
            var face = SignalFace.For(s.Type);
            var scale = SignalFace.PlanScale(g, z);
            var (m11, m12, m21, m22, ox, oy) = SignalFace.PlanTransform(x, y, heading, scale, face);
            dc.PushTransform(new MatrixTransform(m11, m12, m21, m22, ox, oy));
            DrawSignalFace(dc, face, s.Id == o.SelectedObject ? new Pen(Theme.AccentBrush, Math.Max(2.5 / z, g * 0.25) / scale) : null, 1 / (z * scale));
            dc.Pop();
        }
    }

    private static SolidColorBrush B(Rgb c, byte alpha = 0xFF)
    {
        var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    /// <summary>
    /// Zeichnet ein Signalbild in Schirmkoordinaten (Ursprung Mitte der Schirmoberkante, y nach unten, 1 = Schirmbreite).
    /// <paramref name="hairline"/> = Breite einer Bildschirmlinie in diesen Einheiten.
    /// </summary>
    public static void DrawSignalFace(DrawingContext dc, SignalFace f, Pen? selection, double hairline)
    {
        var line = Math.Min(hairline, 0.06);
        // Mast mit Mastschild
        dc.DrawRectangle(B(SignalFace.MastColor), null, new Rect(-0.06, f.Height, 0.12, f.Mast));
        if (f.MastPlate)
        {
            var top = f.Height + SignalFace.PlateGap;
            var w = SignalFace.PlateWidth;
            dc.DrawRectangle(B(SignalFace.PlateWhite), new Pen(B(SignalFace.ShieldColor), line), new Rect(-w / 2, top, w, SignalFace.PlateHeight));
            // weiß-rot-weiß-rot-weiß
            for (var i = 1; i <= 3; i += 2)
                dc.DrawRectangle(B(SignalFace.PlateRed), null, new Rect(-w / 2 + line / 2, top + SignalFace.PlateHeight * i / 5, w - line, SignalFace.PlateHeight / 5));
        }
        // Schirm
        var shield = new Rect(-f.Width / 2, 0, f.Width, f.Height);
        dc.DrawRoundedRectangle(B(SignalFace.ShieldColor), new Pen(B(new Rgb(0x55, 0x55, 0x55)), line), shield, f.Corner, f.Corner);
        foreach (var l in f.Lamps)
        {
            var c = new Point(l.X, l.Y);
            if (l.Lit) dc.DrawEllipse(B(l.Color, 0x50), null, c, l.R * 1.45, l.R * 1.45);
            dc.DrawEllipse(B(SignalFace.Shown(l)), new Pen(B(new Rgb(0x60, 0x60, 0x60)), line * 0.8), c, l.R, l.R);
            if (l.Lit) dc.DrawEllipse(B(new Rgb(0xFF, 0xFF, 0xFF), 0x90), null, new Point(l.X - l.R * 0.3, l.Y - l.R * 0.3), l.R * 0.28, l.R * 0.28);
        }
        if (selection is not null)
            dc.DrawRoundedRectangle(null, selection, new Rect(shield.X - 0.12, -0.12, shield.Width + 0.24, f.TotalHeight + 0.24), f.Corner + 0.1, f.Corner + 0.1);
    }

    /// <summary>Signalbild als Symbol, z. B. für Menüs und Eigenschaften.</summary>
    public static ImageSource SignalImage(SignalType type)
    {
        var face = SignalFace.For(type);
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            // gleiche Höhe für alle Arten, damit die Größen vergleichbar bleiben
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(-0.8, -0.1, 1.6, 4.2));
            dc.PushTransform(new TranslateTransform(0, 4.1 - face.TotalHeight));
            DrawSignalFace(dc, face, null, 0.03);
            dc.Pop();
        }
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

}
