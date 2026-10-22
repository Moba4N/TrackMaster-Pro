using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TrackMaster.Core;

namespace TrackMaster.App;

/// <summary>
/// Zeichenfläche des Gleisplans. Weltkoordinaten in mm; Ansicht = Verschiebung + Zoom (Pixel je mm).
/// Linke Maustaste: wählen, ziehen, Rahmen. Rechte/mittlere Taste oder Leertaste: Ansicht verschieben. Mausrad: zoomen.
/// </summary>
public sealed class PlanCanvas : FrameworkElement
{
    private readonly PlanSession _session;
    private double _ox, _oy, _zoom = 0.4;
    private bool _fitted;

    // Zwischengespeicherte Geometrie, neu bei jeder Planänderung
    private StreamGeometry? _bed, _rails, _stops, _selected;
    private double _gauge;

    private enum DragMode { None, Pan, Move, Band }
    private DragMode _drag;
    private Point _dragStartScreen;
    private Point _dragStartWorld;
    private double _panStartX, _panStartY;
    private bool _moved;
    private Rect? _band;
    private bool _spaceDown;

    public event Action? ViewChanged;
    public event Action<Point?>? CursorMoved;

    public double Zoom => _zoom;

    public PlanCanvas(PlanSession session)
    {
        _session = session;
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        AllowDrop = true;
        SnapsToDevicePixels = true;
        session.Changed += () => { _bed = null; InvalidateVisual(); };
        SizeChanged += (_, _) => { if (!_fitted && ActualWidth > 0) { _fitted = true; Fit(); } };
    }

    // ---------- Ansicht ----------

    public Point ToWorld(Point screen) => new((screen.X - _ox) / _zoom, (screen.Y - _oy) / _zoom);
    public Point ToScreen(double x, double y) => new(x * _zoom + _ox, y * _zoom + _oy);
    public (double X, double Y) CenterWorld() { var p = ToWorld(new Point(ActualWidth / 2, ActualHeight / 2)); return (p.X, p.Y); }

    public void Fit()
    {
        if (ActualWidth <= 0) return;
        var (x0, y0, x1, y1) = _session.Plan.Bounds();
        const double pad = 48;
        double w = Math.Max(x1 - x0, 1), h = Math.Max(y1 - y0, 1);
        _zoom = Math.Max(0.02, Math.Min((ActualWidth - 2 * pad) / w, (ActualHeight - 2 * pad) / h));
        _ox = (ActualWidth - w * _zoom) / 2 - x0 * _zoom;
        _oy = (ActualHeight - h * _zoom) / 2 - y0 * _zoom;
        Redraw();
    }

    public void ZoomBy(double factor, Point? at = null)
    {
        var p = at ?? new Point(ActualWidth / 2, ActualHeight / 2);
        var nz = Math.Clamp(_zoom * factor, 0.02, 20);
        _ox = p.X - (p.X - _ox) * nz / _zoom;
        _oy = p.Y - (p.Y - _oy) * nz / _zoom;
        _zoom = nz;
        Redraw();
    }

    /// <summary>Verschiebt die Ansicht, falls der Punkt außerhalb liegt.</summary>
    public void Reveal(double x, double y)
    {
        var s = ToScreen(x, y);
        if (s.X < 40 || s.Y < 40 || s.X > ActualWidth - 40 || s.Y > ActualHeight - 40)
        {
            _ox = ActualWidth / 2 - x * _zoom;
            _oy = ActualHeight / 2 - y * _zoom;
            Redraw();
        }
    }

    private void Redraw()
    {
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    // ---------- Zeichnen ----------

    private void EnsureGeometry()
    {
        if (_bed is not null) return;
        var plan = _session.Plan;
        _gauge = plan.Gauge.TrackWidth;
        var all = plan.Pieces.SelectMany(p => p.WorldLegs()).ToList();
        _bed = TrackDrawing.Build(all, 0);
        _rails = TrackDrawing.Build(all, _gauge / 2, -_gauge / 2);
        _stops = TrackDrawing.Stops(plan.Pieces.Where(p => p.Def.Kind == PieceKind.Buffer).Select(p => p.WorldLegs()[0]), _gauge);
        _selected = TrackDrawing.Build(plan.Pieces.Where(p => _session.Selection.Contains(p.Id)).SelectMany(p => p.WorldLegs()), 0);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var plan = _session.Plan;
        EnsureGeometry();
        dc.DrawRectangle(Theme.CanvasBrush, null, new Rect(RenderSize));
        double z = _zoom, g = _gauge;

        dc.PushTransform(new MatrixTransform(z, 0, 0, z, _ox, _oy));
        var board = new Rect(0, 0, plan.BoardWidth, plan.BoardHeight);
        dc.DrawRoundedRectangle(Theme.BoardBrush, new Pen(Theme.BoardStrokeBrush, 1.2 / z), board, 4 / z, 4 / z);
        if (_session.ShowGrid) DrawGrid(dc, board);
        if (_session.Selection.Count > 0)
            dc.DrawGeometry(null, new Pen(Theme.SelectionBrush, Math.Max(g * 4, 10 / z)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, _selected);
        dc.DrawGeometry(null, TrackDrawing.BedPen(g), _bed);
        dc.DrawGeometry(null, TrackDrawing.SleeperPen(g), _bed);
        dc.DrawGeometry(null, TrackDrawing.RailPen(g), _rails);
        dc.DrawGeometry(null, TrackDrawing.StopPen(g), _stops);
        dc.Pop();

        DrawDimensions(dc, plan);
        if (_session.ShowLabels) DrawLabels(dc, plan);
        DrawConnectors(dc);
        if (_band is { } band) dc.DrawRectangle(Theme.BandBrush, new Pen(Theme.AccentBrush, 1) { DashStyle = DashStyles.Dash }, band);
    }

    private void DrawGrid(DrawingContext dc, Rect board)
    {
        var view = new Rect(ToWorld(new Point(0, 0)), ToWorld(new Point(ActualWidth, ActualHeight)));
        view.Intersect(board);
        if (view.IsEmpty) return;
        void Lines(double step, Brush brush, double px)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                for (var x = Math.Ceiling(view.Left / step) * step; x <= view.Right; x += step)
                { ctx.BeginFigure(new Point(x, view.Top), false, false); ctx.LineTo(new Point(x, view.Bottom), true, false); }
                for (var y = Math.Ceiling(view.Top / step) * step; y <= view.Bottom; y += step)
                { ctx.BeginFigure(new Point(view.Left, y), false, false); ctx.LineTo(new Point(view.Right, y), true, false); }
            }
            geo.Freeze();
            dc.DrawGeometry(null, new Pen(brush, px / _zoom), geo);
        }
        if (10 * _zoom >= 6) Lines(10, Theme.GridMinorBrush, 1);
        if (100 * _zoom >= 8) Lines(100, Theme.GridMajorBrush, 1);
    }

    private FormattedText Text(string s, double size, Brush brush)
        => new(s, CultureInfo.GetCultureInfo("de-DE"), FlowDirection.LeftToRight, new Typeface(Theme.Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private void DrawDimensions(DrawingContext dc, LayoutPlan plan)
    {
        var tl = ToScreen(0, 0);
        var br = ToScreen(plan.BoardWidth, plan.BoardHeight);
        var pen = new Pen(Theme.TextSecondaryBrush, 1);
        double y = tl.Y - 14, x = tl.X - 14;
        dc.DrawLine(pen, new Point(tl.X, y), new Point(br.X, y));
        dc.DrawLine(pen, new Point(tl.X, y - 4), new Point(tl.X, y + 4));
        dc.DrawLine(pen, new Point(br.X, y - 4), new Point(br.X, y + 4));
        dc.DrawLine(pen, new Point(x, tl.Y), new Point(x, br.Y));
        dc.DrawLine(pen, new Point(x - 4, tl.Y), new Point(x + 4, tl.Y));
        dc.DrawLine(pen, new Point(x - 4, br.Y), new Point(x + 4, br.Y));
        var wText = Text($"{Fmt.Num(plan.BoardWidth, 0)} mm", 11, Theme.TextSecondaryBrush);
        dc.DrawRectangle(Theme.CanvasBrush, null, new Rect((tl.X + br.X - wText.Width) / 2 - 4, y - wText.Height / 2, wText.Width + 8, wText.Height));
        dc.DrawText(wText, new Point((tl.X + br.X - wText.Width) / 2, y - wText.Height / 2));
        var hText = Text($"{Fmt.Num(plan.BoardHeight, 0)} mm", 11, Theme.TextSecondaryBrush);
        var mid = new Point(x, (tl.Y + br.Y) / 2);
        dc.PushTransform(new RotateTransform(-90, mid.X, mid.Y));
        dc.DrawRectangle(Theme.CanvasBrush, null, new Rect(mid.X - hText.Width / 2 - 4, mid.Y - hText.Height / 2, hText.Width + 8, hText.Height));
        dc.DrawText(hText, new Point(mid.X - hText.Width / 2, mid.Y - hText.Height / 2));
        dc.Pop();
    }

    private void DrawLabels(DrawingContext dc, LayoutPlan plan)
    {
        foreach (var p in plan.Pieces)
        {
            var m = p.Middle();
            var s = ToScreen(m.X, m.Y);
            var t = Text(p.Def.Number, 10.5, Theme.TextBrush);
            var r = new Rect(s.X - t.Width / 2 - 3, s.Y - _gauge * 1.6 * _zoom - t.Height, t.Width + 6, t.Height);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF)), null, r, 3, 3);
            dc.DrawText(t, new Point(r.X + 3, r.Y));
        }
    }

    private void DrawConnectors(DrawingContext dc)
    {
        var white = new Pen(Brushes.White, 1.5);
        var active = _session.ActiveConnector;
        foreach (var c in _session.Connections.All)
        {
            var s = ToScreen(c.Pose.X, c.Pose.Y);
            if (!c.IsOpen)
            {
                if (_session.Selection.Contains(c.PieceId)) dc.DrawEllipse(Theme.SuccessBrush, white, s, 3.5, 3.5);
                continue;
            }
            if (c == active)
            {
                var h = c.Pose.HeadingRad;
                var tip = new Point(s.X + Math.Cos(h) * 20, s.Y + Math.Sin(h) * 20);
                var pen = new Pen(Theme.AccentBrush, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(pen, s, tip);
                dc.DrawLine(pen, tip, new Point(tip.X - Math.Cos(h - 0.6) * 6, tip.Y - Math.Sin(h - 0.6) * 6));
                dc.DrawLine(pen, tip, new Point(tip.X - Math.Cos(h + 0.6) * 6, tip.Y - Math.Sin(h + 0.6) * 6));
                dc.DrawEllipse(Theme.AccentBrush, white, s, 7, 7);
            }
            else if (_session.ShowEnds)
                dc.DrawEllipse(Theme.OpenEndBrush, white, s, 5, 5);
        }
    }

    // ---------- Treffer ----------

    private ConnectorRef? HitConnector(Point screen)
    {
        ConnectorRef? best = null;
        var bestD = 9.0;
        foreach (var c in _session.Connections.Open)
        {
            var s = ToScreen(c.Pose.X, c.Pose.Y);
            var d = (s - screen).Length;
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    private PlacedPiece? HitPiece(Point world)
    {
        var tol = Math.Max(_gauge * 1.4, 7 / _zoom);
        PlacedPiece? best = null;
        var bestD = tol;
        foreach (var p in _session.Plan.Pieces)
            foreach (var l in p.WorldLegs())
            {
                var d = l.DistanceTo(world.X, world.Y);
                if (d < bestD) { bestD = d; best = p; }
            }
        return best;
    }

    // ---------- Maus ----------

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var screen = e.GetPosition(this);
        var world = ToWorld(screen);
        CaptureMouse();
        _dragStartScreen = screen;
        _dragStartWorld = world;
        _moved = false;

        if (e.ChangedButton is MouseButton.Middle or MouseButton.Right || _spaceDown || _session.Tool == CanvasTool.Pan)
        {
            _drag = DragMode.Pan;
            _panStartX = _ox; _panStartY = _oy;
            Cursor = Cursors.ScrollAll;
            return;
        }
        if (e.ChangedButton != MouseButton.Left) return;

        if (HitConnector(screen) is { } conn)
        {
            _session.SetActive(conn.PieceId, conn.Index);
            _drag = DragMode.None;
            return;
        }
        if (HitPiece(world) is { } piece)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
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
        _drag = DragMode.Band;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var screen = e.GetPosition(this);
        var world = ToWorld(screen);
        CursorMoved?.Invoke(world);
        switch (_drag)
        {
            case DragMode.Pan:
                _ox = _panStartX + screen.X - _dragStartScreen.X;
                _oy = _panStartY + screen.Y - _dragStartScreen.Y;
                Redraw();
                break;
            case DragMode.Move:
                if (!_moved && (screen - _dragStartScreen).Length < 3) return;
                _moved = true;
                _session.MoveBy(world.X - _dragStartWorld.X, world.Y - _dragStartWorld.Y);
                break;
            case DragMode.Band:
                if ((screen - _dragStartScreen).Length < 4) return;
                _moved = true;
                _band = new Rect(_dragStartScreen, screen);
                InvalidateVisual();
                break;
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        ReleaseMouseCapture();
        var mode = _drag;
        _drag = DragMode.None;
        Cursor = _session.Tool == CanvasTool.Pan ? Cursors.Hand : null;
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
                var a = ToWorld(band.TopLeft);
                var b = ToWorld(band.BottomRight);
                var rect = new Rect(a, b);
                var ids = _session.Plan.Pieces.Where(p => { var m = p.Middle(); return rect.Contains(m.X, m.Y); }).Select(p => p.Id).ToList();
                _session.Select(ids, add: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                break;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        CursorMoved?.Invoke(null);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        ZoomBy(Math.Exp(e.Delta * 0.0015), e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Space && !_spaceDown) { _spaceDown = true; Cursor = Cursors.Hand; e.Handled = true; }
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (e.Key == Key.Space) { _spaceDown = false; Cursor = _session.Tool == CanvasTool.Pan ? Cursors.Hand : null; }
    }

    // ---------- Ziehen aus der Bibliothek ----------

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Effects = e.Data.GetDataPresent(DataFormats.StringFormat) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        if (e.Data.GetData(DataFormats.StringFormat) is not string key) return;
        var w = ToWorld(e.GetPosition(this));
        _session.Drop(key, w.X, w.Y, 28 / _zoom);
        Focus();
    }

    // Hintergrund trifft immer (sonst gehen Klicks ins Leere)
    protected override HitTestResult HitTestCore(PointHitTestParameters p) => new PointHitTestResult(this, p.HitPoint);
}
