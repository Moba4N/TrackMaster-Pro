using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TrackMaster.Core;

namespace TrackMaster.App;

/// <summary>
/// Zeichenfläche des Gleisplans. Weltkoordinaten in mm; Ansicht = Verschiebung + Zoom (Pixel je mm).
/// Linke Maustaste: wählen, ziehen, Rahmen – oder das gewählte Werkzeug (Gelände, Gebäude, Baum, Einspeisung, Trennstelle, Signal).
/// Rechte/mittlere Taste oder Leertaste: Ansicht verschieben. Mausrad: zoomen.
/// </summary>
public sealed class PlanCanvas : FrameworkElement
{
    private readonly PlanSession _session;
    private double _ox, _oy, _zoom = 0.4;
    private bool _fitted;

    // Zwischengespeicherte Geometrie, neu bei jeder Planänderung
    private PlanScene? _scene;
    private double _gauge;

    private enum DragMode { None, Pan, Move, Band, Create }
    private DragMode _drag;
    private Point _dragStartScreen;
    private Point _dragStartWorld;
    private Point _lastWorld;
    private double _panStartX, _panStartY;
    private bool _moved;
    private Rect? _band;
    private bool _spaceDown;
    private readonly List<(double X, double Y)> _polygon = [];
    private readonly Random _random = new(7);

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
        session.Changed += () => { _scene = null; InvalidateVisual(); };
        SizeChanged += (_, _) => { if (!_fitted && ActualWidth > 0) { _fitted = true; Fit(); } };
    }

    /// <summary>Zeichnung neu aufbauen (z. B. nach Umschalten der Stromkreis-Ansicht).</summary>
    public void Rebuild()
    {
        _scene = null;
        InvalidateVisual();
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

    // ---------- Vieleck zeichnen (Höhenfläche, Gewässer) ----------

    public bool IsDrawingPolygon => _polygon.Count > 0;

    /// <summary>Schließt das gerade gezeichnete Vieleck ab (Doppelklick oder Eingabetaste).</summary>
    public void FinishPolygon()
    {
        if (_polygon.Count < 3)
        {
            if (_polygon.Count > 0) _session.Say("Eine Fläche braucht mindestens drei Punkte.");
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
        _session.Say(water ? "Gewässer angelegt. Tiefe und Name stehen in den Eigenschaften." : "Höhenfläche angelegt. Höhe und Name stehen in den Eigenschaften.");
        InvalidateVisual();
    }

    public void CancelPolygon()
    {
        _polygon.Clear();
        InvalidateVisual();
    }

    // ---------- Zeichnen ----------

    private PlanScene Scene()
    {
        if (_scene is not null) return _scene;
        _gauge = _session.Plan.Gauge.TrackWidth;
        return _scene = new PlanScene(_session.Plan, _session.Selection, _session.ShowSections);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var plan = _session.Plan;
        var scene = Scene();
        dc.DrawRectangle(Theme.CanvasBrush, null, new Rect(RenderSize));
        double z = _zoom;

        dc.PushTransform(new MatrixTransform(z, 0, 0, z, _ox, _oy));
        var board = new Rect(0, 0, plan.BoardWidth, plan.BoardHeight);
        scene.Draw(dc, z, new RenderOptions(_session.ShowScenery, _session.ShowSections, _session.Selection, _session.SelectedObject),
            d =>
            {
                if (!_session.ShowGrid) return;
                d.PushClip(PlanScene.BoardOutline(plan));
                DrawGrid(d, board);
                d.Pop();
            });
        DrawToolPreview(dc);
        dc.Pop();

        DrawDimensions(dc, plan);
        if (_session.ShowLabels) DrawLabels(dc, plan);
        if (_session.ShowHeights) DrawHeights(dc, plan);
        if (_session.ShowScenery) DrawSceneryNames(dc, plan);
        DrawSignalNames(dc, plan);
        DrawConnectors(dc);
        DrawIssues(dc);
        if (_band is { } band) dc.DrawRectangle(Theme.BandBrush, new Pen(Theme.AccentBrush, 1) { DashStyle = DashStyles.Dash }, band);
    }

    private void DrawToolPreview(DrawingContext dc)
    {
        var z = _zoom;
        if (_polygon.Count > 0)
        {
            var pts = _polygon.Append((_lastWorld.X, _lastWorld.Y)).ToList();
            var fill = _session.Tool == CanvasTool.Water ? new SolidColorBrush(Color.FromArgb(0x60, 0x86, 0xBC, 0xE6)) : new SolidColorBrush(Color.FromArgb(0x60, 0x9C, 0xB8, 0x7E));
            dc.DrawGeometry(fill, new Pen(Theme.AccentBrush, 1.5 / z) { DashStyle = DashStyles.Dash }, PlanScene.Polygon(pts));
            foreach (var p in _polygon) dc.DrawEllipse(Brushes.White, new Pen(Theme.AccentBrush, 1.5 / z), new Point(p.X, p.Y), 4 / z, 4 / z);
        }
        if (_drag == DragMode.Create && _session.Tool == CanvasTool.Building && _moved)
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x80, 0xB8, 0x5C, 0x45)), new Pen(Theme.AccentBrush, 1.5 / z), new Rect(_dragStartWorld, _lastWorld));
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

    private FormattedText Text(string s, double size, Brush brush, bool bold = false)
        => new(s, CultureInfo.GetCultureInfo("de-DE"), FlowDirection.LeftToRight, new Typeface(Theme.Font, FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static readonly Brush LabelBack = Frozen(new SolidColorBrush(Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF)));

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }

    private void DrawTag(DrawingContext dc, string text, Point center, Brush? brush = null, double size = 10.5)
    {
        var t = Text(text, size, brush ?? Theme.TextBrush);
        var r = new Rect(center.X - t.Width / 2 - 3, center.Y - t.Height / 2, t.Width + 6, t.Height);
        dc.DrawRoundedRectangle(LabelBack, null, r, 3, 3);
        dc.DrawText(t, new Point(r.X + 3, r.Y));
    }

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
            DrawTag(dc, p.Def.Number + (p.Address is { } a ? $" · {a}" : ""), new Point(s.X, s.Y - _gauge * 1.6 * _zoom - 8));
        }
    }

    private void DrawHeights(DrawingContext dc, LayoutPlan plan)
    {
        foreach (var p in plan.Pieces)
        {
            var g = p.EffectiveGrade;
            if (Math.Abs(p.Height) < 0.5 && Math.Abs(g) < 1e-6) continue;
            if (p.Def.TrackLength * _zoom < 70) continue;
            var m = p.Middle();
            var s = ToScreen(m.X, m.Y);
            var mid = p.HeightAt(0, 0.5);
            var text = Math.Abs(g) < 1e-6 ? $"{Fmt.Num(mid, 0)} mm" : $"{Fmt.Num(mid, 0)} mm · {(g > 0 ? "↗" : "↘")} {Fmt.Num(Math.Abs(g) * 100, 1)} %";
            DrawTag(dc, text, new Point(s.X, s.Y + _gauge * 1.6 * _zoom + 8), Math.Abs(g) * 100 > PlanCheck.WarnGradePercent ? Theme.DangerBrush : Theme.AccentBrush, 10);
        }
    }

    private void DrawSceneryNames(DrawingContext dc, LayoutPlan plan)
    {
        foreach (var s in plan.Scenery.Where(s => s.Kind != SceneryKind.Tree))
        {
            var outline = s.Outline();
            var w = (outline.Max(p => p.X) - outline.Min(p => p.X)) * _zoom;
            if (w < 60) continue;
            var (cx, cy) = s.Center();
            var text = s.Kind switch
            {
                SceneryKind.Water => s.Name,
                _ => $"{s.Name} · {Fmt.Num(s.Height, 0)} mm",
            };
            DrawTag(dc, text, ToScreen(cx, cy), Theme.TextSecondaryBrush, 10);
        }
    }

    private void DrawSignalNames(DrawingContext dc, LayoutPlan plan)
    {
        if (_zoom < 0.25) return;
        foreach (var s in plan.Signals.Where(s => s.Name.Length > 0))
        {
            var (x, y, _) = Electrics.SignalPose(plan, s);
            var p = ToScreen(x, y);
            DrawTag(dc, s.Name + (s.Address is { } a ? $" ({a})" : ""), new Point(p.X, p.Y - 14), Theme.TextBrush, 10);
        }
    }

    private void DrawConnectors(DrawingContext dc)
    {
        var white = new Pen(Brushes.White, 1.5);
        var active = _session.ActiveConnector;
        var tool = _session.Tool;
        foreach (var c in _session.Connections.All)
        {
            var s = ToScreen(c.Pose.X, c.Pose.Y);
            if (!c.IsOpen)
            {
                if (_session.Selection.Contains(c.PieceId) || tool is CanvasTool.Isolation or CanvasTool.Signal) dc.DrawEllipse(Theme.SuccessBrush, white, s, 3.5, 3.5);
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

    private void DrawIssues(DrawingContext dc)
    {
        foreach (var i in _session.Issues.Where(i => i.Level != IssueLevel.Hinweis))
        {
            var s = ToScreen(i.X, i.Y);
            var brush = i.Level == IssueLevel.Fehler ? Theme.DangerBrush : Theme.OpenEndBrush;
            dc.DrawEllipse(null, new Pen(brush, 2.5), s, 16, 16);
            dc.DrawEllipse(brush, new Pen(Brushes.White, 1.5), new Point(s.X + 12, s.Y - 12), 8, 8);
            var t = Text("!", 11, Brushes.White, true);
            dc.DrawText(t, new Point(s.X + 12 - t.Width / 2, s.Y - 12 - t.Height / 2));
        }
    }

    // ---------- Treffer ----------

    private ConnectorRef? HitConnector(Point screen, bool openOnly = true, double radius = 9)
    {
        ConnectorRef? best = null;
        var bestD = radius;
        foreach (var c in openOnly ? _session.Connections.Open : _session.Connections.All)
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
                // Bei gleichem Abstand gewinnt das höher liegende Gleis (Brücke)
                if (d < bestD - 0.01 || (Math.Abs(d - bestD) < 0.01 && best is not null && p.Height > best.Height)) { bestD = d; best = p; }
            }
        return best;
    }

    /// <summary>Kleine Objekte (Signal, Einspeisung, Trennstelle) nach Bildschirmabstand, dann Bäume und Gebäude.</summary>
    private int? HitSmallObject(Point screen, Point world)
    {
        var plan = _session.Plan;
        int? best = null;
        var bestD = 11.0;
        void Try(int id, double x, double y)
        {
            var d = (ToScreen(x, y) - screen).Length;
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

    private int? HitArea(Point world)
        => _session.ShowScenery
            ? _session.Plan.Scenery.Where(s => s.IsArea).OrderByDescending(s => s.Kind == SceneryKind.Water).ThenByDescending(s => s.Height).FirstOrDefault(s => s.Contains(world.X, world.Y))?.Id
            : null;

    // ---------- Maus ----------

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var screen = e.GetPosition(this);
        var world = ToWorld(screen);
        _dragStartScreen = screen;
        _dragStartWorld = world;
        _lastWorld = world;
        _moved = false;

        if (e.ChangedButton is MouseButton.Middle or MouseButton.Right || _spaceDown || _session.Tool == CanvasTool.Pan)
        {
            CaptureMouse();
            _drag = DragMode.Pan;
            _panStartX = _ox; _panStartY = _oy;
            Cursor = Cursors.ScrollAll;
            return;
        }
        if (e.ChangedButton != MouseButton.Left) return;

        switch (_session.Tool)
        {
            case CanvasTool.Terrain:
            case CanvasTool.Water:
                if (e.ClickCount == 2) { FinishPolygon(); return; }
                _polygon.Add((Math.Round(world.X), Math.Round(world.Y)));
                InvalidateVisual();
                return;
            case CanvasTool.Building:
                CaptureMouse();
                _drag = DragMode.Create;
                return;
            case CanvasTool.Tree:
                var size = _gauge * (2.6 + _random.NextDouble() * 1.4);
                _session.AddScenery(new SceneryItem { Kind = SceneryKind.Tree, X = Math.Round(world.X), Y = Math.Round(world.Y), Width = Math.Round(size), Height = Math.Round(size * 2.2), Name = "Baum" });
                return;
            case CanvasTool.Feeder:
                if (HitPiece(world) is { } fp) _session.AddFeeder(fp.Id);
                else _session.Say("Für eine Einspeisung bitte direkt auf ein Gleis klicken.");
                return;
            case CanvasTool.Isolation:
                if (HitConnector(screen, openOnly: false, radius: 14) is { } ic) _session.ToggleIsolation(ic.PieceId, ic.Index);
                else _session.Say("Für eine Trennstelle bitte auf eine Gleisverbindung (grüner Punkt) klicken.");
                return;
            case CanvasTool.Signal:
                if (HitConnector(screen, openOnly: false, radius: 14) is { } sc) _session.AddSignal(sc.PieceId, sc.Index);
                else _session.Say("Signale stehen an Gleisenden: bitte auf einen Punkt an einer Gleisverbindung klicken.");
                return;
        }

        CaptureMouse();
        if (HitConnector(screen) is { } conn)
        {
            _session.SetActive(conn.PieceId, conn.Index);
            _drag = DragMode.None;
            return;
        }
        if (HitSmallObject(screen, world) is { } obj)
        {
            _session.SelectObject(obj);
            _session.BeginMove();
            _drag = DragMode.Move;
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
        if (HitArea(world) is { } area && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _session.SelectObject(area);
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
        _lastWorld = world;
        CursorMoved?.Invoke(world);
        if (_polygon.Count > 0) InvalidateVisual();
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
            case DragMode.Create:
                if ((screen - _dragStartScreen).Length < 4) return;
                _moved = true;
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
        Cursor = _session.Tool == CanvasTool.Pan ? Cursors.Hand : _session.Tool is CanvasTool.Select ? null : Cursors.Cross;
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
            case DragMode.Create:
                var end = ToWorld(e.GetPosition(this));
                var r = new Rect(_dragStartWorld, end);
                if (!_moved || r.Width < 5 || r.Height < 5)
                    r = new Rect(_dragStartWorld.X - _gauge * 6, _dragStartWorld.Y - _gauge * 4, _gauge * 12, _gauge * 8);
                _session.AddScenery(new SceneryItem
                {
                    Kind = SceneryKind.Building, X = Math.Round(r.X + r.Width / 2), Y = Math.Round(r.Y + r.Height / 2),
                    Width = Math.Round(r.Width), Depth = Math.Round(r.Height), Height = Math.Round(_gauge * 6),
                });
                InvalidateVisual();
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
