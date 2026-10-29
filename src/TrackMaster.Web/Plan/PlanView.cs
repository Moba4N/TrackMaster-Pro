using TrackMaster.Core;
using Path = TrackMaster.Web.Graphics.Path;
using TrackMaster.Web.Graphics;
using TrackMaster.Web.Interop;

namespace TrackMaster.Web.Plan;

/// <summary>
/// Zeichenfläche im Browser. Maus wie unter Windows (Klick wählt, Ziehen verschiebt die Auswahl oder zieht einen Rahmen auf,
/// rechte/mittlere Taste oder Leertaste verschiebt die Ansicht, Mausrad zoomt, Strg+Mausrad dreht), Finger wie unter Android
/// (Tippen wählt, Ziehen bewegt Auswahl oder Ansicht, zwei Finger zoomen/drehen, langes Drücken, Doppeltippen).
/// Gezeichnet wird mit dem Zeichenprotokoll aus <see cref="Canvas"/>; JavaScript überträgt es auf das &lt;canvas&gt;.
/// </summary>
public sealed class PlanView
{
    public const string ElementId = "plan-canvas";

    private readonly PlanSession _session;
    private float _ox, _oy, _zoom = 0.4f;
    private double _rot;
    private bool _fitted;
    private PlanPainter? _painter;
    private float _gauge;
    private readonly Paint _p = new(PaintFlags.AntiAlias);
    private readonly Random _random = new(7);
    private HashSet<int> _known = [];
    private bool _framePending;

    public float Width { get; private set; }
    public float Height { get; private set; }

    private enum DragMode { None, Pan, Move, Band, Building, FlexDraw, FlexEnd }
    private DragMode _drag;
    private float _downX, _downY;
    private (double X, double Y) _downWorld, _lastWorld;
    private float _panStartX, _panStartY;
    private bool _moved;
    private bool _touch;
    private bool _additive;
    private RectF? _band;
    private readonly List<(double X, double Y)> _polygon = [];
    private (double X, double Y)? _dimStart;
    private FlexDraft? _flexDraft;
    private ConnectorRef? _flexEnd;
    private bool _reshaping;
    private IReadOnlyList<Segment> _reshapeLast = [];

    /// <summary>Doppelklick auf ein Gleis oder Objekt: Eigenschaften zeigen.</summary>
    public event Action? PropertiesRequested;
    /// <summary>Das Vieleck-Werkzeug hat Punkte gesetzt (Leiste „Fertig/Abbrechen“ zeigen).</summary>
    public event Action? PolygonChanged;
    /// <summary>Werkzeug Beschriftung: Klick auf diese Stelle; die Seite fragt den Text ab.</summary>
    public event Action<double, double>? LabelRequested;
    /// <summary>Zoom oder Drehung der Ansicht geändert (Statuszeile).</summary>
    public event Action? ViewChanged;

    public PlanView(PlanSession session)
    {
        _session = session;
        session.Changed += () => { _painter = null; Invalidate(); };
    }

    public float Zoom => _zoom;
    public double ViewRotation => _rot;

    /// <summary>Zuletzt bekannte Mausposition in Plankoordinaten (für „Einfügen“).</summary>
    public (double X, double Y)? Cursor { get; private set; }

    // ---------- Ansicht ----------

    private (double C, double S) Turn() => (Math.Cos(_rot * Math.PI / 180), Math.Sin(_rot * Math.PI / 180));

    public (double X, double Y) ToWorld(float sx, float sy)
    {
        var (c, s) = Turn();
        double dx = (sx - _ox) / _zoom, dy = (sy - _oy) / _zoom;
        return (dx * c + dy * s, -dx * s + dy * c);
    }

    public (float X, float Y) ToScreen(double x, double y)
    {
        var (c, s) = Turn();
        return ((float)((x * c - y * s) * _zoom + _ox), (float)((x * s + y * c) * _zoom + _oy));
    }

    private void Anchor(double wx, double wy, float px, float py)
    {
        var (c, s) = Turn();
        _ox = (float)(px - (wx * c - wy * s) * _zoom);
        _oy = (float)(py - (wx * s + wy * c) * _zoom);
    }

    public void SetViewRotation(double degrees, float? px = null, float? py = null)
    {
        var d = degrees % 360;
        if (d > 180) d -= 360;
        if (d <= -180) d += 360;
        if (Math.Abs(d) < 0.05) d = 0;
        float ax = px ?? Width / 2f, ay = py ?? Height / 2f;
        var w = ToWorld(ax, ay);
        _rot = d;
        Anchor(w.X, w.Y, ax, ay);
        Invalidate();
        ViewChanged?.Invoke();
    }

    public void RotateViewBy(double degrees, float? px = null, float? py = null) => SetViewRotation(_rot + degrees, px, py);
    public (double X, double Y) CenterWorld() => ToWorld(Width / 2f, Height / 2f);

    public void Resize(float w, float h)
    {
        if (w <= 0 || h <= 0) return;
        if (_fitted && Width > 0)
        {
            // Mitte beim Ändern der Fenstergröße beibehalten
            var center = CenterWorld();
            Width = w;
            Height = h;
            Anchor(center.X, center.Y, w / 2, h / 2);
        }
        else
        {
            Width = w;
            Height = h;
        }
        if (!_fitted) { _fitted = true; Fit(); }
        Invalidate();
    }

    public void Fit()
    {
        if (Width <= 0) return;
        var (x0, y0, x1, y1) = _session.Plan.Bounds();
        var pad = Ui.Dp(48);
        var (c, s) = Turn();
        double rx0 = double.MaxValue, ry0 = double.MaxValue, rx1 = double.MinValue, ry1 = double.MinValue;
        foreach (var (px, py) in new[] { (x0, y0), (x1, y0), (x0, y1), (x1, y1) })
        {
            double rx = px * c - py * s, ry = px * s + py * c;
            rx0 = Math.Min(rx0, rx); rx1 = Math.Max(rx1, rx); ry0 = Math.Min(ry0, ry); ry1 = Math.Max(ry1, ry);
        }
        double w = Math.Max(rx1 - rx0, 1), h = Math.Max(ry1 - ry0, 1);
        _zoom = (float)Math.Max(0.01, Math.Min((Width - 2 * pad) / w, (Height - 2 * pad) / h));
        Anchor((x0 + x1) / 2, (y0 + y1) / 2, Width / 2f, Height / 2f);
        Invalidate();
        ViewChanged?.Invoke();
    }

    public void ZoomBy(float factor, float? fx = null, float? fy = null)
    {
        var px = fx ?? Width / 2f;
        var py = fy ?? Height / 2f;
        var w = ToWorld(px, py);
        _zoom = Math.Clamp(_zoom * factor, 0.01f, 40f);
        Anchor(w.X, w.Y, px, py);
        Invalidate();
        ViewChanged?.Invoke();
    }

    public void Reveal(double x, double y)
    {
        var s = ToScreen(x, y);
        var m = Ui.Dp(40);
        if (s.X < m || s.Y < m || s.X > Width - m || s.Y > Height - m)
        {
            Anchor(x, y, Width / 2f, Height / 2f);
            Invalidate();
        }
    }

    // ---------- Vieleck (Höhenfläche, Gewässer) ----------

    public bool IsDrawingPolygon => _polygon.Count > 0;
    public int PolygonPoints => _polygon.Count;

    public void FinishPolygon()
    {
        if (_polygon.Count < 3)
        {
            _session.Say(Lang.T("Eine Fläche braucht mindestens drei Punkte."));
            return;
        }
        var water = _session.Tool == CanvasTool.Water;
        _session.AddScenery(new SceneryItem
        {
            Kind = water ? SceneryKind.Water : SceneryKind.Terrain,
            Points = [.. _polygon],
            Height = water ? 10 : Math.Round(_gauge * 4 / 5) * 5,
        });
        _polygon.Clear();
        PolygonChanged?.Invoke();
        _session.Say(water ? Lang.T("Gewässer angelegt. Tiefe und Name stehen in den Eigenschaften.") : Lang.T("Höhenfläche angelegt. Höhe und Name stehen in den Eigenschaften."));
        Invalidate();
    }

    public void CancelPolygon()
    {
        _polygon.Clear();
        _dimStart = null;
        PolygonChanged?.Invoke();
        Invalidate();
    }

    public void UndoPolygonPoint()
    {
        if (_polygon.Count > 0) _polygon.RemoveAt(_polygon.Count - 1);
        PolygonChanged?.Invoke();
        Invalidate();
    }

    // ---------- Zeichnen ----------

    /// <summary>Neu zeichnen, sobald der Browser das nächste Bild malt.</summary>
    public void Invalidate()
    {
        if (_framePending) return;
        _framePending = true;
        Js.RequestFrame(ElementId);
    }

    /// <summary>Darstellung geändert (Design): Pfade und Farben neu aufbauen.</summary>
    public void Rebuild()
    {
        _painter = null;
        Invalidate();
    }

    /// <summary>Wird vom Browser vor dem nächsten Bild aufgerufen.</summary>
    public void Render()
    {
        _framePending = false;
        if (Width <= 0 || Height <= 0) return;
        var canvas = new Canvas(Width, Height, _known);
        OnDraw(canvas);
        _known = canvas.UsedPaths;
        Js.Draw(ElementId, canvas);
    }

    private PlanPainter Painter()
    {
        if (_painter is not null) return _painter;
        _gauge = (float)_session.Plan.Gauge.TrackWidth;
        return _painter = new PlanPainter(_session.Plan, _session.Selection, _session.ShowSections);
    }

    private void OnDraw(Canvas canvas)
    {
        var plan = _session.Plan;
        var painter = Painter();
        canvas.DrawColor(Ui.CanvasBack);
        canvas.Save();
        canvas.Translate(_ox, _oy);
        canvas.Rotate((float)_rot);
        canvas.Scale(_zoom, _zoom);
        painter.Draw(canvas, _zoom, _session.ShowScenery, _session.Selection, _session.SelectedObject, c => { if (_session.ShowGrid) DrawGrid(c, plan); });
        DrawToolPreview(canvas);
        canvas.Restore();

        DrawDimensions(canvas, plan);
        if (_session.ShowLabels) DrawLabels(canvas, plan);
        if (_session.ShowHeights) DrawHeights(canvas, plan);
        if (_session.ShowScenery) DrawSceneryNames(canvas, plan);
        DrawSignalNames(canvas, plan);
        DrawConnectors(canvas);
        DrawIssues(canvas);
        if (_band is { } band)
        {
            canvas.DrawRect(band, Fill(Ui.Band));
            canvas.DrawRect(band, Stroke(Ui.Accent, Ui.Dp(1)));
        }
        DrawCompass(canvas);
    }

    /// <summary>Kompass oben rechts, solange die Ansicht gedreht ist; ein Klick darauf richtet sie wieder gerade aus.</summary>
    private (float X, float Y) CompassCenter => (Width - Ui.Dp(36), Ui.Dp(36));

    private bool HitCompass(float x, float y)
    {
        if (_rot == 0) return false;
        var c = CompassCenter;
        return Math.Abs(x - c.X) < Ui.Dp(26) && Math.Abs(y - c.Y) < Ui.Dp(26);
    }

    private void DrawCompass(Canvas canvas)
    {
        if (_rot == 0) return;
        var c = CompassCenter;
        var r = Ui.Dp(22);
        canvas.DrawCircle(c.X, c.Y, r, Fill(Ui.Panel));
        canvas.DrawCircle(c.X, c.Y, r, Stroke(Ui.TextSecondary, Ui.Density));
        canvas.Save();
        canvas.Rotate((float)_rot, c.X, c.Y);
        var north = new Path();
        north.MoveTo(c.X, c.Y - Ui.Dp(16));
        north.LineTo(c.X + Ui.Dp(6), c.Y + Ui.Dp(2));
        north.LineTo(c.X - Ui.Dp(6), c.Y + Ui.Dp(2));
        north.Close();
        canvas.DrawPath(north, Fill(Ui.Accent));
        canvas.DrawLine(c.X, c.Y + Ui.Dp(2), c.X, c.Y + Ui.Dp(14), Stroke(Ui.TextSecondary, Ui.Dp(2)));
        canvas.Restore();
        var paint = TextPaint(10.5f, Ui.CanvasText);
        paint.TextAlign = Paint.Align.Center;
        canvas.DrawText($"{Fmt.Num(_rot, 0)}°", c.X, c.Y + r + Ui.Dp(14), paint);
    }

    private Paint Stroke(Color c, float width)
    {
        _p.Reset();
        _p.SetStyle(Paint.Style.Stroke);
        _p.Color = c;
        _p.StrokeWidth = width;
        _p.StrokeCap = Paint.Cap.Round;
        _p.StrokeJoin = Paint.Join.Round;
        return _p;
    }

    private Paint Fill(Color c)
    {
        _p.Reset();
        _p.SetStyle(Paint.Style.Fill);
        _p.Color = c;
        return _p;
    }

    private Paint TextPaint(float size, Color color, bool bold = false)
    {
        _p.Reset();
        _p.Color = color;
        _p.TextSize = size * (float)Ui.FontScale;
        _p.FakeBoldText = bold;
        return _p;
    }

    private void DrawToolPreview(Canvas c)
    {
        var z = _zoom;
        if (_polygon.Count > 0)
        {
            var path = PlanPainter.Polygon(_polygon);
            c.DrawPath(path, Fill(_session.Tool == CanvasTool.Water ? Color.Argb(0x60, 0x86, 0xBC, 0xE6) : Color.Argb(0x60, 0x9C, 0xB8, 0x7E)));
            c.DrawPath(path, Stroke(Ui.Accent, 1.5f * Ui.Density / z));
            if (!_touch && Cursor is { } cur && _drag == DragMode.None)
            {
                var last = _polygon[^1];
                c.DrawLine((float)last.X, (float)last.Y, (float)cur.X, (float)cur.Y, Stroke(Ui.Accent, 1.2f * Ui.Density / z));
            }
            foreach (var p in _polygon)
            {
                c.DrawCircle((float)p.X, (float)p.Y, 5 * Ui.Density / z, Fill(Color.White));
                c.DrawCircle((float)p.X, (float)p.Y, 5 * Ui.Density / z, Stroke(Ui.Accent, 2 * Ui.Density / z));
            }
        }
        if (_dimStart is { } a)
        {
            c.DrawCircle((float)a.X, (float)a.Y, 7 * Ui.Density / z, Stroke(Ui.Accent, 2.5f * Ui.Density / z));
            if (!_touch && Cursor is { } cur)
                c.DrawLine((float)a.X, (float)a.Y, (float)cur.X, (float)cur.Y, Stroke(Ui.Accent, 1.2f * Ui.Density / z));
        }
        if (_flexDraft is { Segments.Count: > 0 } draft)
        {
            var path = PlanPainter.Build(draft.Legs, 0);
            c.DrawPath(path, Stroke(Color.Argb(0x70, 0xE0, 0x8A, 0x2E), Math.Max(_gauge * 2.6f, 6 * Ui.Density / z)));
            c.DrawPath(path, Stroke(Ui.Accent, 2 * Ui.Density / z));
            if (draft.Target is { } t) c.DrawCircle((float)t.Pose.X, (float)t.Pose.Y, 14 * Ui.Density / z, Stroke(Ui.Accent, 2.5f * Ui.Density / z));
        }
        if (_drag == DragMode.Building && _moved)
        {
            var r = new RectF((float)Math.Min(_downWorld.X, _lastWorld.X), (float)Math.Min(_downWorld.Y, _lastWorld.Y), (float)Math.Max(_downWorld.X, _lastWorld.X), (float)Math.Max(_downWorld.Y, _lastWorld.Y));
            c.DrawRect(r, Fill(Color.Argb(0x80, 0xB8, 0x5C, 0x45)));
            c.DrawRect(r, Stroke(Ui.Accent, 1.5f * Ui.Density / z));
        }
    }

    private void DrawGrid(Canvas c, LayoutPlan plan)
    {
        var corners = new[] { ToWorld(0, 0), ToWorld(Width, 0), ToWorld(0, Height), ToWorld(Width, Height) };
        double vx0 = corners.Min(p => p.X), vy0 = corners.Min(p => p.Y), vx1 = corners.Max(p => p.X), vy1 = corners.Max(p => p.Y);
        double x0 = Math.Max(vx0, 0), y0 = Math.Max(vy0, 0), x1 = Math.Min(vx1, plan.BoardWidth), y1 = Math.Min(vy1, plan.BoardHeight);
        if (x1 <= x0 || y1 <= y0) return;
        void Lines(double step, Color color)
        {
            var path = new Path();
            for (var x = Math.Ceiling(x0 / step) * step; x <= x1; x += step) { path.MoveTo((float)x, (float)y0); path.LineTo((float)x, (float)y1); }
            for (var y = Math.Ceiling(y0 / step) * step; y <= y1; y += step) { path.MoveTo((float)x0, (float)y); path.LineTo((float)x1, (float)y); }
            var paint = Stroke(color, 1 / _zoom);
            paint.StrokeCap = Paint.Cap.Butt;
            c.DrawPath(path, paint);
        }
        c.Save();
        c.ClipPath(PlanPainter.BoardPath(plan));
        if (10 * _zoom >= 8 * Ui.Density) Lines(10, Ui.GridMinor);
        if (100 * _zoom >= 10 * Ui.Density) Lines(100, Ui.GridMajor);
        c.Restore();
    }

    private static readonly Color TagBack = Color.Argb(0xD8, 0xFF, 0xFF, 0xFF);

    private void DrawTag(Canvas c, string text, float cx, float cy, Color? color = null, float size = 11)
        => c.DrawTag(text, cx, cy, size * (float)Ui.FontScale, color ?? Ui.Text, TagBack, Ui.Dp(3));

    private void DrawDimensions(Canvas c, LayoutPlan plan)
    {
        var center = ToScreen(plan.BoardWidth / 2, plan.BoardHeight / 2);
        Dimension(c, ToScreen(0, 0), ToScreen(plan.BoardWidth, 0), plan.BoardWidth, center);
        Dimension(c, ToScreen(0, 0), ToScreen(0, plan.BoardHeight), plan.BoardHeight, center);
    }

    /// <summary>Maßlinie entlang einer Plattenkante, nach außen versetzt; die Schrift bleibt lesbar.</summary>
    private void Dimension(Canvas c, (float X, float Y) a, (float X, float Y) b, double length, (float X, float Y) center)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        var len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        float nx = -dy / len, ny = dx / len;
        float mx = a.X + dx / 2, my = a.Y + dy / 2;
        if ((mx - center.X) * nx + (my - center.Y) * ny < 0) { nx = -nx; ny = -ny; }
        float d = Ui.Dp(14), t = Ui.Dp(4);
        float ax = a.X + nx * d, ay = a.Y + ny * d, bx = b.X + nx * d, by = b.Y + ny * d;
        var pen = Stroke(Ui.CanvasText, Ui.Density);
        c.DrawLine(ax, ay, bx, by, pen);
        c.DrawLine(ax - nx * t, ay - ny * t, ax + nx * t, ay + ny * t, pen);
        c.DrawLine(bx - nx * t, by - ny * t, bx + nx * t, by + ny * t, pen);
        var angle = MathF.Atan2(dy, dx) * 180 / MathF.PI;
        if (angle > 90) angle -= 180;
        if (angle < -90) angle += 180;
        c.Save();
        c.Rotate(angle, mx + nx * d, my + ny * d);
        c.DrawTag($"{Fmt.Num(length, 0)} mm", mx + nx * d, my + ny * d, 11 * (float)Ui.FontScale, Ui.CanvasText, Ui.CanvasBack, Ui.Dp(3));
        c.Restore();
    }

    private void DrawLabels(Canvas c, LayoutPlan plan)
    {
        foreach (var p in plan.Pieces)
        {
            var m = p.Middle();
            var s = ToScreen(m.X, m.Y);
            DrawTag(c, p.Def.Number + (p.Address is { } a ? $" · {a}" : ""), s.X, s.Y - _gauge * 1.6f * _zoom - Ui.Dp(9));
        }
    }

    private void DrawHeights(Canvas c, LayoutPlan plan)
    {
        foreach (var p in plan.Pieces)
        {
            var g = p.EffectiveGrade;
            if (Math.Abs(p.Height) < 0.5 && Math.Abs(g) < 1e-6) continue;
            if (p.Def.TrackLength * _zoom < Ui.Dp(70)) continue;
            var m = p.Middle();
            var s = ToScreen(m.X, m.Y);
            var mid = p.HeightAt(0, 0.5);
            var text = Math.Abs(g) < 1e-6 ? $"{Fmt.Num(mid, 0)} mm" : $"{Fmt.Num(mid, 0)} mm · {(g > 0 ? "↗" : "↘")} {Fmt.Num(Math.Abs(g) * 100, 1)} %";
            DrawTag(c, text, s.X, s.Y + _gauge * 1.6f * _zoom + Ui.Dp(9), Math.Abs(g) * 100 > PlanCheck.WarnGradePercent ? Ui.Danger : Ui.Accent, 10);
        }
    }

    private void DrawSceneryNames(Canvas c, LayoutPlan plan)
    {
        foreach (var s in plan.Scenery.Where(s => s.Kind != SceneryKind.Tree && !s.IsAnnotation))
        {
            var o = s.Outline();
            if (o.Count == 0 || (o.Max(p => p.X) - o.Min(p => p.X)) * _zoom < Ui.Dp(60)) continue;
            var (cx, cy) = s.Center();
            var p = ToScreen(cx, cy);
            DrawTag(c, s.Kind == SceneryKind.Water ? s.Name : $"{s.Name} · {Fmt.Num(s.Height, 0)} mm", p.X, p.Y, Ui.TextSecondary, 10);
        }
    }

    private void DrawSignalNames(Canvas c, LayoutPlan plan)
    {
        if (_zoom < 0.25f * Ui.Density) return;
        foreach (var s in plan.Signals.Where(s => s.Name.Length > 0))
        {
            var (x, y, _) = Electrics.SignalPose(plan, s);
            var p = ToScreen(x, y);
            DrawTag(c, s.Name + (s.Address is { } a ? $" ({a})" : ""), p.X, p.Y - Ui.Dp(15), Ui.Text, 10);
        }
    }

    private void DrawConnectors(Canvas c)
    {
        var active = _session.ActiveConnector;
        var tool = _session.Tool;
        var d = Ui.Density;
        foreach (var cn in _session.Connections.All)
        {
            var s = ToScreen(cn.Pose.X, cn.Pose.Y);
            if (!cn.IsOpen)
            {
                if (_session.Selection.Contains(cn.PieceId) || tool is CanvasTool.Isolation or CanvasTool.Signal)
                {
                    c.DrawCircle(s.X, s.Y, 4.5f * d, Fill(Ui.Success));
                    c.DrawCircle(s.X, s.Y, 4.5f * d, Stroke(Color.White, 1.5f * d));
                }
                continue;
            }
            if (cn == active)
            {
                var h = cn.Pose.HeadingRad + _rot * Math.PI / 180;
                float tx = (float)(s.X + Math.Cos(h) * 24 * d), ty = (float)(s.Y + Math.Sin(h) * 24 * d);
                var pen = Stroke(Ui.Accent, 2.5f * d);
                c.DrawLine(s.X, s.Y, tx, ty, pen);
                c.DrawLine(tx, ty, (float)(tx - Math.Cos(h - 0.6) * 8 * d), (float)(ty - Math.Sin(h - 0.6) * 8 * d), pen);
                c.DrawLine(tx, ty, (float)(tx - Math.Cos(h + 0.6) * 8 * d), (float)(ty - Math.Sin(h + 0.6) * 8 * d), pen);
                c.DrawCircle(s.X, s.Y, 8 * d, Fill(Ui.Accent));
                c.DrawCircle(s.X, s.Y, 8 * d, Stroke(Color.White, 2 * d));
            }
            else if (_session.ShowEnds)
            {
                c.DrawCircle(s.X, s.Y, 6 * d, Fill(Ui.OpenEnd));
                c.DrawCircle(s.X, s.Y, 6 * d, Stroke(Color.White, 2 * d));
            }
        }
    }

    private void DrawIssues(Canvas c)
    {
        var d = Ui.Density;
        foreach (var i in _session.Issues.Where(i => i.Level != IssueLevel.Hinweis))
        {
            var s = ToScreen(i.X, i.Y);
            var color = i.Level == IssueLevel.Fehler ? Ui.Danger : Ui.OpenEnd;
            c.DrawCircle(s.X, s.Y, 18 * d, Stroke(color, 2.5f * d));
            c.DrawCircle(s.X + 13 * d, s.Y - 13 * d, 9 * d, Fill(color));
            var t = TextPaint(11, Color.White, true);
            t.TextAlign = Paint.Align.Center;
            c.DrawText("!", s.X + 13 * d, s.Y - 13 * d + 4 * d, t);
        }
    }

    // ---------- Treffer ----------

    private float Reach(float mouse, float finger) => (_touch ? finger : mouse) * Ui.Density;

    private ConnectorRef? HitConnector(float sx, float sy, bool openOnly = true, float? radius = null)
    {
        ConnectorRef? best = null;
        var bestD = radius ?? Reach(12, 22);
        foreach (var c in openOnly ? _session.Connections.Open : _session.Connections.All)
        {
            var s = ToScreen(c.Pose.X, c.Pose.Y);
            var d = MathF.Sqrt((s.X - sx) * (s.X - sx) + (s.Y - sy) * (s.Y - sy));
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    /// <summary>Ende eines Flexgleises zum Nachformen: offene Enden immer, verbundene nur, wenn das Flexgleis gewählt ist.</summary>
    private ConnectorRef? HitFlexEnd(float sx, float sy)
    {
        ConnectorRef? best = null;
        var bestD = Reach(12, 22);
        foreach (var c in _session.Connections.All)
        {
            if (_session.Plan.Find(c.PieceId) is not { Def.IsFlex: true } || !(c.IsOpen || _session.Selection.Contains(c.PieceId))) continue;
            var s = ToScreen(c.Pose.X, c.Pose.Y);
            var d = MathF.Sqrt((s.X - sx) * (s.X - sx) + (s.Y - sy) * (s.Y - sy));
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    private void EndFlex()
    {
        if (_reshaping) _session.EndReshape(_reshapeLast);
        _reshaping = false;
        _reshapeLast = [];
        _flexEnd = null;
        _flexDraft = null;
    }

    private PlacedPiece? HitPiece((double X, double Y) world)
    {
        var tol = Math.Max(_gauge * 1.4, Reach(8, 16) / _zoom);
        PlacedPiece? best = null;
        var bestD = tol;
        foreach (var p in _session.Plan.Pieces)
            foreach (var l in p.WorldLegs())
            {
                var d = l.DistanceTo(world.X, world.Y);
                if (d < bestD - 0.01 || (Math.Abs(d - bestD) < 0.01 && best is not null && p.Height > best.Height)) { bestD = d; best = p; }
            }
        return best ?? TableShape.Hit(_session.Plan, world.X, world.Y);
    }

    private int? HitSmallObject(float sx, float sy, (double X, double Y) world)
    {
        var plan = _session.Plan;
        int? best = null;
        double bestD = Reach(11, 20);
        void Try(int id, double x, double y)
        {
            var s = ToScreen(x, y);
            var d = Math.Sqrt((s.X - sx) * (s.X - sx) + (s.Y - sy) * (s.Y - sy));
            if (d < bestD) { bestD = d; best = id; }
        }
        foreach (var s in plan.Signals)
        {
            var (x, y, heading) = Electrics.SignalPose(plan, s);
            foreach (var (hx, hy) in SignalFace.PlanHitPoints(x, y, heading, SignalFace.PlanScale(_gauge, _zoom, 6 * Ui.Density), SignalFace.For(s.Type))) Try(s.Id, hx, hy);
        }
        foreach (var f in plan.Feeders)
            if (plan.Find(f.PieceId) is { } p)
            {
                var m = p.Middle();
                Try(f.Id, m.X - Math.Sin(m.HeadingRad) * _gauge * 2.2, m.Y + Math.Cos(m.HeadingRad) * _gauge * 2.2);
            }
        foreach (var i in plan.Isolations)
            if (plan.Find(i.PieceId) is { } p && i.Index < p.WorldConnectors().Count)
            {
                var c = p.WorldConnectors()[i.Index];
                Try(i.Id, c.X, c.Y);
            }
        if (best is not null) return best;
        if (plan.Scenery.Where(s => s.IsAnnotation).Reverse().FirstOrDefault(s => s.Contains(world.X, world.Y)) is { } note) return note.Id;
        if (!_session.ShowScenery) return null;
        return plan.Scenery.Where(s => s.Kind is SceneryKind.Tree or SceneryKind.Building).Reverse().FirstOrDefault(s => s.Contains(world.X, world.Y))?.Id;
    }

    private int? HitArea((double X, double Y) world)
        => _session.ShowScenery
            ? _session.Plan.Scenery.Where(s => s.IsArea).OrderByDescending(s => s.Kind == SceneryKind.Water).ThenByDescending(s => s.Height).FirstOrDefault(s => s.Contains(world.X, world.Y))?.Id
            : null;

    // ---------- Eingabe ----------

    private sealed record TouchPoint(float X, float Y);
    private readonly Dictionary<int, TouchPoint> _touches = [];
    private int? _primary;
    private bool _multiTouch;
    private double _pinchDistance, _pinchAngle, _twoTurned;
    private bool _twoRotating;
    private float _pinchX, _pinchY;
    private CancellationTokenSource? _longPress;
    private (double Time, float X, float Y) _lastTap = (-1, 0, 0);
    private bool _spaceDown;

    [Flags]
    public enum Mods { None = 0, Shift = 1, Ctrl = 2, Alt = 4, Space = 8 }

    public void SetSpace(bool down) => _spaceDown = down;

    /// <summary>Zeiger gedrückt. <paramref name="button"/>: 0 links, 1 Mitte, 2 rechts.</summary>
    public void PointerDown(int id, float x, float y, int button, Mods mods, bool touch, double time)
    {
        _touch = touch;
        if (touch)
        {
            _touches[id] = new TouchPoint(x, y);
            if (_touches.Count == 2)
            {
                // Zweiter Finger: laufende Aktion abbrechen, ab jetzt zoomen, verschieben und drehen
                CancelLongPress();
                if (_drag == DragMode.Move) _session.EndMove(Ui.Dp(20) / _zoom);
                EndFlex();
                _drag = DragMode.None;
                _band = null;
                _multiTouch = true;
                BeginPinch();
                Invalidate();
                return;
            }
            if (_touches.Count > 2) return;
            _primary = id;
            _multiTouch = false;
        }
        _downX = x; _downY = y;
        var world = ToWorld(x, y);
        _downWorld = world;
        _lastWorld = world;
        _moved = false;
        _drag = DragMode.None;
        _panStartX = _ox;
        _panStartY = _oy;
        _additive = mods.HasFlag(Mods.Shift) || mods.HasFlag(Mods.Ctrl);
        if (touch) TouchDown(x, y, world, time);
        else MouseDown(x, y, world, button, mods);
    }

    public void PointerMove(int id, float x, float y, Mods mods, bool touch)
    {
        var world = ToWorld(x, y);
        if (touch)
        {
            if (!_touches.ContainsKey(id)) return;
            _touches[id] = new TouchPoint(x, y);
            if (_multiTouch) { Pinch(); return; }
            if (id != _primary) return;
        }
        else
        {
            Cursor = world;
            Js.SetText("status-xy", $"X {Fmt.Num(world.X, 0)} · Y {Fmt.Num(world.Y, 0)} mm");
            if (_drag == DragMode.None && (_polygon.Count > 0 || _dimStart is not null)) Invalidate();
        }
        if (_drag == DragMode.None) return;
        _lastWorld = world;
        var threshold = touch ? Ui.Dp(8) : Ui.Dp(3);
        if (!_moved && Math.Abs(x - _downX) + Math.Abs(y - _downY) < threshold) return;
        if (!_moved) CancelLongPress();
        _moved = true;
        switch (_drag)
        {
            case DragMode.Pan:
                _ox = _panStartX + x - _downX;
                _oy = _panStartY + y - _downY;
                Invalidate();
                break;
            case DragMode.Move:
                _session.MoveBy(world.X - _downWorld.X, world.Y - _downWorld.Y);
                break;
            case DragMode.Band:
                _band = new RectF(Math.Min(_downX, x), Math.Min(_downY, y), Math.Max(_downX, x), Math.Max(_downY, y));
                Invalidate();
                break;
            case DragMode.Building:
                Invalidate();
                break;
            case DragMode.FlexDraw when _flexDraft is { } draft:
                draft.Update(world.X, world.Y, _session.FlexTarget(world.X, world.Y, Reach(14, 26) / _zoom, exclude: draft.From));
                if (draft.Segments.Count > 0) _session.Say(FlexTrack.Describe(draft.Segments) + (draft.Target is not null ? Lang.T(" – rastet am Gleisende ein") : ""));
                Invalidate();
                break;
            case DragMode.FlexEnd when _flexEnd is { } fe:
                if (!_reshaping)
                {
                    if (!_session.BeginReshape(fe.PieceId, fe.Index)) { _drag = touch ? DragMode.Pan : DragMode.None; break; }
                    _reshaping = true;
                }
                var shaped = _session.Reshape(world.X, world.Y, _session.FlexTarget(world.X, world.Y, Reach(14, 26) / _zoom, fe.PieceId));
                if (shaped.Count > 0)
                {
                    _reshapeLast = shaped;
                    _session.Say(FlexTrack.Describe(shaped));
                }
                break;
        }
    }

    public void PointerUp(int id, float x, float y, Mods mods, bool touch, double time)
    {
        var world = ToWorld(x, y);
        if (touch)
        {
            if (!_touches.Remove(id)) return;
            if (_multiTouch)
            {
                if (_touches.Count == 0) _multiTouch = false;
                else BeginPinch();
                return;
            }
            if (id != _primary) return;
            CancelLongPress();
            TouchUp(x, y, world, time);
        }
        else MouseUp(x, y, world);
        _drag = DragMode.None;
    }

    public void PointerCancel(int id)
    {
        _touches.Remove(id);
        CancelLongPress();
        if (_drag == DragMode.Move) _session.EndMove(Ui.Dp(20) / _zoom);
        EndFlex();
        _drag = DragMode.None;
        _band = null;
        if (_touches.Count == 0) _multiTouch = false;
        Invalidate();
    }

    public void PointerLeave()
    {
        Cursor = null;
        Js.SetText("status-xy", "X – · Y –");
    }

    public void Wheel(float x, float y, double deltaY, Mods mods)
    {
        if (mods.HasFlag(Mods.Ctrl))
        {
            // Strg + Mausrad dreht die Ansicht (mit Umschalt in feinen Schritten)
            var step = mods.HasFlag(Mods.Shift) ? 1 : 5;
            RotateViewBy(-Math.Sign(deltaY) * step, x, y);
        }
        else ZoomBy((float)Math.Exp(-deltaY * 0.0015), x, y);
    }

    public void DoubleClick(float x, float y)
    {
        if (_session.Tool is CanvasTool.Terrain or CanvasTool.Water)
        {
            // Der zweite Klick hat schon einen Punkt gesetzt
            if (_polygon.Count > 0) _polygon.RemoveAt(_polygon.Count - 1);
            FinishPolygon();
            return;
        }
        if (_session.Tool != CanvasTool.Select) return;
        var world = ToWorld(x, y);
        if (HitSmallObject(x, y, world) is not null || HitPiece(world) is not null || HitArea(world) is not null)
            PropertiesRequested?.Invoke();
        else
            Fit();
    }

    public void Drop(string key, float x, float y)
    {
        var w = ToWorld(x, y);
        _session.Drop(key, w.X, w.Y, 28 / _zoom);
    }

    // ---------- Maus ----------

    private void MouseDown(float x, float y, (double X, double Y) world, int button, Mods mods)
    {
        if (button == 0 && HitCompass(x, y))
        {
            SetViewRotation(0);
            _session.Say(Lang.T("Ansicht wieder gerade ausgerichtet."));
            return;
        }
        if (button is 1 or 2 || _spaceDown || mods.HasFlag(Mods.Space) || _session.Tool == CanvasTool.Pan)
        {
            _drag = DragMode.Pan;
            return;
        }
        if (button != 0) return;

        switch (_session.Tool)
        {
            case CanvasTool.Terrain:
            case CanvasTool.Water:
                _polygon.Add((Math.Round(world.X), Math.Round(world.Y)));
                PolygonChanged?.Invoke();
                Invalidate();
                return;
            case CanvasTool.Building:
                _drag = DragMode.Building;
                return;
            case CanvasTool.Flex:
                _flexDraft = _session.BeginFlexDraw(world.X, world.Y, _session.FlexTarget(world.X, world.Y, Reach(12, 26) / _zoom));
                _drag = DragMode.FlexDraw;
                return;
            case CanvasTool.Label or CanvasTool.Dimension or CanvasTool.Tree or CanvasTool.Feeder or CanvasTool.Isolation or CanvasTool.Signal:
                ToolTap(x, y, world);
                return;
        }

        if (HitFlexEnd(x, y) is { } fe)
        {
            if (fe.IsOpen) _session.SetActive(fe.PieceId, fe.Index);
            _flexEnd = fe;
            _reshaping = false;
            _drag = DragMode.FlexEnd;
            return;
        }
        if (HitConnector(x, y) is { } conn)
        {
            _session.SetActive(conn.PieceId, conn.Index);
            _drag = DragMode.None;
            return;
        }
        if (HitSmallObject(x, y, world) is { } obj)
        {
            _session.SelectObject(obj);
            _session.BeginMove();
            _drag = DragMode.Move;
            return;
        }
        if (HitPiece(world) is { } piece)
        {
            if (_additive)
            {
                _session.Toggle(piece.Id);
                _drag = DragMode.None;
                return;
            }
            if (!_session.Selection.Contains(piece.Id)) _session.Select([piece.Id], add: false);
            _session.BeginMove();
            _drag = DragMode.Move;
            return;
        }
        if (HitArea(world) is { } area && !mods.HasFlag(Mods.Shift))
        {
            _session.SelectObject(area);
            _session.BeginMove();
            _drag = DragMode.Move;
            return;
        }
        _drag = DragMode.Band;
    }

    private void MouseUp(float x, float y, (double X, double Y) world)
    {
        var mode = _drag;
        _drag = DragMode.None;
        switch (mode)
        {
            case DragMode.Move:
                _session.EndMove(16 / _zoom);
                break;
            case DragMode.Band:
                if (!_moved || _band is not { } band)
                {
                    _session.ClearSelection();
                    break;
                }
                _band = null;
                SelectInBand(band, _additive);
                break;
            case DragMode.Building:
                AddBuilding(world);
                break;
            case DragMode.FlexDraw:
                var draft = _flexDraft;
                _flexDraft = null;
                if (_moved && draft is not null) _session.FinishFlexDraw(draft);
                else _session.Say(Lang.T("Flexgleis ziehen: an einem offenen Gleisende (oder frei) drücken und mit gedrückter Maustaste ziehen."));
                Invalidate();
                break;
            case DragMode.FlexEnd:
                EndFlex();
                break;
        }
    }

    private void SelectInBand(RectF band, bool add)
    {
        var ids = _session.Plan.Pieces.Where(p => { var m = p.Middle(); var sp = ToScreen(m.X, m.Y); return band.Contains(sp.X, sp.Y); }).Select(p => p.Id).ToList();
        _session.Select(ids, add);
        if (ids.Count > 0) _session.Say(Lang.F($"{_session.Selection.Count} Gleise gewählt."));
        Invalidate();
    }

    private void AddBuilding((double X, double Y) world)
    {
        var r = (X0: Math.Min(_downWorld.X, world.X), Y0: Math.Min(_downWorld.Y, world.Y), X1: Math.Max(_downWorld.X, world.X), Y1: Math.Max(_downWorld.Y, world.Y));
        if (!_moved || r.X1 - r.X0 < 5 || r.Y1 - r.Y0 < 5)
            r = (_downWorld.X - _gauge * 6, _downWorld.Y - _gauge * 4, _downWorld.X + _gauge * 6, _downWorld.Y + _gauge * 4);
        _session.AddScenery(new SceneryItem
        {
            Kind = SceneryKind.Building, X = Math.Round((r.X0 + r.X1) / 2), Y = Math.Round((r.Y0 + r.Y1) / 2),
            Width = Math.Round(r.X1 - r.X0), Depth = Math.Round(r.Y1 - r.Y0), Height = Math.Round(_gauge * 6),
        });
        _session.Say(Lang.T("Gebäude gesetzt. Größe, Höhe und Drehung stehen in den Eigenschaften."));
        Invalidate();
    }

    /// <summary>Klick oder Tippen mit einem Werkzeug für Beschriftung, Bäume oder Elektrik.</summary>
    private void ToolTap(float x, float y, (double X, double Y) world)
    {
        switch (_session.Tool)
        {
            case CanvasTool.Terrain:
            case CanvasTool.Water:
                _polygon.Add((Math.Round(world.X), Math.Round(world.Y)));
                PolygonChanged?.Invoke();
                Invalidate();
                return;
            case CanvasTool.Tree:
                var size = _gauge * (2.6 + _random.NextDouble() * 1.4);
                _session.AddScenery(new SceneryItem { Kind = SceneryKind.Tree, X = Math.Round(world.X), Y = Math.Round(world.Y), Width = Math.Round(size), Height = Math.Round(size * 2.2), Name = Lang.T("Baum") });
                return;
            case CanvasTool.Label:
                LabelRequested?.Invoke(Math.Round(world.X), Math.Round(world.Y));
                return;
            case CanvasTool.Dimension:
                var snap = Annotation.Snap(_session.Plan, world.X, world.Y, Reach(12, 22) / _zoom);
                if (_dimStart is not { } from) { _dimStart = snap; _session.Say(Lang.T("Maß: jetzt den zweiten Punkt anklicken (Gleisenden und Plattenecken rasten ein, Esc bricht ab).")); }
                else if (Math.Abs(snap.X - from.X) + Math.Abs(snap.Y - from.Y) > 1) { _dimStart = null; _session.AddDimension(from, snap); }
                Invalidate();
                return;
            case CanvasTool.Feeder:
                // Klick auf eine vorhandene Einspeisung entfernt sie wieder
                if (HitSmallObject(x, y, world) is { } fo && _session.Plan.Feeders.Any(f => f.Id == fo)) _session.RemoveObject(fo);
                else if (HitPiece(world) is { } fp) _session.AddFeeder(fp.Id);
                else _session.Say(Lang.T("Für eine Einspeisung bitte direkt auf ein Gleis klicken."));
                return;
            case CanvasTool.Isolation:
                if (HitConnector(x, y, openOnly: false, radius: Reach(14, 26)) is { } ic) _session.ToggleIsolation(ic.PieceId, ic.Index);
                else _session.Say(Lang.T("Für eine Trennstelle bitte auf eine Gleisverbindung (grüner Punkt) klicken."));
                return;
            case CanvasTool.Signal:
                // Klick auf ein vorhandenes Signal entfernt es wieder
                if (HitSmallObject(x, y, world) is { } so && _session.Plan.Signals.Any(s => s.Id == so)) _session.RemoveObject(so);
                else if (HitConnector(x, y, openOnly: false, radius: Reach(14, 26)) is { } sc) _session.AddSignal(sc.PieceId, sc.Index);
                else _session.Say(Lang.T("Signale stehen an Gleisenden: bitte auf einen Punkt an einer Gleisverbindung klicken."));
                return;
        }
    }

    // ---------- Finger ----------

    private void TouchDown(float x, float y, (double X, double Y) world, double time)
    {
        if (HitCompass(x, y)) { _drag = DragMode.Pan; return; }
        switch (_session.Tool)
        {
            case CanvasTool.Building:
                _drag = DragMode.Building;
                return;
            case CanvasTool.Pan or CanvasTool.Terrain or CanvasTool.Water or CanvasTool.Tree or CanvasTool.Feeder or CanvasTool.Isolation or CanvasTool.Signal or CanvasTool.Label or CanvasTool.Dimension:
                _drag = DragMode.Pan;
                return;
            case CanvasTool.Flex:
                _flexDraft = _session.BeginFlexDraw(world.X, world.Y, _session.FlexTarget(world.X, world.Y, Ui.Dp(26) / _zoom));
                _drag = DragMode.FlexDraw;
                return;
        }
        StartLongPress(x, y);
        if (HitFlexEnd(x, y) is { } fe) { _flexEnd = fe; _reshaping = false; _drag = DragMode.FlexEnd; return; }
        if (HitConnector(x, y) is not null) { _drag = DragMode.Pan; return; }
        if (HitSmallObject(x, y, world) is { } obj && obj == _session.SelectedObject) { _session.BeginMove(); _drag = DragMode.Move; return; }
        if (HitPiece(world) is { } piece && _session.Selection.Contains(piece.Id)) { _session.BeginMove(); _drag = DragMode.Move; return; }
        if (HitArea(world) is { } area && area == _session.SelectedObject) { _session.BeginMove(); _drag = DragMode.Move; return; }
        _drag = DragMode.Pan;
    }

    private void TouchUp(float x, float y, (double X, double Y) world, double time)
    {
        switch (_drag)
        {
            case DragMode.Move:
                if (_moved) _session.EndMove(Ui.Dp(20) / _zoom);
                else Tap(x, y, world, time);
                return;
            case DragMode.Band:
                _band = null;
                if (_moved)
                {
                    var rect = new RectF(Math.Min(_downX, x), Math.Min(_downY, y), Math.Max(_downX, x), Math.Max(_downY, y));
                    SelectInBand(rect, true);
                }
                Invalidate();
                return;
            case DragMode.Building:
                AddBuilding(world);
                return;
            case DragMode.Pan:
                if (!_moved) Tap(x, y, world, time);
                return;
            case DragMode.FlexDraw:
                if (_moved && _flexDraft is { } draft) _session.FinishFlexDraw(draft);
                else _session.Say(Lang.T("Flexgleis ziehen: an einem offenen Gleisende (oder frei) aufsetzen und mit dem Finger ziehen."));
                EndFlex();
                Invalidate();
                return;
            case DragMode.FlexEnd:
                var shaped = _reshaping;
                EndFlex();
                if (!shaped) Tap(x, y, world, time);
                return;
            case DragMode.None when !_moved:
                return;
        }
    }

    private void Tap(float x, float y, (double X, double Y) world, double time)
    {
        // Doppeltippen
        if (time - _lastTap.Time < 320 && Math.Abs(x - _lastTap.X) + Math.Abs(y - _lastTap.Y) < Ui.Dp(30))
        {
            _lastTap = (-1, 0, 0);
            if (_session.Tool == CanvasTool.Select) { DoubleClick(x, y); return; }
        }
        else _lastTap = (time, x, y);

        if (HitCompass(x, y))
        {
            SetViewRotation(0);
            _session.Say(Lang.T("Ansicht wieder gerade ausgerichtet."));
            return;
        }
        if (_session.Tool != CanvasTool.Select) { ToolTap(x, y, world); return; }
        if (HitConnector(x, y) is { } conn) { _session.SetActive(conn.PieceId, conn.Index); return; }
        if (HitSmallObject(x, y, world) is { } obj) { _session.SelectObject(obj); return; }
        if (HitPiece(world) is { } piece) { _session.Select([piece.Id], add: false); return; }
        if (HitArea(world) is { } area) { _session.SelectObject(area); return; }
        _session.ClearSelection();
    }

    private void StartLongPress(float x, float y)
    {
        CancelLongPress();
        var cts = new CancellationTokenSource();
        _longPress = cts;
        // Im Browser gibt es nur einen Thread; die Fortsetzung läuft dort, wo auch die Zeigerereignisse ankommen
        _ = Task.Delay(500, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled || cts.IsCancellationRequested || _moved || _multiTouch) return;
            try { LongPress(x, y); }
            catch (Exception e) { Interop.JsEntry.Host?.Report(e); }
        }, TaskScheduler.Default);
    }

    private void CancelLongPress()
    {
        _longPress?.Cancel();
        _longPress = null;
    }

    private void LongPress(float x, float y)
    {
        if (_session.Tool != CanvasTool.Select || _moved) return;
        var world = ToWorld(x, y);
        Js.Vibrate(20);
        if (HitPiece(world) is { } piece)
        {
            // Langes Drücken: Gleis zur Auswahl hinzufügen oder herausnehmen
            if (_drag == DragMode.Move) _session.EndMove(0);
            _session.Toggle(piece.Id);
            _session.Say(Lang.F($"{_session.Selection.Count} Gleise gewählt. Ziehen verschiebt alle gewählten Gleise."));
            _drag = DragMode.None;
            return;
        }
        // Auf leerer Fläche: Rahmen aufziehen
        _drag = DragMode.Band;
        _session.Say(Lang.T("Rahmen aufziehen: alle Gleise darin werden gewählt."));
    }

    private void BeginPinch()
    {
        var pts = _touches.Values.Take(2).ToList();
        if (pts.Count < 2) return;
        _pinchDistance = Math.Max(1, Math.Sqrt(Math.Pow(pts[1].X - pts[0].X, 2) + Math.Pow(pts[1].Y - pts[0].Y, 2)));
        _pinchAngle = Math.Atan2(pts[1].Y - pts[0].Y, pts[1].X - pts[0].X) * 180 / Math.PI;
        _pinchX = (pts[0].X + pts[1].X) / 2;
        _pinchY = (pts[0].Y + pts[1].Y) / 2;
        _twoTurned = 0;
        _twoRotating = false;
    }

    /// <summary>Zwei Finger: um den Mittelpunkt zoomen und verschieben; drehen erst ab einer deutlichen Drehbewegung.</summary>
    private void Pinch()
    {
        var pts = _touches.Values.Take(2).ToList();
        if (pts.Count < 2) return;
        var dist = Math.Max(1, Math.Sqrt(Math.Pow(pts[1].X - pts[0].X, 2) + Math.Pow(pts[1].Y - pts[0].Y, 2)));
        var angle = Math.Atan2(pts[1].Y - pts[0].Y, pts[1].X - pts[0].X) * 180 / Math.PI;
        float cx = (pts[0].X + pts[1].X) / 2, cy = (pts[0].Y + pts[1].Y) / 2;
        _ox += cx - _pinchX;
        _oy += cy - _pinchY;
        _pinchX = cx;
        _pinchY = cy;
        ZoomBy((float)(dist / _pinchDistance), cx, cy);
        _pinchDistance = dist;
        var delta = angle - _pinchAngle;
        if (delta > 180) delta -= 360;
        if (delta < -180) delta += 360;
        _pinchAngle = angle;
        _twoTurned += delta;
        if (!_twoRotating && Math.Abs(_twoTurned) > 12)
        {
            _twoRotating = true;
            delta = _twoTurned;
        }
        if (_twoRotating) SetViewRotation(_rot + delta, cx, cy);
    }
}
