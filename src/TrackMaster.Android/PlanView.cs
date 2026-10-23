using Android.Content;
using Android.Graphics;
using Android.Views;
using TrackMaster.Core;
using Path = Android.Graphics.Path;

namespace TrackMaster.Mobile;

/// <summary>
/// Zeichenfläche für Touch: Tippen wählt Gleis, Objekt oder Gleisende; Ziehen verschiebt die Auswahl oder die Ansicht;
/// zwei Finger zoomen und verschieben; langes Drücken nimmt ein Gleis zur Auswahl hinzu oder zieht einen Rahmen auf;
/// Doppeltippen öffnet die Eigenschaften (auf leerer Fläche: Plan einpassen). Werkzeuge setzen Gelände und Elektrik.
/// </summary>
public sealed class PlanView : View
{
    private readonly PlanSession _session;
    private float _ox, _oy, _zoom = 0.4f;
    private bool _fitted;
    private PlanPainter? _painter;
    private float _gauge;
    private readonly ScaleGestureDetector _scale;
    private readonly GestureDetector _gestures;
    private readonly Paint _p = new(PaintFlags.AntiAlias);
    private readonly Random _random = new(7);

    private enum DragMode { None, Pan, Move, Band, Building }
    private DragMode _drag;
    private float _downX, _downY;
    private (double X, double Y) _downWorld, _lastWorld;
    private float _panStartX, _panStartY;
    private bool _moved;
    private bool _multiTouch;
    private RectF? _band;
    private readonly List<(double X, double Y)> _polygon = [];

    /// <summary>Doppeltippen auf ein Gleis oder Objekt: Eigenschaften zeigen.</summary>
    public event Action? PropertiesRequested;
    /// <summary>Das Vieleck-Werkzeug hat Punkte gesetzt (Leiste „Fertig/Abbrechen“ zeigen).</summary>
    public event Action? PolygonChanged;

    public PlanView(Context context, PlanSession session) : base(context)
    {
        _session = session;
        session.Changed += () => { _painter = null; Invalidate(); };
        _scale = new ScaleGestureDetector(context, new ScaleListener(this));
        _gestures = new GestureDetector(context, new GestureListener(this));
        _gestures.IsLongpressEnabled = true;
    }

    public float Zoom => _zoom;

    // ---------- Ansicht ----------

    public (double X, double Y) ToWorld(float sx, float sy) => ((sx - _ox) / _zoom, (sy - _oy) / _zoom);
    public PointF ToScreen(double x, double y) => new((float)(x * _zoom + _ox), (float)(y * _zoom + _oy));
    public (double X, double Y) CenterWorld() => ToWorld(Width / 2f, Height / 2f);

    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        if (!_fitted && w > 0) { _fitted = true; Fit(); }
    }

    public void Fit()
    {
        if (Width <= 0) return;
        var (x0, y0, x1, y1) = _session.Plan.Bounds();
        var pad = Ui.Dp(36);
        double w = Math.Max(x1 - x0, 1), h = Math.Max(y1 - y0, 1);
        _zoom = (float)Math.Max(0.01, Math.Min((Width - 2 * pad) / w, (Height - 2 * pad) / h));
        _ox = (float)((Width - w * _zoom) / 2 - x0 * _zoom);
        _oy = (float)((Height - h * _zoom) / 2 - y0 * _zoom);
        Invalidate();
    }

    public void ZoomBy(float factor, float? fx = null, float? fy = null)
    {
        var px = fx ?? Width / 2f;
        var py = fy ?? Height / 2f;
        var nz = Math.Clamp(_zoom * factor, 0.01f, 40f);
        _ox = px - (px - _ox) * nz / _zoom;
        _oy = py - (py - _oy) * nz / _zoom;
        _zoom = nz;
        Invalidate();
    }

    public void Reveal(double x, double y)
    {
        var s = ToScreen(x, y);
        var m = Ui.Dp(40);
        if (s.X < m || s.Y < m || s.X > Width - m || s.Y > Height - m)
        {
            _ox = (float)(Width / 2f - x * _zoom);
            _oy = (float)(Height / 2f - y * _zoom);
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
            _session.Say("Eine Fläche braucht mindestens drei Punkte.");
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
        _session.Say(water ? "Gewässer angelegt. Tiefe und Name stehen in den Eigenschaften." : "Höhenfläche angelegt. Höhe und Name stehen in den Eigenschaften.");
        Invalidate();
    }

    public void CancelPolygon()
    {
        _polygon.Clear();
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

    private PlanPainter Painter()
    {
        if (_painter is not null) return _painter;
        _gauge = (float)_session.Plan.Gauge.TrackWidth;
        return _painter = new PlanPainter(_session.Plan, _session.Selection, _session.ShowSections);
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        var plan = _session.Plan;
        var painter = Painter();
        canvas.DrawColor(Ui.CanvasBack);
        canvas.Save();
        canvas.Translate(_ox, _oy);
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
    }

    private Paint Stroke(Color c, float width)
    {
        _p.Reset();
        _p.AntiAlias = true;
        _p.SetStyle(Paint.Style.Stroke);
        _p.Color = c;
        _p.StrokeWidth = width;
        _p.StrokeCap = Paint.Cap.Round;
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

    private void DrawToolPreview(Canvas c)
    {
        var z = _zoom;
        if (_polygon.Count > 0)
        {
            var path = PlanPainter.Polygon(_polygon);
            c.DrawPath(path, Fill(_session.Tool == CanvasTool.Water ? Color.Argb(0x60, 0x86, 0xBC, 0xE6) : Color.Argb(0x60, 0x9C, 0xB8, 0x7E)));
            c.DrawPath(path, Stroke(Ui.Accent, 1.5f * Ui.Density / z));
            foreach (var p in _polygon)
            {
                c.DrawCircle((float)p.X, (float)p.Y, 6 * Ui.Density / z, Fill(Color.White));
                c.DrawCircle((float)p.X, (float)p.Y, 6 * Ui.Density / z, Stroke(Ui.Accent, 2 * Ui.Density / z));
            }
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
        var (vx0, vy0) = ToWorld(0, 0);
        var (vx1, vy1) = ToWorld(Width, Height);
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
        if (10 * _zoom >= 8 * Ui.Density) Lines(10, Ui.GridMinor);
        if (100 * _zoom >= 10 * Ui.Density) Lines(100, Ui.GridMajor);
    }

    private Paint TextPaint(float sizeSp, Color color, bool bold = false)
    {
        _p.Reset();
        _p.AntiAlias = true;
        _p.Color = color;
        _p.TextSize = sizeSp * Resources!.DisplayMetrics!.ScaledDensity;
        _p.SetTypeface(bold ? Typeface.DefaultBold : Typeface.Default);
        return _p;
    }

    /// <summary>Beschriftung mit hellem Hintergrund, mittig um <paramref name="cx"/>, <paramref name="cy"/>.</summary>
    private void DrawTag(Canvas c, string text, float cx, float cy, Color? color = null, float size = 11)
    {
        var paint = TextPaint(size, color ?? Ui.Text);
        var w = paint.MeasureText(text);
        var fm = paint.GetFontMetrics()!;
        var h = fm.Descent - fm.Ascent;
        var pad = Ui.Dp(3);
        var col = paint.Color;
        var r = new RectF(cx - w / 2 - pad, cy - h / 2, cx + w / 2 + pad, cy + h / 2);
        c.DrawRoundRect(r, pad, pad, Fill(Color.Argb(0xD8, 0xFF, 0xFF, 0xFF)));
        paint = TextPaint(size, col);
        c.DrawText(text, cx - w / 2, cy - h / 2 - fm.Ascent, paint);
    }

    private void DrawDimensions(Canvas c, LayoutPlan plan)
    {
        var tl = ToScreen(0, 0);
        var br = ToScreen(plan.BoardWidth, plan.BoardHeight);
        var d = Ui.Dp(12);
        var t = Ui.Dp(4);
        float y = tl.Y - d, x = tl.X - d;
        var pen = Stroke(Ui.TextSecondary, Ui.Density);
        c.DrawLine(tl.X, y, br.X, y, pen);
        c.DrawLine(tl.X, y - t, tl.X, y + t, pen);
        c.DrawLine(br.X, y - t, br.X, y + t, pen);
        c.DrawLine(x, tl.Y, x, br.Y, pen);
        c.DrawLine(x - t, tl.Y, x + t, tl.Y, pen);
        c.DrawLine(x - t, br.Y, x + t, br.Y, pen);
        DrawTag(c, $"{Fmt.Num(plan.BoardWidth, 0)} mm", (tl.X + br.X) / 2, y, Ui.TextSecondary, 11);
        c.Save();
        c.Rotate(-90, x, (tl.Y + br.Y) / 2);
        DrawTag(c, $"{Fmt.Num(plan.BoardHeight, 0)} mm", x, (tl.Y + br.Y) / 2, Ui.TextSecondary, 11);
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
        foreach (var s in plan.Scenery.Where(s => s.Kind != SceneryKind.Tree))
        {
            var o = s.Outline();
            if ((o.Max(p => p.X) - o.Min(p => p.X)) * _zoom < Ui.Dp(60)) continue;
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
                var h = cn.Pose.HeadingRad;
                float tx = (float)(s.X + Math.Cos(h) * 24 * d), ty = (float)(s.Y + Math.Sin(h) * 24 * d);
                var pen = Stroke(Ui.Accent, 2.5f * d);
                c.DrawLine(s.X, s.Y, tx, ty, pen);
                c.DrawLine(tx, ty, (float)(tx - Math.Cos(h - 0.6) * 8 * d), (float)(ty - Math.Sin(h - 0.6) * 8 * d), pen);
                c.DrawLine(tx, ty, (float)(tx - Math.Cos(h + 0.6) * 8 * d), (float)(ty - Math.Sin(h + 0.6) * 8 * d), pen);
                c.DrawCircle(s.X, s.Y, 9 * d, Fill(Ui.Accent));
                c.DrawCircle(s.X, s.Y, 9 * d, Stroke(Color.White, 2 * d));
            }
            else if (_session.ShowEnds)
            {
                c.DrawCircle(s.X, s.Y, 7 * d, Fill(Ui.OpenEnd));
                c.DrawCircle(s.X, s.Y, 7 * d, Stroke(Color.White, 2 * d));
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

    private ConnectorRef? HitConnector(float sx, float sy, bool openOnly = true, float radiusDp = 22)
    {
        ConnectorRef? best = null;
        var bestD = radiusDp * Ui.Density;
        foreach (var c in openOnly ? _session.Connections.Open : _session.Connections.All)
        {
            var s = ToScreen(c.Pose.X, c.Pose.Y);
            var d = (float)Math.Sqrt((s.X - sx) * (s.X - sx) + (s.Y - sy) * (s.Y - sy));
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    private PlacedPiece? HitPiece((double X, double Y) world)
    {
        var tol = Math.Max(_gauge * 1.4, 16 * Ui.Density / _zoom);
        PlacedPiece? best = null;
        var bestD = tol;
        foreach (var p in _session.Plan.Pieces)
            foreach (var l in p.WorldLegs())
            {
                var d = l.DistanceTo(world.X, world.Y);
                if (d < bestD - 0.01 || (Math.Abs(d - bestD) < 0.01 && best is not null && p.Height > best.Height)) { bestD = d; best = p; }
            }
        return best;
    }

    private int? HitSmallObject(float sx, float sy, (double X, double Y) world)
    {
        var plan = _session.Plan;
        int? best = null;
        double bestD = 20 * Ui.Density;
        void Try(int id, double x, double y)
        {
            var s = ToScreen(x, y);
            var d = Math.Sqrt((s.X - sx) * (s.X - sx) + (s.Y - sy) * (s.Y - sy));
            if (d < bestD) { bestD = d; best = id; }
        }
        foreach (var s in plan.Signals) { var (x, y, _) = Electrics.SignalPose(plan, s); Try(s.Id, x, y); }
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
        if (best is not null || !_session.ShowScenery) return best;
        return plan.Scenery.Where(s => s.Kind is SceneryKind.Tree or SceneryKind.Building).Reverse().FirstOrDefault(s => s.Contains(world.X, world.Y))?.Id;
    }

    private int? HitArea((double X, double Y) world)
        => _session.ShowScenery
            ? _session.Plan.Scenery.Where(s => s.IsArea).OrderByDescending(s => s.Kind == SceneryKind.Water).ThenByDescending(s => s.Height).FirstOrDefault(s => s.Contains(world.X, world.Y))?.Id
            : null;

    // ---------- Touch ----------

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null) return false;
        _scale.OnTouchEvent(e);
        if (e.PointerCount > 1 || _scale.IsInProgress)
        {
            // Zwei Finger: laufende Aktion abbrechen, Ansicht wird über den Zoom-Erkenner bewegt
            if (!_multiTouch)
            {
                _multiTouch = true;
                if (_drag == DragMode.Move) _session.EndMove(Ui.Dp(20) / _zoom);
                _drag = DragMode.None;
                _band = null;
            }
            return true;
        }
        _gestures.OnTouchEvent(e);
        var x = e.GetX();
        var y = e.GetY();
        var world = ToWorld(x, y);
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                _multiTouch = false;
                _downX = x; _downY = y;
                _downWorld = world;
                _lastWorld = world;
                _moved = false;
                _drag = DragMode.None;
                OnDown(x, y, world);
                return true;
            case MotionEventActions.Move:
                if (_multiTouch) return true;
                _lastWorld = world;
                if (!_moved && Math.Abs(x - _downX) + Math.Abs(y - _downY) < Ui.Dp(8)) return true;
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
                }
                return true;
            case MotionEventActions.Up:
                if (!_multiTouch) OnUp(x, y, world);
                _drag = DragMode.None;
                _multiTouch = false;
                return true;
            case MotionEventActions.Cancel:
                if (_drag == DragMode.Move) _session.EndMove(Ui.Dp(20) / _zoom);
                _drag = DragMode.None;
                _band = null;
                Invalidate();
                return true;
        }
        return true;
    }

    /// <summary>Was ein Finger beim Aufsetzen trifft, bestimmt, ob Ziehen verschiebt oder die Ansicht bewegt.</summary>
    private void OnDown(float x, float y, (double X, double Y) world)
    {
        _panStartX = _ox;
        _panStartY = _oy;
        switch (_session.Tool)
        {
            case CanvasTool.Building:
                _drag = DragMode.Building;
                return;
            case CanvasTool.Terrain or CanvasTool.Water or CanvasTool.Tree or CanvasTool.Feeder or CanvasTool.Isolation or CanvasTool.Signal:
                _drag = DragMode.Pan;
                return;
        }
        if (HitConnector(x, y) is not null) { _drag = DragMode.Pan; return; }
        if (HitSmallObject(x, y, world) is { } obj && obj == _session.SelectedObject) { _session.BeginMove(); _drag = DragMode.Move; return; }
        if (HitPiece(world) is { } piece && _session.Selection.Contains(piece.Id)) { _session.BeginMove(); _drag = DragMode.Move; return; }
        if (HitArea(world) is { } area && area == _session.SelectedObject) { _session.BeginMove(); _drag = DragMode.Move; return; }
        _drag = DragMode.Pan;
    }

    private void OnUp(float x, float y, (double X, double Y) world)
    {
        switch (_drag)
        {
            case DragMode.Move:
                if (_moved) _session.EndMove(Ui.Dp(20) / _zoom);
                else Tap(x, y, world);
                return;
            case DragMode.Band:
                _band = null;
                if (_moved)
                {
                    double ax = Math.Min(_downWorld.X, world.X), ay = Math.Min(_downWorld.Y, world.Y), bx = Math.Max(_downWorld.X, world.X), by = Math.Max(_downWorld.Y, world.Y);
                    var ids = _session.Plan.Pieces.Where(p => { var m = p.Middle(); return m.X >= ax && m.X <= bx && m.Y >= ay && m.Y <= by; }).Select(p => p.Id).ToList();
                    _session.Select(ids, add: true);
                    _session.Say($"{_session.Selection.Count} Gleise gewählt.");
                }
                Invalidate();
                return;
            case DragMode.Building:
                var r = (X0: Math.Min(_downWorld.X, world.X), Y0: Math.Min(_downWorld.Y, world.Y), X1: Math.Max(_downWorld.X, world.X), Y1: Math.Max(_downWorld.Y, world.Y));
                if (!_moved || r.X1 - r.X0 < 5 || r.Y1 - r.Y0 < 5)
                    r = (_downWorld.X - _gauge * 6, _downWorld.Y - _gauge * 4, _downWorld.X + _gauge * 6, _downWorld.Y + _gauge * 4);
                _session.AddScenery(new SceneryItem
                {
                    Kind = SceneryKind.Building, X = Math.Round((r.X0 + r.X1) / 2), Y = Math.Round((r.Y0 + r.Y1) / 2),
                    Width = Math.Round(r.X1 - r.X0), Depth = Math.Round(r.Y1 - r.Y0), Height = Math.Round(_gauge * 6),
                });
                _session.Say("Gebäude gesetzt. Größe, Höhe und Drehung stehen in den Eigenschaften.");
                Invalidate();
                return;
            case DragMode.Pan:
                if (!_moved) Tap(x, y, world);
                return;
        }
    }

    private void Tap(float x, float y, (double X, double Y) world)
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
                _session.AddScenery(new SceneryItem { Kind = SceneryKind.Tree, X = Math.Round(world.X), Y = Math.Round(world.Y), Width = Math.Round(size), Height = Math.Round(size * 2.2), Name = "Baum" });
                return;
            case CanvasTool.Feeder:
                if (HitPiece(world) is { } fp) _session.AddFeeder(fp.Id);
                else _session.Say("Für eine Einspeisung bitte direkt auf ein Gleis tippen.");
                return;
            case CanvasTool.Isolation:
                if (HitConnector(x, y, openOnly: false, radiusDp: 26) is { } ic) _session.ToggleIsolation(ic.PieceId, ic.Index);
                else _session.Say("Für eine Trennstelle bitte auf eine Gleisverbindung (grüner Punkt) tippen.");
                return;
            case CanvasTool.Signal:
                if (HitConnector(x, y, openOnly: false, radiusDp: 26) is { } sc) _session.AddSignal(sc.PieceId, sc.Index);
                else _session.Say("Signale stehen an Gleisenden: bitte auf einen Punkt an einer Gleisverbindung tippen.");
                return;
        }
        if (HitConnector(x, y) is { } conn) { _session.SetActive(conn.PieceId, conn.Index); return; }
        if (HitSmallObject(x, y, world) is { } obj) { _session.SelectObject(obj); return; }
        if (HitPiece(world) is { } piece)
        {
            _session.Select([piece.Id], add: false);
            return;
        }
        if (HitArea(world) is { } area) { _session.SelectObject(area); return; }
        _session.ClearSelection();
    }

    private void LongPress(float x, float y)
    {
        if (_session.Tool != CanvasTool.Select || _moved) return;
        var world = ToWorld(x, y);
        PerformHapticFeedback(FeedbackConstants.LongPress);
        if (HitPiece(world) is { } piece)
        {
            // Langes Drücken: Gleis zur Auswahl hinzufügen oder herausnehmen
            _session.Toggle(piece.Id);
            _session.Say($"{_session.Selection.Count} Gleise gewählt. Ziehen verschiebt alle gewählten Gleise.");
            _drag = DragMode.None;
            return;
        }
        // Auf leerer Fläche: Rahmen aufziehen
        _drag = DragMode.Band;
        _session.Say("Rahmen aufziehen: alle Gleise darin werden gewählt.");
    }

    private void DoubleTap(float x, float y)
    {
        if (_session.Tool != CanvasTool.Select) return;
        var world = ToWorld(x, y);
        if (HitSmallObject(x, y, world) is not null || HitPiece(world) is not null || HitArea(world) is not null)
            PropertiesRequested?.Invoke();
        else
            Fit();
    }

    private sealed class ScaleListener(PlanView view) : ScaleGestureDetector.SimpleOnScaleGestureListener
    {
        private float _fx, _fy;

        public override bool OnScaleBegin(ScaleGestureDetector detector)
        {
            _fx = detector.FocusX;
            _fy = detector.FocusY;
            return true;
        }

        public override bool OnScale(ScaleGestureDetector detector)
        {
            // Verschieben mit zwei Fingern über den Mittelpunkt, Zoomen um den Mittelpunkt
            view._ox += detector.FocusX - _fx;
            view._oy += detector.FocusY - _fy;
            _fx = detector.FocusX;
            _fy = detector.FocusY;
            view.ZoomBy(detector.ScaleFactor, detector.FocusX, detector.FocusY);
            return true;
        }
    }

    private sealed class GestureListener(PlanView view) : GestureDetector.SimpleOnGestureListener
    {
        public override void OnLongPress(MotionEvent e) => view.LongPress(e.GetX(), e.GetY());

        public override bool OnDoubleTap(MotionEvent e)
        {
            view.DoubleTap(e.GetX(), e.GetY());
            return true;
        }
    }
}
