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

    /// <summary>Ausstattungsmodell: Grundrisse der Körper von unten nach oben und Dachlinien.</summary>
    private void DrawModel(Canvas c, PlacedModel model, float z)
    {
        foreach (var shape in model.PlanShapes())
        {
            var color = Color.Rgb(shape.Color.R, shape.Color.G, shape.Color.B);
            var dark = shape.Color.Scale(shape.Closed ? 0.7 : 0.6);
            var path = Polygon(shape.Points, shape.Closed);
            if (shape.Closed) c.DrawPath(path, Fill(color));
            c.DrawPath(path, Stroke(Color.Rgb(dark.R, dark.G, dark.B), 1f * Ui.Density / z));
        }
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
    private static readonly Color GroundColor = Color.Rgb(0x79, 0x55, 0x48);
    private static readonly Color IsolationColor = Color.Rgb(0xE0, 0x6C, 0x00);

    /// <summary>Zeichnet alles in Weltkoordinaten; die Canvas ist bereits skaliert. <paramref name="zoom"/> = Pixel je mm.</summary>
    public void Draw(Canvas c, float zoom, bool scenery, IReadOnlySet<int> selection, int? selectedObject, Action<Canvas>? afterBoard = null, bool wires = true)
    {
        var plan = _plan;
        float z = zoom, g = _g;
        var selWidth = Math.Max(2.5f * Ui.Density / z, g * 0.25f);
        DrawRooms(c, z, selectedObject, selWidth);
        var board = BoardPath(plan);
        c.DrawPath(board, Fill(Ui.Board));
        if (plan.Background is { Visible: true, PixelWidth: > 0 } bg) DrawBackground(c, bg);
        c.DrawPath(board, Stroke(Ui.BoardStroke, 1.2f * Ui.Density / z));
        DrawBeams(c, z, selectedObject, selWidth);

        if (scenery)
        {
            foreach (var s in plan.Scenery.Where(s => s.Kind == SceneryKind.Terrain).OrderBy(s => s.Height))
            {
                // Wie auf einer Landkarte: Höhenstufen mit Höhenlinien
                var path = Polygon(s.Points);
                c.DrawPath(path, Fill(TerrainColor(0)));
                var n = 0;
                foreach (var (level, loops) in TerrainField.For(plan, s).Layers())
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
                if (ModelLibrary.Placed(plan, s) is { } model)
                {
                    DrawModel(c, model, z);
                    if (s.Id == selectedObject) c.DrawPath(Polygon(s.Outline()), Stroke(Ui.Accent, selWidth));
                    continue;
                }
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
        DrawObstacles(c, z, selectedObject, selWidth);
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

        DrawLocks(c, z);
        DrawElectrics(c, z, selectedObject);
        if (wires) DrawWires(c, z, selectedObject);
        DrawPowerSources(c, _plan, _g, z, selectedObject, Ui.Density, wires);
        DrawAnnotations(c, plan, z, selectedObject);
    }

    // ---------- Raum, Raumelemente, gesperrte Gleise ----------

    private static readonly Color WallColor = Color.Rgb(0x6E, 0x69, 0x63);
    private static readonly Color FloorColor = Color.Argb(0x2C, 0x9A, 0x8C, 0x78);
    private static readonly Color DoorColor = Color.Rgb(0x8A, 0x5A, 0x2B);
    private static readonly Color WindowFill = Color.Argb(0xB0, 0xC4, 0xE4, 0xF7);
    private static readonly Color WindowStroke = Color.Rgb(0x3F, 0x86, 0xC0);
    private static readonly Color ColumnFill = Color.Argb(0xD0, 0x80, 0x80, 0x80);
    private static readonly Color HeaterFill = Color.Argb(0xC0, 0xEE, 0xA8, 0x96);
    private static readonly Color HeaterStroke = Color.Rgb(0xB0, 0x4A, 0x32);
    private static readonly Color FurnitureFill = Color.Argb(0x90, 0xB8, 0x98, 0x70);
    private static readonly Color FurnitureStroke = Color.Rgb(0x7A, 0x5C, 0x3A);
    private static readonly Color OtherFill = Color.Argb(0x80, 0xA0, 0xA0, 0xA0);
    private static readonly Color DetectorColor = Color.Rgb(0x6B, 0x3F, 0xA0);
    private static readonly Color LockColor = Color.Rgb(0x5C, 0x5C, 0x5C);

    private static readonly Color BeamFill = Color.Argb(0xC8, 0xD4, 0xB4, 0x82);
    private static readonly Color BeamStroke = Color.Rgb(0x8A, 0x6B, 0x4A);

    private static readonly Color PlateFill = Color.Argb(0x50, 0xC8, 0xA8, 0x78);

    /// <summary>Frei gezogene Leisten des Unterbaus (Diagonale, Streben): Holzfarbe, immer sichtbar; darüber durchscheinend aufgelegte Deckplatten.</summary>
    private void DrawBeams(Canvas c, float z, int? selectedObject, float selWidth)
    {
        foreach (var p in _plan.Scenery.Where(s => s.IsPlate))
        {
            var path = Polygon(PlateLayout.Outline(p));
            c.DrawPath(path, Fill(PlateFill));
            c.DrawPath(path, p.Id == selectedObject ? Stroke(Ui.Accent, selWidth) : Stroke(BeamStroke, 1.5f * Ui.Density / z, effect: new DashPathEffect([8 / z, 4 / z], 0)));
        }
        foreach (var b in _plan.Scenery.Where(s => s.IsBeam && s.Points.Count == 2))
        {
            var path = Polygon(BeamLayout.Outline(b));
            c.DrawPath(path, Fill(BeamFill));
            c.DrawPath(path, Stroke(b.Id == selectedObject ? Ui.Accent : BeamStroke, b.Id == selectedObject ? selWidth : 1.2f * Ui.Density / z));
        }
    }

    /// <summary>Räume: Boden und Wände (Wandstärke nach außen), unter der Platte.</summary>
    private void DrawRooms(Canvas c, float z, int? selectedObject, float selWidth)
    {
        foreach (var r in _plan.Scenery.Where(s => s.Kind == SceneryKind.Room && s.Points.Count >= 3))
        {
            var path = Polygon(r.Points);
            var wall = Stroke(WallColor, (float)Math.Max(r.Width * 2, 2 * Ui.Density / z));
            wall.StrokeJoin = Paint.Join.Miter;
            c.DrawPath(path, wall);
            c.DrawPath(path, Fill(Ui.Board));
            c.DrawPath(path, Fill(FloorColor));
            if (r.Id == selectedObject) c.DrawPath(path, Stroke(Ui.Accent, selWidth));
        }
    }

    /// <summary>Türen mit Schwenkbereich, Fenster, Säulen, Heizkörper und Möbel.</summary>
    private void DrawObstacles(Canvas c, float z, int? selectedObject, float selWidth)
    {
        var line = 1.2f * Ui.Density / z;
        foreach (var o in _plan.Scenery.Where(s => s.Kind == SceneryKind.Obstacle))
        {
            float hw = (float)o.Width / 2, hd = (float)o.Depth / 2;
            c.Save();
            c.Translate((float)o.X, (float)o.Y);
            c.Rotate((float)o.Rotation);
            var rect = new Path();
            rect.MoveTo(-hw, -hd); rect.LineTo(hw, -hd); rect.LineTo(hw, hd); rect.LineTo(-hw, hd); rect.Close();
            switch (o.Obstacle)
            {
                case ObstacleType.Tuer:
                {
                    // Angeschlagen links an der Wandseite (oben), schlägt nach unten auf
                    var w = (float)o.Width;
                    var swing = new Path();
                    swing.MoveTo(-hw, -hd);
                    for (var i = 0; i <= 18; i++)
                    {
                        var a = i / 18.0 * Math.PI / 2;
                        swing.LineTo(-hw + (float)(Math.Cos(a) * w), -hd + (float)(Math.Sin(a) * Math.Min(w, o.Depth)));
                    }
                    swing.Close();
                    c.DrawPath(swing, Fill(Color.Argb(0x30, DoorColor.R, DoorColor.G, DoorColor.B)));
                    c.DrawPath(swing, Stroke(DoorColor, line, effect: new DashPathEffect([6 / z, 4 / z], 0)));
                    c.DrawLine(-hw, -hd, -hw, -hd + (float)Math.Min(w, o.Depth), Stroke(DoorColor, Math.Max(3 * Ui.Density / z, 30)));
                    break;
                }
                case ObstacleType.Fenster:
                    c.DrawPath(rect, Fill(WindowFill));
                    c.DrawPath(rect, Stroke(WindowStroke, line));
                    c.DrawLine(-hw, 0, hw, 0, Stroke(WindowStroke, line));
                    break;
                case ObstacleType.Saeule:
                    c.DrawPath(rect, Fill(ColumnFill));
                    c.DrawPath(rect, Stroke(WallColor, line));
                    c.DrawLine(-hw, -hd, hw, hd, Stroke(WallColor, line));
                    c.DrawLine(-hw, hd, hw, -hd, Stroke(WallColor, line));
                    break;
                case ObstacleType.Heizkoerper:
                    c.DrawPath(rect, Fill(HeaterFill));
                    c.DrawPath(rect, Stroke(HeaterStroke, line));
                    var ribs = Math.Max(2, (int)(o.Width / 60));
                    for (var i = 1; i < ribs; i++)
                    {
                        var x = -hw + hw * 2 * i / ribs;
                        c.DrawLine(x, -hd, x, hd, Stroke(HeaterStroke, line * 0.7f));
                    }
                    break;
                case ObstacleType.Moebel:
                    c.DrawPath(rect, Fill(FurnitureFill));
                    c.DrawPath(rect, Stroke(FurnitureStroke, line));
                    break;
                default:
                    c.DrawPath(rect, Fill(OtherFill));
                    c.DrawPath(rect, Stroke(WallColor, line, effect: new DashPathEffect([6 / z, 4 / z], 0)));
                    break;
            }
            if (o.Id == selectedObject) c.DrawPath(rect, Stroke(Ui.Accent, selWidth));
            c.Restore();
        }
    }

    /// <summary>Kleines Schloss auf gesperrten Gleisen.</summary>
    private void DrawLocks(Canvas c, float z)
    {
        var s = 4.5f * Ui.Density / z;
        foreach (var p in _plan.Pieces.Where(p => p.Locked))
        {
            var m = p.Middle();
            float x = (float)m.X, y = (float)m.Y;
            c.DrawCircle(x, y - s * 0.35f, s * 0.6f, Stroke(LockColor, s * 0.32f));
            c.DrawRect(x - s, y - s * 0.2f, x + s, y + s * 1.1f, Fill(LockColor));
            c.DrawCircle(x, y + s * 0.4f, s * 0.22f, Fill(Color.White));
        }
    }

    /// <summary>Lage des Rückmelder-Symbols: neben der Gleismitte, gegenüber der Einspeisung.</summary>
    public static (double X, double Y) DetectorPoint(PlacedPiece p, double gauge) => Electrics.DetectorPoint(p, gauge);

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
            // sitzt direkt auf der Schiene (Gleichstrom) bzw. dem Mittelleiter (Wechselstrom)
            var r = Math.Max(g * 0.35f, 5 * d / z);
            var (fpx, fpy) = Electrics.FeederPoint(plan, p, f);
            var cx = (float)fpx;
            var cy = (float)fpy;
            c.DrawCircle(cx, cy, r, Fill(f.Ground ? GroundColor : FeederColor));
            c.DrawCircle(cx, cy, r, f.Id == selectedObject ? Stroke(Ui.Accent, 2.5f * d / z) : Stroke(Color.White, 1 / z));
            var bolt = new Path();
            bolt.MoveTo(cx + r * 0.15f, cy - r * 0.65f);
            bolt.LineTo(cx - r * 0.3f, cy + r * 0.05f);
            bolt.LineTo(cx + r * 0.25f, cy + r * 0.05f);
            bolt.LineTo(cx - r * 0.15f, cy + r * 0.65f);
            c.DrawPath(bolt, Stroke(Color.White, r * 0.18f, Paint.Cap.Round));
        }
        foreach (var det in plan.Detectors)
        {
            if (plan.Find(det.PieceId) is not { } p) continue;
            var m = p.Middle();
            var (cx, cy) = DetectorPoint(p, g);
            var r = Math.Max(g * 0.85f, 4.5f * d / z);
            c.DrawLine((float)m.X, (float)m.Y, (float)cx, (float)cy, Stroke(DetectorColor, Math.Max(g * 0.15f, 1 / z)));
            c.DrawRect((float)cx - r, (float)cy - r, (float)cx + r, (float)cy + r, Fill(DetectorColor));
            c.DrawRect((float)cx - r, (float)cy - r, (float)cx + r, (float)cy + r, det.Id == selectedObject ? Stroke(Ui.Accent, 2.5f * d / z) : Stroke(Color.White, 1 / z));
            c.DrawCircle((float)cx, (float)cy, r * 0.42f, det.Type == DetectorType.Strom ? Stroke(Color.White, r * 0.22f) : Fill(Color.White));
        }
        foreach (var s in plan.Signals)
        {
            var (x, y, heading) = Electrics.SignalPose(plan, s);
            var face = SignalFace.For(s);
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
    /// Stromquellen als Symbol (Zentrale, Booster, Trafo, Zubehörtrafo) mit gestrichelter Verbindung zu ihren Gleisanschlüssen.
    /// </summary>
    public static void DrawPowerSources(Canvas c, LayoutPlan plan, float g, float z, int? selectedObject, float density, bool links = true)
    {
        var items = PowerSupply.Items(plan).ToList();
        if (items.Count == 0) return;
        using var p = new Paint(PaintFlags.AntiAlias);
        // Verbindung Zentrale/Booster/Trafo → Gleisanschluss: immer rot wie die Bahnstromleitung
        foreach (var src in links ? PowerSupply.TrackSources(plan) : Enumerable.Empty<SceneryItem>())
        {
            var (tx, ty) = PowerSupply.Terminal(src);
            p.SetStyle(Paint.Style.Stroke);
            p.Color = Color.Argb(0xC8, 0xD3, 0x2F, 0x2F);
            p.StrokeWidth = Math.Max(g * 0.18f, 1.3f * density / z);
            p.SetPathEffect(new DashPathEffect([Math.Max(g * 1.2f, 6 / z), Math.Max(g * 0.8f, 4 / z)], 0));
            foreach (var f in PowerSupply.FeedersOf(plan, src).Where(f => f.WireId is null))
                if (plan.Find(f.PieceId) is { } piece)
                {
                    var (fx, fy) = Electrics.FeederPoint(plan, piece, f);
                    // als Pfad, damit die Strichelung greift
                    using var link = Polygon(PowerSupply.LinkPath((tx, ty), (fx, fy), src), close: false);
                    c.DrawPath(link, p);
                }
            p.SetPathEffect(null);
        }
        foreach (var src in items)
            if (PowerSupply.Type(src) == PowerSourceType.Verteiler) DrawDistributor(c, src, z, src.Id == selectedObject, density, p);
            else if (PowerSupply.Type(src) == PowerSourceType.Baustein) DrawModule(c, src, g, z, src.Id == selectedObject, density, p);
            else DrawPowerSymbol(c, src, g, z, src.Id == selectedObject, density, p);
    }

    /// <summary>Decoder-Baustein: Platine mit Typ und Name, Klemmen für Ausgänge bzw. Melder an der Unterkante.</summary>
    private static void DrawModule(Canvas c, SceneryItem src, float g, float z, bool selected, float density, Paint p)
    {
        var m = Decoders.ModelOf(src);
        var feedback = m?.Role == ModuleRole.Melden;
        var body = feedback ? new Rgb(0x0D, 0x47, 0xA1) : new Rgb(0x1B, 0x5E, 0x20);
        float w = (float)src.Width, d = (float)src.Depth, hw = w / 2, hd = d / 2;
        c.Save();
        c.Translate((float)src.X, (float)src.Y);
        c.Rotate((float)src.Rotation);
        var rect = new RectF(-hw, -hd, hw, hd);
        var corner = d * 0.08f;
        p.SetPathEffect(null);
        p.SetStyle(Paint.Style.Fill);
        p.Color = C(body);
        c.DrawRoundRect(rect, corner, corner, p);
        p.SetStyle(Paint.Style.Stroke);
        p.Color = selected ? Ui.Accent : Color.Rgb(0xE0, 0xE0, 0xE0);
        p.StrokeWidth = selected ? 3 * density / z : 1.2f * density / z;
        c.DrawRoundRect(rect, corner, corner, p);
        // Typ groß, Name und Adressen klein
        p.SetStyle(Paint.Style.Fill);
        p.Color = Color.White;
        p.TextAlign = Paint.Align.Center;
        p.SetTypeface(true);
        var title = m?.Short ?? src.Model ?? "";
        p.TextSize = Fit(p, title, d * 0.26f, w * 0.9f);
        c.DrawText(title, 0, -d * 0.12f, p);
        p.SetTypeface(false);
        var info = Decoders.Range(src) is { } r ? $"{src.Name} · {r.First}–{r.Last}" : src.Name;
        p.TextSize = Fit(p, info, d * 0.18f, w * 0.9f);
        c.DrawText(info, 0, d * 0.14f, p);
        // Klemmen
        var n = Math.Clamp(m?.Outputs ?? 0, 0, 16);
        var step = n > 0 ? w / n : w;
        var tr = Math.Min(step * 0.32f, Math.Max(g * 0.45f, 3 * density / z));
        for (var i = 0; i < n; i++)
        {
            var x = -hw + step * (i + 0.5f);
            p.SetStyle(Paint.Style.Fill);
            p.Color = Color.Rgb(0xF2, 0xC0, 0x00);
            c.DrawCircle(x, hd, tr, p);
            p.SetStyle(Paint.Style.Stroke);
            p.Color = Color.Rgb(0x42, 0x42, 0x42);
            p.StrokeWidth = Math.Max(tr * 0.25f, 0.6f / z);
            c.DrawCircle(x, hd, tr, p);
        }
        c.Restore();
    }

    /// <summary>Verteilerleiste: Leiste in Aderfarbe mit Klemmen auf der Mittellinie.</summary>
    private static void DrawDistributor(Canvas c, SceneryItem src, float z, bool selected, float density, Paint p)
    {
        var rgb = ModelBody.ParseColor(PowerSupply.DistributorColor(src)) ?? new Rgb(0x79, 0x55, 0x48);
        var color = C(rgb);
        float w = (float)src.Width, d = (float)src.Depth, hw = w / 2, hd = d / 2;
        c.Save();
        c.Translate((float)src.X, (float)src.Y);
        c.Rotate((float)src.Rotation);
        var body = new RectF(-hw, -hd, hw, hd);
        var corner = d * 0.2f;
        p.SetPathEffect(null);
        p.SetStyle(Paint.Style.Fill);
        p.Color = color;
        c.DrawRoundRect(body, corner, corner, p);
        p.SetStyle(Paint.Style.Stroke);
        p.Color = selected ? Ui.Accent : C(rgb.Scale(0.55));
        p.StrokeWidth = selected ? 3 * density / z : 1.2f * density / z;
        c.DrawRoundRect(body, corner, corner, p);
        var step = w / PowerSupply.Clamps;
        var r = Math.Min(step * 0.3f, d * 0.3f);
        for (var i = 0; i < PowerSupply.Clamps; i++)
        {
            var x = -hw + step * (i + 0.5f);
            p.SetStyle(Paint.Style.Fill);
            p.Color = Color.Rgb(0xE0, 0xE0, 0xE0);
            c.DrawCircle(x, 0, r, p);
            p.SetStyle(Paint.Style.Stroke);
            p.Color = Color.Rgb(0x42, 0x42, 0x42);
            p.StrokeWidth = Math.Max(r * 0.25f, 0.6f / z);
            c.DrawCircle(x, 0, r, p);
            c.DrawLine(x - r * 0.6f, 0, x + r * 0.6f, 0, p);
        }
        c.Restore();
    }

    private static void DrawPowerSymbol(Canvas c, SceneryItem src, float g, float z, bool selected, float density, Paint p)
    {
        var type = PowerSupply.Type(src);
        var color = C(ModelBody.ParseColor(PowerSupply.Color(type)) ?? new Rgb(0x37, 0x47, 0x4F));
        float w = (float)src.Width, d = (float)src.Depth, hw = w / 2, hd = d / 2;
        var ground = PowerSupply.HasGround(type);
        c.Save();
        c.Translate((float)src.X, (float)src.Y);
        c.Rotate((float)src.Rotation);
        var body = new RectF(-hw, -hd, hw, hd);
        var corner = d * 0.12f;
        p.SetPathEffect(null);
        p.SetStyle(Paint.Style.Fill);
        p.Color = color;
        c.DrawRoundRect(body, corner, corner, p);
        p.SetStyle(Paint.Style.Stroke);
        p.Color = selected ? Ui.Accent : Color.White;
        p.StrokeWidth = selected ? 3 * density / z : 1.2f * density / z;
        c.DrawRoundRect(body, corner, corner, p);

        // Kennzeichen links: Bildschirm (Zentrale), Blitz (Booster), Drehknopf (Fahrtrafo), Trafo-Spulen (Zubehör)
        float ix = -hw + d * 0.42f, r = d * 0.26f;
        p.StrokeWidth = Math.Max(d * 0.06f, 1 / z);
        p.Color = Color.White;
        switch (type)
        {
            case PowerSourceType.Zentrale:
                p.SetStyle(Paint.Style.Fill);
                p.Color = Color.Rgb(0xB9, 0xF6, 0xCA);
                c.DrawRect(ix - r, -r * 0.75f, ix + r, r * 0.75f, p);
                break;
            case PowerSourceType.Booster:
                using (var bolt = new Path())
                {
                    bolt.MoveTo(ix + r * 0.2f, -r);
                    bolt.LineTo(ix - r * 0.45f, r * 0.1f);
                    bolt.LineTo(ix + r * 0.35f, r * 0.1f);
                    bolt.LineTo(ix - r * 0.2f, r);
                    c.DrawPath(bolt, p);
                }
                break;
            case PowerSourceType.Fahrtrafo:
                c.DrawCircle(ix, 0, r, p);
                c.DrawLine(ix, 0, ix + r * 0.6f, -r * 0.6f, p);
                break;
            default:
                c.DrawCircle(ix - r * 0.35f, 0, r * 0.6f, p);
                c.DrawCircle(ix + r * 0.35f, 0, r * 0.6f, p);
                break;
        }

        // Art und Name
        var short_ = PowerSupply.Label(src);
        var tx = ix + r + (hw - (ix + r)) / 2;
        var room = hw - (ix + r) - d * 0.08f;
        p.SetStyle(Paint.Style.Fill);
        p.Color = Color.White;
        p.TextAlign = Paint.Align.Center;
        p.SetTypeface(true);
        if (src.Name == short_)
        {
            p.TextSize = Fit(p, short_, d * 0.3f, room);
            c.DrawText(short_, tx, p.TextSize * 0.35f, p);
        }
        else
        {
            p.TextSize = Fit(p, short_, d * 0.24f, room);
            c.DrawText(short_, tx, -d * 0.06f, p);
            p.SetTypeface(false);
            p.TextSize = Fit(p, src.Name, d * 0.22f, room);
            c.DrawText(src.Name, tx, d * 0.3f, p);
        }

        // Anschlussklemmen unten: Zentrale/Booster Bahnstrom (B, rot) und Masse (0, braun), sonst eine Klemme
        var tr = Math.Max(g * 0.55f, 3.5f * density / z);
        void Clamp(float x, Color ring, string? mark)
        {
            p.SetStyle(Paint.Style.Fill);
            p.Color = Color.White;
            c.DrawCircle(x, hd, tr, p);
            p.SetStyle(Paint.Style.Stroke);
            p.Color = ring;
            p.StrokeWidth = Math.Max(tr * 0.35f, 1 / z);
            c.DrawCircle(x, hd, tr, p);
            if (mark is null) return;
            p.SetStyle(Paint.Style.Fill);
            p.SetTypeface(true);
            p.TextAlign = Paint.Align.Center;
            p.TextSize = tr * 1.25f;
            c.DrawText(mark, x, hd + tr * 0.45f, p);
        }
        if (ground)
        {
            Clamp(-w / 5, Color.Rgb(0xD3, 0x2F, 0x2F), "B");
            Clamp(w / 5, Color.Rgb(0x79, 0x55, 0x48), "0");
        }
        else Clamp(0, color, null);
        c.Restore();
    }

    /// <summary>Schriftgröße, bei der der Text in die Breite passt.</summary>
    private static float Fit(Paint p, string text, float size, float width)
    {
        p.TextSize = size;
        var m = p.MeasureText(text);
        return m > width && m > 0 ? size * width / m : size;
    }

    /// <summary>Selbst verlegte Kabel: Linienzug in der Aderfarbe mit dunkler Kante, Enden als Punkte.</summary>
    private void DrawWires(Canvas c, float z, int? selectedObject)
    {
        var d = Ui.Density;
        var current = Electrics.Current(_plan);
        var width = Math.Max(_g * 0.35f, 2.2f * d / z);
        foreach (var w in _plan.Scenery.Where(s => s.IsWire && s.Points.Count >= 2))
        {
            var rgb = ModelBody.ParseColor(WireLayout.ColorOf(w, current)) ?? new Rgb(0x21, 0x21, 0x21);
            var path = Polygon(w.Points, close: false);
            if (w.Id == selectedObject) c.DrawPath(path, Stroke(Ui.Accent, width + 5 * d / z, Paint.Cap.Round));
            c.DrawPath(path, Stroke(C(rgb.Scale(0.55)), width + 1.4f * d / z, Paint.Cap.Round));
            c.DrawPath(path, Stroke(C(rgb), width, Paint.Cap.Round));
            foreach (var (x, y) in new[] { w.Points[0], w.Points[^1] })
            {
                c.DrawCircle((float)x, (float)y, width * 1.2f, Fill(C(rgb)));
                c.DrawCircle((float)x, (float)y, width * 1.2f, Stroke(Color.White, 1 * d / z));
            }
        }
    }

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
        void Part(SignalPart part)
        {
            using var path = Polygon(part.Points);
            Fill(part.Fill);
            c.DrawPath(path, p);
        }
        foreach (var part in f.BackParts) Part(part);
        if (f.Shield)
        {
            Fill(SignalFace.ShieldColor);
            c.DrawRoundRect(shield, corner, corner, p);
            Stroke(new Rgb(0x55, 0x55, 0x55), line);
            c.DrawRoundRect(shield, corner, corner, p);
        }
        foreach (var part in f.FrontParts) Part(part);
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
    public static void SignalIcon(Canvas c, SignalType type, float width, float height, SignalSystem system = SignalSystem.HV)
    {
        var face = SignalFace.For(type, system);
        var scale = height / 4.3f;
        c.Save();
        c.Translate(width / 2, height - scale * (float)(face.TotalHeight + 0.1));
        c.Scale(scale, scale);
        DrawSignalFace(c, face, 0, 1.2f / scale);
        c.Restore();
    }

    /// <summary>Hintergrundbild maßstäblich unter dem Plan.</summary>
    private static void DrawBackground(Canvas c, PlanBackground bg)
        => c.DrawImage(bg, (float)bg.X, (float)bg.Y, (float)(bg.X + bg.Width), (float)(bg.Y + bg.Height), (float)bg.Opacity);

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
