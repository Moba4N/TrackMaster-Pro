using TrackMaster.Web.Graphics;
using TrackMaster.Core;
using Path = TrackMaster.Web.Graphics.Path;

namespace TrackMaster.Web.Plan;

/// <summary>
/// Zeichnet den Plan in Weltkoordinaten (mm) auf eine Android-Canvas: Platte, Gelände, Gebäude, Gleise nach Höhe geschichtet,
/// Tunnel, Bäume und Elektrik. Die Pfade werden einmal je Planänderung gebaut (wie PlanScene unter Windows).
/// </summary>
public sealed class PlanPainter
{
    private sealed record Layer(double Height, Path Bed, Path Rails, Path Stops, bool Tunnel, Path? Walls);

    public static readonly Color[] SectionColors =
    [
        Color.Rgb(0x00, 0x67, 0xC0), Color.Rgb(0xD8, 0x3B, 0x01), Color.Rgb(0x10, 0x7C, 0x10), Color.Rgb(0x88, 0x17, 0x98),
        Color.Rgb(0xC2, 0x9A, 0x00), Color.Rgb(0x00, 0x8B, 0x8B), Color.Rgb(0xC3, 0x00, 0x52), Color.Rgb(0x5C, 0x5C, 0x5C),
    ];

    private readonly LayoutPlan _plan;
    private readonly float _g;
    private readonly List<Layer> _layers = [];
    private readonly List<(Path Path, Color Color)> _sections = [];
    private readonly Path _selected;
    private readonly List<TableShape> _tables;
    private readonly Paint _p = new(PaintFlags.AntiAlias);
    private static readonly Color PitFill = Color.Rgb(0x7A, 0x70, 0x62);
    private static readonly Color RimFill = Color.Rgb(0xC9, 0xC2, 0xB6);
    private static readonly Color Deck = Color.Rgb(0x4E, 0x46, 0x3E);

    public PlanPainter(LayoutPlan plan, IReadOnlySet<int> selected, bool sections)
    {
        _plan = plan;
        _g = (float)plan.Gauge.TrackWidth;
        foreach (var grp in plan.Pieces.GroupBy(p => (p.Tunnel, Math.Round(p.ConnectorHeights().Max() / 5) * 5)).OrderBy(g => g.Key.Tunnel ? 0 : 1).ThenBy(g => g.Key.Item2))
        {
            var legs = grp.SelectMany(p => p.WorldLegs()).ToList();
            _layers.Add(new Layer(grp.Key.Item2,
                Build(legs, 0),
                Build(legs, _g / 2, -_g / 2),
                Stops(grp.Where(p => p.Def.Kind == PieceKind.Buffer).Select(p => p.WorldLegs()[0]), _g),
                grp.Key.Tunnel,
                grp.Key.Tunnel ? Build(legs, _g * 1.7, -_g * 1.7) : null));
        }
        _selected = Build(plan.Pieces.Where(p => selected.Contains(p.Id)).SelectMany(p => p.WorldLegs()), 0);
        _tables = plan.Pieces.Where(p => p.Def.IsTable).Select(p => TableShape.Of(p)!).ToList();
        if (sections)
            foreach (var s in Electrics.Sections(plan))
            {
                var set = s.PieceIds.ToHashSet();
                var c = SectionColors[(s.Number - 1) % SectionColors.Length];
                _sections.Add((Build(plan.Pieces.Where(p => set.Contains(p.Id)).SelectMany(p => p.WorldLegs()), 0), Color.Argb(0x70, c.R, c.G, c.B)));
            }
    }

    // ---------- Pfade ----------

    /// <summary>Gleisabschnitt mit seitlichem Versatz; Bögen werden fein unterteilt.</summary>
    public static void AddLeg(Path path, Leg leg, double offset)
    {
        var n = leg.IsArc ? Math.Max(2, (int)Math.Ceiling(leg.Angle / 3)) : 1;
        for (var i = 0; i <= n; i++)
        {
            var q = leg.At((double)i / n);
            var x = (float)(q.X - Math.Sin(q.HeadingRad) * offset);
            var y = (float)(q.Y + Math.Cos(q.HeadingRad) * offset);
            if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
        }
    }

    public static Path Build(IEnumerable<Leg> legs, params double[] offsets)
    {
        var p = new Path();
        foreach (var l in legs)
            foreach (var o in offsets)
                AddLeg(p, l, o);
        return p;
    }

    public static Path Stops(IEnumerable<Leg> bufferLegs, double gauge)
    {
        var p = new Path();
        foreach (var l in bufferLegs)
        {
            var e = l.End;
            double nx = -Math.Sin(e.HeadingRad) * gauge * 1.1, ny = Math.Cos(e.HeadingRad) * gauge * 1.1;
            p.MoveTo((float)(e.X - nx), (float)(e.Y - ny));
            p.LineTo((float)(e.X + nx), (float)(e.Y + ny));
        }
        return p;
    }

    /// <summary>Umriss der Anlagenplatte (L-, U-, Ring-Form usw.; beim Ring mit offener Mitte).</summary>
    public static Path BoardPath(LayoutPlan plan)
    {
        var path = new Path();
        path.SetFillType(Path.FillType.EvenOdd);
        foreach (var outline in BoardGeometry.Outlines(plan))
            path.AddPath(Polygon(outline));
        return path;
    }

    public static Path Polygon(IReadOnlyList<(double X, double Y)> pts, bool close = true)
    {
        var p = new Path();
        for (var i = 0; i < pts.Count; i++)
            if (i == 0) p.MoveTo((float)pts[i].X, (float)pts[i].Y); else p.LineTo((float)pts[i].X, (float)pts[i].Y);
        if (close && pts.Count > 2) p.Close();
        return p;
    }

    // ---------- Stifte ----------

    private Paint Stroke(Color c, float width, Paint.Cap? cap = null, DashPathEffect? effect = null)
    {
        _p.Reset();
        _p.AntiAlias = true;
        _p.SetStyle(Paint.Style.Stroke);
        _p.Color = c;
        _p.StrokeWidth = width;
        _p.StrokeCap = cap ?? Paint.Cap.Butt;
        _p.StrokeJoin = Paint.Join.Round;
        _p.SetPathEffect(effect);
        return _p;
    }

    private Paint Fill(Color c)
    {
        _p.Reset();
        _p.AntiAlias = true;
        _p.SetStyle(Paint.Style.Fill);
        _p.Color = c;
        return _p;
    }

    public static Color TerrainColor(double height)
    {
        var t = TerrainField.TintAt(height);
        return Color.Argb(0xE8, t.R, t.G, t.B);
    }

    private static readonly Color Contour = Color.Argb(0x90, 0x8A, 0x72, 0x50);
    private static readonly Color Shore = Color.Argb(0xC0, 0xB8, 0xA4, 0x78);
    private static readonly Color TreeShadow = Color.Argb(0x38, 0x20, 0x28, 0x18);
    private static readonly Color ConiferFill = Color.Rgb(0x2F, 0x62, 0x30);
    private static readonly Color LeafHighlight = Color.Argb(0x90, 0x8C, 0xC0, 0x5C);

    private static readonly Color WaterFill = Color.Argb(0xE0, 0x86, 0xBC, 0xE6);
    private static readonly Color WaterStroke = Color.Rgb(0x3F, 0x86, 0xC0);
    private static readonly Color TerrainStroke = Color.Rgb(0x6E, 0x80, 0x52);
    private static readonly Color RoofFill = Color.Rgb(0xB8, 0x5C, 0x45);
    private static readonly Color RoofStroke = Color.Rgb(0x7A, 0x37, 0x28);
    private static readonly Color TreeFill = Color.Argb(0xD8, 0x4E, 0x8B, 0x3A);
    private static readonly Color TreeStroke = Color.Rgb(0x2F, 0x5E, 0x22);
    private static readonly Color BridgeShadow = Color.Argb(0x60, 0x30, 0x30, 0x30);
    private static readonly Color TunnelWall = Color.Rgb(0x6B, 0x5A, 0x48);
    private static readonly Color FeederColor = Color.Rgb(0xC4, 0x2B, 0x1C);
    private static readonly Color IsolationColor = Color.Rgb(0xE0, 0x6C, 0x00);

    /// <summary>Zeichnet alles in Weltkoordinaten; die Canvas ist bereits skaliert. <paramref name="zoom"/> = Pixel je mm.</summary>
    public void Draw(Canvas c, float zoom, bool scenery, IReadOnlySet<int> selection, int? selectedObject, Action<Canvas>? afterBoard = null)
    {
        var plan = _plan;
        float z = zoom, g = _g;
        var board = BoardPath(plan);
        c.DrawPath(board, Fill(Ui.Board));
        c.DrawPath(board, Stroke(Ui.BoardStroke, 1.2f * Ui.Density / z));
        var selWidth = Math.Max(2.5f * Ui.Density / z, g * 0.25f);

        if (scenery)
        {
            foreach (var s in plan.Scenery.Where(s => s.Kind == SceneryKind.Terrain).OrderBy(s => s.Height))
            {
                // Wie auf einer Landkarte: Höhenstufen mit Höhenlinien
                var path = Polygon(s.Points);
                c.DrawPath(path, Fill(TerrainColor(0)));
                var n = 0;
                foreach (var (level, loops) in TerrainField.For(s).Layers())
                {
                    n++;
                    var layer = new Path();
                    layer.SetFillType(Path.FillType.EvenOdd);
                    foreach (var loop in loops) layer.AddPath(Polygon(loop));
                    c.DrawPath(layer, Fill(TerrainColor(level)));
                    c.DrawPath(layer, Stroke(Contour, (n % 5 == 0 ? 1.8f : 0.9f) * Ui.Density / z));
                }
                c.DrawPath(path, s.Id == selectedObject ? Stroke(Ui.Accent, selWidth) : Stroke(TerrainStroke, 1.2f * Ui.Density / z, effect: new DashPathEffect([6 / z, 4 / z], 0)));
            }
            foreach (var s in plan.Scenery.Where(s => s.Kind == SceneryKind.Water))
            {
                var path = Polygon(s.Points);
                var shore = Stroke(Shore, Math.Max(g * 1.4f, 3 * Ui.Density / z));
                shore.StrokeJoin = Paint.Join.Round;
                c.DrawPath(path, shore);
                c.DrawPath(path, Fill(WaterFill));
                c.DrawPath(path, s.Id == selectedObject ? Stroke(Ui.Accent, selWidth) : Stroke(WaterStroke, 1.5f * Ui.Density / z));
            }
            foreach (var s in plan.Scenery.Where(s => s.Kind == SceneryKind.Building))
            {
                var o = s.Outline();
                var path = Polygon(o);
                c.DrawPath(path, Fill(RoofFill));
                c.DrawPath(path, s.Id == selectedObject ? Stroke(Ui.Accent, selWidth) : Stroke(RoofStroke, 1.2f * Ui.Density / z));
                var (a, b, cc, d) = (o[0], o[1], o[2], o[3]);
                var ridge = s.Width >= s.Depth
                    ? (((a.X + d.X) / 2, (a.Y + d.Y) / 2), ((b.X + cc.X) / 2, (b.Y + cc.Y) / 2))
                    : (((a.X + b.X) / 2, (a.Y + b.Y) / 2), ((d.X + cc.X) / 2, (d.Y + cc.Y) / 2));
                c.DrawLine((float)ridge.Item1.Item1, (float)ridge.Item1.Item2, (float)ridge.Item2.Item1, (float)ridge.Item2.Item2, Stroke(RoofStroke, 1.2f * Ui.Density / z));
            }
        }
        afterBoard?.Invoke(c);

        if (selection.Count > 0)
            c.DrawPath(_selected, Stroke(Ui.Selection, Math.Max(g * 4, 10 * Ui.Density / z), Paint.Cap.Round));
        foreach (var (path, color) in _sections)
            c.DrawPath(path, Stroke(color, Math.Max(g * 3.6f, 8 * Ui.Density / z)));

        // Drehscheiben und Schiebebühnen: Grube, Ring und Bühne
        foreach (var t in _tables)
        {
            var edge = Math.Max(g * 0.12f, 0.8f * Ui.Density / z);
            float cx = (float)t.Center.X, cy = (float)t.Center.Y;
            if (t.Round)
            {
                c.DrawCircle(cx, cy, (float)t.RimRadius, Fill(RimFill));
                c.DrawCircle(cx, cy, (float)t.RimRadius, Stroke(Color.DarkGray, edge));
                c.DrawCircle(cx, cy, (float)t.PitRadius, Fill(PitFill));
                c.DrawCircle(cx, cy, (float)t.PitRadius, Stroke(Color.DarkGray, edge));
            }
            else
            {
                var pit = Polygon(t.Pit);
                c.DrawPath(pit, Fill(PitFill));
                c.DrawPath(pit, Stroke(Color.DarkGray, edge));
            }
            var (a, b) = (t.BridgeA, t.BridgeB);
            var bridge = new Leg(a.X, a.Y, Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI, false, Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)));
            c.DrawPath(Build([bridge], 0), Stroke(Deck, g * 2.6f));
            c.DrawPath(Build([bridge], _g / 2, -_g / 2), Stroke(Ui.Rail, Math.Max(g * 0.11f, 0.6f / z)));
            if (t.Round) c.DrawCircle(cx, cy, g * 0.9f, Fill(Deck));
        }

        foreach (var layer in _layers)
        {
            if (layer.Tunnel)
            {
                c.DrawPath(layer.Bed, Stroke(Color.Argb(0x73, Ui.Bed.R, Ui.Bed.G, Ui.Bed.B), g * 2.4f));
                c.DrawPath(layer.Rails, Stroke(Color.Argb(0x73, Ui.Rail.R, Ui.Rail.G, Ui.Rail.B), Math.Max(g * 0.11f, 0.5f)));
                if (layer.Walls is { } walls)
                    c.DrawPath(walls, Stroke(TunnelWall, Math.Max(g * 0.2f, 1 / z), effect: new DashPathEffect([g * 0.8f, g * 0.6f], 0)));
                continue;
            }
            if (layer.Height > 1)
                c.DrawPath(layer.Bed, Stroke(BridgeShadow, g * 3.2f));
            c.DrawPath(layer.Bed, Stroke(Ui.Bed, g * 2.4f));
            // Schwellen nur, wenn sie auf dem Bildschirm mindestens zwei Pixel breit sind
            if (g * 0.16f * z >= 1.5f)
                c.DrawPath(layer.Bed, Stroke(Ui.Sleeper, g * 1.9f, effect: new DashPathEffect([g * 0.16f, g * 0.3f], 0)));
            c.DrawPath(layer.Rails, Stroke(Ui.Rail, Math.Max(g * 0.11f, 0.6f / z)));
            c.DrawPath(layer.Stops, Stroke(Ui.Danger, g * 0.5f, Paint.Cap.Round));
        }

        if (scenery)
            foreach (var t in plan.Scenery.Where(s => s.Kind == SceneryKind.Tree))
            {
                // Von oben: Schatten, Krone mit Lichtkante; Nadelbäume dunkler mit Zweigen
                float x = (float)t.X, y = (float)t.Y, r = (float)t.Width / 2;
                c.DrawCircle(x + r * 0.3f, y + r * 0.3f, r, Fill(TreeShadow));
                var conifer = Scene3DModel.IsConifer(t);
                c.DrawCircle(x, y, r, Fill(conifer ? ConiferFill : TreeFill));
                if (conifer)
                    for (var k = 0; k < 8; k++)
                    {
                        var a = k * Math.PI / 4;
                        c.DrawLine(x, y, x + (float)Math.Cos(a) * r * 0.85f, y + (float)Math.Sin(a) * r * 0.85f, Stroke(TreeStroke, Math.Max(r * 0.08f, 0.6f * Ui.Density / z)));
                    }
                else
                    c.DrawCircle(x - r * 0.25f, y - r * 0.25f, r * 0.5f, Fill(LeafHighlight));
                c.DrawCircle(x, y, r, t.Id == selectedObject ? Stroke(Ui.Accent, selWidth) : Stroke(TreeStroke, 1 * Ui.Density / z));
            }

        DrawElectrics(c, z, selectedObject);
        DrawAnnotations(c, plan, z, selectedObject);
    }

    private static readonly Color DimensionColor = Color.Rgb(0x1F, 0x4E, 0x79);

    /// <summary>Beschriftungen und Maßlinien, unabhängig von „Gelände zeigen“.</summary>
    public static void DrawAnnotations(Canvas c, LayoutPlan plan, float z, int? selected)
    {
        using var text = new Paint(PaintFlags.AntiAlias) { TextAlign = Paint.Align.Center };
        text.SetTypeface(true);
        using var line = new Paint(PaintFlags.AntiAlias);
        line.SetStyle(Paint.Style.Stroke);
        void Centered(string s, double x, double y, double size, double angle, Color color)
        {
            text.TextSize = (float)Math.Max(size, 0.5);
            text.Color = color;
            c.Save();
            c.Rotate((float)angle, (float)x, (float)y);
            c.DrawText(s, (float)x, (float)(y + size * 0.35), text);
            c.Restore();
        }
        foreach (var s in plan.Scenery.Where(s => s.IsAnnotation))
        {
            var isSel = s.Id == selected;
            if (s.Kind == SceneryKind.Label)
            {
                if (isSel)
                {
                    line.Color = Ui.Accent;
                    line.StrokeWidth = 1.5f * Ui.Density / z;
                    c.DrawPath(Polygon(s.Outline()), line);
                }
                Centered(s.Name, s.X, s.Y, s.Height, s.Rotation, isSel ? Ui.Accent : Color.Black);
                continue;
            }
            if (s.Points.Count < 2) continue;
            var d = Annotation.Dimension(s, s.Height);
            var color = isSel ? Ui.Accent : DimensionColor;
            line.Color = color;
            double ex = d.A.X - d.P1.X, ey = d.A.Y - d.P1.Y, el = Math.Max(Math.Sqrt(ex * ex + ey * ey), 1e-9), over = d.TextSize * 0.4;
            line.StrokeWidth = (float)Math.Max(d.TextSize * 0.03, 0.6 * Ui.Density / z);
            c.DrawLine((float)d.P1.X, (float)d.P1.Y, (float)(d.A.X + ex / el * over), (float)(d.A.Y + ey / el * over), line);
            c.DrawLine((float)d.P2.X, (float)d.P2.Y, (float)(d.B.X + ex / el * over), (float)(d.B.Y + ey / el * over), line);
            line.StrokeWidth = (float)Math.Max(d.TextSize * 0.06, 0.8 * Ui.Density / z);
            c.DrawLine((float)d.A.X, (float)d.A.Y, (float)d.B.X, (float)d.B.Y, line);
            double ux = d.B.X - d.A.X, uy = d.B.Y - d.A.Y, ul = Math.Max(Math.Sqrt(ux * ux + uy * uy), 1e-9);
            double tx = (ux / ul - uy / ul) * 0.7071 * d.TextSize * 0.45, ty = (uy / ul + ux / ul) * 0.7071 * d.TextSize * 0.45;
            foreach (var q in new[] { d.A, d.B })
                c.DrawLine((float)(q.X - tx), (float)(q.Y - ty), (float)(q.X + tx), (float)(q.Y + ty), line);
            Centered(d.Text, d.TextAt.X, d.TextAt.Y, d.TextSize, d.TextAngle, color);
        }
    }

    private void DrawElectrics(Canvas c, float z, int? selectedObject)
    {
        var plan = _plan;
        var g = _g;
        var d = Ui.Density;
        foreach (var i in plan.Isolations)
        {
            if (plan.Find(i.PieceId) is not { } p) continue;
            var conns = p.WorldConnectors();
            if (i.Index >= conns.Count) continue;
            var cn = conns[i.Index];
            float nx = (float)(-Math.Sin(cn.HeadingRad) * g * 1.6), ny = (float)(Math.Cos(cn.HeadingRad) * g * 1.6);
            c.DrawLine((float)cn.X - nx, (float)cn.Y - ny, (float)cn.X + nx, (float)cn.Y + ny,
                Stroke(i.Id == selectedObject ? Ui.Accent : IsolationColor, Math.Max(g * 0.45f, 3 * d / z), Paint.Cap.Round));
        }
        foreach (var f in plan.Feeders)
        {
            if (plan.Find(f.PieceId) is not { } p) continue;
            var m = p.Middle();
            var r = Math.Max(g * 0.9f, 5 * d / z);
            var cx = (float)(m.X - Math.Sin(m.HeadingRad) * g * 2.2);
            var cy = (float)(m.Y + Math.Cos(m.HeadingRad) * g * 2.2);
            c.DrawLine((float)m.X, (float)m.Y, cx, cy, Stroke(FeederColor, Math.Max(g * 0.15f, 1 / z)));
            c.DrawCircle(cx, cy, r, Fill(FeederColor));
            c.DrawCircle(cx, cy, r, f.Id == selectedObject ? Stroke(Ui.Accent, 2.5f * d / z) : Stroke(Color.White, 1 / z));
            var bolt = new Path();
            bolt.MoveTo(cx + r * 0.15f, cy - r * 0.65f);
            bolt.LineTo(cx - r * 0.3f, cy + r * 0.05f);
            bolt.LineTo(cx + r * 0.25f, cy + r * 0.05f);
            bolt.LineTo(cx - r * 0.15f, cy + r * 0.65f);
            c.DrawPath(bolt, Stroke(Color.White, r * 0.18f, Paint.Cap.Round));
        }
        foreach (var s in plan.Signals)
        {
            var (x, y, heading) = Electrics.SignalPose(plan, s);
            var face = SignalFace.For(s.Type);
            var scale = SignalFace.PlanScale(g, z, 6 * d);
            var (m11, m12, m21, m22, ox, oy) = SignalFace.PlanTransform(x, y, heading, scale, face);
            c.Save();
            c.Concat(m11, m12, m21, m22, ox, oy);
            DrawSignalFace(c, face, s.Id == selectedObject ? (float)(2.5 * d / z / scale) : 0, (float)(1 / (z * scale)));
            c.Restore();
        }
    }

    private static Color C(Rgb c, int alpha = 0xFF) => Color.Argb(alpha, c.R, c.G, c.B);

    /// <summary>
    /// Zeichnet ein Signalbild in Schirmkoordinaten (Ursprung Mitte der Schirmoberkante, y nach unten, 1 = Schirmbreite).
    /// <paramref name="selection"/> &gt; 0 zeichnet einen Auswahlrahmen dieser Breite; <paramref name="hairline"/> = Breite einer Bildschirmlinie.
    /// </summary>
    public static void DrawSignalFace(Canvas c, SignalFace f, float selection, float hairline)
    {
        using var p = new Paint(PaintFlags.AntiAlias);
        var line = Math.Min(hairline, 0.06f);
        void Fill(Rgb col, int alpha = 0xFF) { p.SetStyle(Paint.Style.Fill); p.Color = C(col, alpha); }
        void Stroke(Rgb col, float w) { p.SetStyle(Paint.Style.Stroke); p.Color = C(col); p.StrokeWidth = w; }
        float H = (float)f.Height, W = (float)f.Width;
        Fill(SignalFace.MastColor);
        c.DrawRect(-0.06f, H, 0.06f, (float)f.TotalHeight, p);
        if (f.MastPlate)
        {
            var top = H + (float)SignalFace.PlateGap;
            float w = (float)SignalFace.PlateWidth, ph = (float)SignalFace.PlateHeight;
            Fill(SignalFace.PlateWhite);
            c.DrawRect(-w / 2, top, w / 2, top + ph, p);
            Fill(SignalFace.PlateRed);
            for (var i = 1; i <= 3; i += 2) c.DrawRect(-w / 2, top + ph * i / 5, w / 2, top + ph * (i + 1) / 5, p);
            Stroke(SignalFace.ShieldColor, line);
            c.DrawRect(-w / 2, top, w / 2, top + ph, p);
        }
        var shield = new RectF(-W / 2, 0, W / 2, H);
        var corner = (float)f.Corner;
        Fill(SignalFace.ShieldColor);
        c.DrawRoundRect(shield, corner, corner, p);
        Stroke(new Rgb(0x55, 0x55, 0x55), line);
        c.DrawRoundRect(shield, corner, corner, p);
        foreach (var l in f.Lamps)
        {
            float x = (float)l.X, y = (float)l.Y, r = (float)l.R;
            if (l.Lit) { Fill(l.Color, 0x50); c.DrawCircle(x, y, r * 1.45f, p); }
            Fill(SignalFace.Shown(l));
            c.DrawCircle(x, y, r, p);
            Stroke(new Rgb(0x60, 0x60, 0x60), line * 0.8f);
            c.DrawCircle(x, y, r, p);
            if (l.Lit) { Fill(new Rgb(0xFF, 0xFF, 0xFF), 0x90); c.DrawCircle(x - r * 0.3f, y - r * 0.3f, r * 0.28f, p); }
        }
        if (selection > 0)
        {
            p.SetStyle(Paint.Style.Stroke);
            p.Color = Ui.Accent;
            p.StrokeWidth = selection;
            c.DrawRoundRect(new RectF(-W / 2 - 0.12f, -0.12f, W / 2 + 0.12f, (float)f.TotalHeight + 0.12f), corner + 0.1f, corner + 0.1f, p);
        }
    }

    /// <summary>Signalbild als kleines Symbol, eingepasst in <paramref name="width"/> × <paramref name="height"/> (gleicher Maßstab für alle Arten).</summary>
    public static void SignalIcon(Canvas c, SignalType type, float width, float height)
    {
        var face = SignalFace.For(type);
        var scale = height / 4.3f;
        c.Save();
        c.Translate(width / 2, height - scale * (float)(face.TotalHeight + 0.1));
        c.Scale(scale, scale);
        DrawSignalFace(c, face, 0, 1.2f / scale);
        c.Restore();
    }

    /// <summary>Vorschaubild eines Katalogteils für die Bibliothek, eingepasst in <paramref name="size"/>.</summary>
    public static void Thumbnail(Canvas c, TrackPieceDef def, float width, float height)
    {
        var gauge = Catalog.GetGauge(def.GaugeId).TrackWidth;
        var legs = TrackGeometry.LocalLegs(def, false);
        var pts = legs.SelectMany(l => Enumerable.Range(0, 9).Select(i => l.At(i / 8.0))).ToList();
        double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y) - gauge * 1.2, y1 = pts.Max(p => p.Y) + gauge * 1.2;
        var span = Math.Max(Math.Max(x1 - x0, (y1 - y0) * width / height), gauge * 8);
        var scale = (float)(width * 0.86 / span);
        c.Save();
        c.Translate(width / 2, height / 2);
        c.Scale(scale, scale);
        c.Translate((float)-(x0 + x1) / 2, (float)-(y0 + y1) / 2);
        using var paint = new Paint(PaintFlags.AntiAlias);
        if (def.IsTable && TableShape.Of(new PlacedPiece { Key = def.Key }) is { } t)
        {
            paint.SetStyle(Paint.Style.Fill);
            paint.Color = PitFill;
            if (t.Round) c.DrawCircle((float)t.Center.X, (float)t.Center.Y, (float)t.RimRadius, paint);
            else c.DrawPath(Polygon(t.Pit), paint);
            var (a, b) = (t.BridgeA, t.BridgeB);
            legs = [.. legs, new Leg(a.X, a.Y, Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI, false, Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)))];
        }
        paint.SetStyle(Paint.Style.Stroke);
        paint.Color = Ui.Bed;
        paint.StrokeWidth = (float)gauge * 2.4f;
        c.DrawPath(Build(legs, 0), paint);
        paint.Color = Ui.Rail;
        paint.StrokeWidth = (float)Math.Max(gauge * 0.18, 1.6 / scale);
        c.DrawPath(Build(legs, gauge / 2, -gauge / 2), paint);
        if (def.Kind == PieceKind.Buffer)
        {
            paint.Color = Ui.Danger;
            paint.StrokeWidth = (float)gauge * 0.5f;
            c.DrawPath(Stops(legs, gauge), paint);
        }
        c.Restore();
    }
}
