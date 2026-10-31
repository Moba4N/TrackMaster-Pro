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
    /// <summary>Drehung der Ansicht in Grad (im Uhrzeigersinn); der Plan selbst bleibt unverändert.</summary>
    private double _rot;
    private bool _fitted;

    // Zwischengespeicherte Geometrie, neu bei jeder Planänderung
    private PlanScene? _scene;
    private double _gauge;

    private enum DragMode { None, Pan, Move, Band, Create, FlexDraw, FlexEnd, Board }
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
    // Flexgleis: neu ziehen bzw. ein Ende nachformen
    private FlexDraft? _flexDraft;
    private ConnectorRef? _flexEnd;
    private bool _reshaping;
    private IReadOnlyList<Segment> _reshapeLast = [];

    public event Action? ViewChanged;
    public event Action<Point?>? CursorMoved;

    public double Zoom => _zoom;
    public double ViewRotation => _rot;

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

    private (double Cos, double Sin) Turn => (Math.Cos(_rot * Math.PI / 180), Math.Sin(_rot * Math.PI / 180));

    public Point ToWorld(Point screen)
    {
        var (c, s) = Turn;
        double dx = (screen.X - _ox) / _zoom, dy = (screen.Y - _oy) / _zoom;
        return new(dx * c + dy * s, -dx * s + dy * c);
    }

    public Point ToScreen(double x, double y)
    {
        var (c, s) = Turn;
        return new((x * c - y * s) * _zoom + _ox, (x * s + y * c) * _zoom + _oy);
    }

    /// <summary>Plan → Bildschirm: Maßstab, Drehung, Verschiebung.</summary>
    private Matrix ViewMatrix
    {
        get
        {
            var (c, s) = Turn;
            return new Matrix(c * _zoom, s * _zoom, -s * _zoom, c * _zoom, _ox, _oy);
        }
    }

    /// <summary>Verschiebt die Ansicht so, dass der Planpunkt an der Bildschirmstelle liegt.</summary>
    private void Anchor(Point world, Point screen)
    {
        var (c, s) = Turn;
        _ox = screen.X - (world.X * c - world.Y * s) * _zoom;
        _oy = screen.Y - (world.X * s + world.Y * c) * _zoom;
    }

    public (double X, double Y) CenterWorld() { var p = ToWorld(new Point(ActualWidth / 2, ActualHeight / 2)); return (p.X, p.Y); }

    public void Fit()
    {
        if (ActualWidth <= 0) return;
        var (x0, y0, x1, y1) = _session.Plan.Bounds();
        const double pad = 48;
        var (c, s) = Turn;
        var turned = new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) }.Select(p => (X: p.Item1 * c - p.Item2 * s, Y: p.Item1 * s + p.Item2 * c)).ToList();
        double w = Math.Max(turned.Max(p => p.X) - turned.Min(p => p.X), 1), h = Math.Max(turned.Max(p => p.Y) - turned.Min(p => p.Y), 1);
        _zoom = Math.Max(0.02, Math.Min((ActualWidth - 2 * pad) / w, (ActualHeight - 2 * pad) / h));
        Anchor(new Point((x0 + x1) / 2, (y0 + y1) / 2), new Point(ActualWidth / 2, ActualHeight / 2));
        Redraw();
    }

    public void ZoomBy(double factor, Point? at = null)
    {
        var p = at ?? new Point(ActualWidth / 2, ActualHeight / 2);
        var world = ToWorld(p);
        _zoom = Math.Clamp(_zoom * factor, 0.02, 20);
        Anchor(world, p);
        Redraw();
    }

    /// <summary>Dreht die Ansicht frei um einen Bildschirmpunkt (Standard: Mitte). Der Plan selbst ändert sich nicht.</summary>
    public void SetViewRotation(double degrees, Point? at = null)
    {
        var p = at ?? new Point(ActualWidth / 2, ActualHeight / 2);
        var world = ToWorld(p);
        _rot = Angles.Normalize(degrees);
        if (_rot > 180) _rot -= 360;
        if (Math.Abs(_rot) < 1e-9) _rot = 0;
        Anchor(world, p);
        Redraw();
    }

    public void RotateViewBy(double degrees, Point? at = null) => SetViewRotation(_rot + degrees, at);

    /// <summary>Verschiebt die Ansicht, falls der Punkt außerhalb liegt.</summary>
    public void Reveal(double x, double y)
    {
        var s = ToScreen(x, y);
        if (s.X < 40 || s.Y < 40 || s.X > ActualWidth - 40 || s.Y > ActualHeight - 40)
        {
            Anchor(new Point(x, y), new Point(ActualWidth / 2, ActualHeight / 2));
            Redraw();
        }
    }

    private void Redraw()
    {
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    // ---------- Vieleck zeichnen (Höhenfläche, Gewässer) ----------

    public bool IsDrawingPolygon => _polygon.Count > 0 || _dimStart is not null;

    /// <summary>Werkzeug Beschriftung: Klick auf diese Stelle (Weltkoordinaten); das Hauptfenster fragt den Text ab.</summary>
    public event Action<double, double>? LabelRequested;
    private (double X, double Y)? _dimStart;

    /// <summary>Schließt das gerade gezeichnete Vieleck ab (Doppelklick oder Eingabetaste).</summary>
    public void FinishPolygon()
    {
        if (_polygon.Count < 3)
        {
            if (_polygon.Count > 0) _session.Say(Lang.T("Eine Fläche braucht mindestens drei Punkte."));
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
        _session.Say(water ? Lang.T("Gewässer angelegt. Tiefe und Name stehen in den Eigenschaften.") : Lang.T("Höhenfläche angelegt. Höhe und Name stehen in den Eigenschaften."));
        InvalidateVisual();
    }

    public void CancelPolygon()
    {
        _polygon.Clear();
        _dimStart = null;
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

        dc.PushTransform(new MatrixTransform(ViewMatrix));
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
        DrawCompass(dc);
    }

    /// <summary>Kompass oben rechts, solange die Ansicht gedreht ist; ein Klick darauf richtet sie wieder gerade aus.</summary>
    private Point CompassCenter => new(ActualWidth - 34, 92); // unter dem Hinweisband

    private void DrawCompass(DrawingContext dc)
    {
        if (_rot == 0) return;
        var c = CompassCenter;
        dc.DrawEllipse(Theme.PanelBrush, new Pen(Theme.BorderStrongBrush, 1), c, 22, 22);
        dc.PushTransform(new RotateTransform(_rot, c.X, c.Y));
        var north = new StreamGeometry();
        using (var ctx = north.Open())
        {
            ctx.BeginFigure(new Point(c.X, c.Y - 16), true, true);
            ctx.LineTo(new Point(c.X + 6, c.Y + 2), true, false);
            ctx.LineTo(new Point(c.X - 6, c.Y + 2), true, false);
        }
        dc.DrawGeometry(Theme.AccentBrush, null, north);
        dc.DrawLine(new Pen(Theme.TextSecondaryBrush, 2), new Point(c.X, c.Y + 2), new Point(c.X, c.Y + 14));
        dc.Pop();
        var t = Text($"{Fmt.Num(_rot, 0)}°", 10.5, Theme.CanvasTextBrush);
        dc.DrawText(t, new Point(c.X - t.Width / 2, c.Y + 25));
    }

    private void DrawToolPreview(DrawingContext dc)
    {
        var z = _zoom;
        if (_session.Tool == CanvasTool.Board)
        {
            var plan = _session.Plan;
            var outline = new Pen(Theme.AccentBrush, 2 / z);
            if (plan.BoardShape == BoardShape.Module)
                foreach (var m in plan.Modules) dc.DrawRectangle(null, outline, new Rect(m.X, m.Y, m.Width, m.Depth));
            else
                dc.DrawRectangle(null, new Pen(Theme.AccentBrush, 1.2 / z), new Rect(0, 0, plan.BoardWidth, plan.BoardHeight));
            var h = 6 / z;
            foreach (var g in ModuleLayout.Handles(plan))
                dc.DrawRectangle(Brushes.White, outline, new Rect(g.X - h, g.Y - h, 2 * h, 2 * h));
            // Griffe an den Schenkeln: Innenkante = Tiefe, freies Ende = Länge
            foreach (var g in BoardGeometry.LegGrips(plan))
                dc.DrawEllipse(g.Length ? Theme.AccentBrush : Brushes.White, outline, new Point(g.X, g.Y), h, h);
        }
        if (_polygon.Count > 0)
        {
            var pts = _polygon.Append((_lastWorld.X, _lastWorld.Y)).ToList();
            var fill = _session.Tool == CanvasTool.Water ? new SolidColorBrush(Color.FromArgb(0x60, 0x86, 0xBC, 0xE6)) : new SolidColorBrush(Color.FromArgb(0x60, 0x9C, 0xB8, 0x7E));
            dc.DrawGeometry(fill, new Pen(Theme.AccentBrush, 1.5 / z) { DashStyle = DashStyles.Dash }, PlanScene.Polygon(pts));
            foreach (var p in _polygon) dc.DrawEllipse(Brushes.White, new Pen(Theme.AccentBrush, 1.5 / z), new Point(p.X, p.Y), 4 / z, 4 / z);
        }
        if (_flexDraft is { Segments.Count: > 0 } draft)
        {
            var geo = TrackDrawing.Build(draft.Legs, 0);
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0xE0, 0x8A, 0x2E)), Math.Max(_gauge * 2.6, 4 / z)) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat }, geo);
            dc.DrawGeometry(null, new Pen(Theme.AccentBrush, 1.5 / z) { DashStyle = DashStyles.Dash }, geo);
            if (draft.Target is { } t) dc.DrawEllipse(null, new Pen(Theme.AccentBrush, 2 / z), new Point(t.Pose.X, t.Pose.Y), 10 / z, 10 / z);
        }
        if (_dimStart is { } a)
        {
            var b = Annotation.Snap(_session.Plan, _lastWorld.X, _lastWorld.Y, 12 / z);
            var preview = new SceneryItem { Kind = SceneryKind.Dimension, Points = [a, b], Width = Annotation.DefaultTextSize(_session.Plan.Gauge) * 1.5, Height = Annotation.DefaultTextSize(_session.Plan.Gauge) * 0.8 };
            var plan = new LayoutPlan();
            plan.Scenery.Add(preview);
            PlanScene.DrawAnnotations(dc, plan, z, preview.Id);
        }
        if (_drag == DragMode.Create && _session.Tool == CanvasTool.Building && _moved)
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x80, 0xB8, 0x5C, 0x45)), new Pen(Theme.AccentBrush, 1.5 / z), new Rect(_dragStartWorld, _lastWorld));
    }

    private void DrawGrid(DrawingContext dc, Rect board)
    {
        var corners = new[] { new Point(0, 0), new Point(ActualWidth, 0), new Point(ActualWidth, ActualHeight), new Point(0, ActualHeight) }.Select(ToWorld).ToList();
        var view = new Rect(new Point(corners.Min(p => p.X), corners.Min(p => p.Y)), new Point(corners.Max(p => p.X), corners.Max(p => p.Y)));
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
        var center = ToScreen(plan.BoardWidth / 2, plan.BoardHeight / 2);
        Dimension(dc, ToScreen(0, 0), ToScreen(plan.BoardWidth, 0), plan.BoardWidth, center);
        Dimension(dc, ToScreen(0, 0), ToScreen(0, plan.BoardHeight), plan.BoardHeight, center);
    }

    /// <summary>Maßkette entlang einer Plattenkante, 14 px nach außen versetzt; die Schrift bleibt lesbar, auch wenn die Ansicht gedreht ist.</summary>
    private void Dimension(DrawingContext dc, Point a, Point b, double length, Point center)
    {
        var d = b - a;
        if (d.Length < 1) return;
        var n = new Vector(-d.Y, d.X) / d.Length;
        var mid = a + d / 2;
        if ((mid - center) * n < 0) n = -n;
        var pen = new Pen(Theme.CanvasTextBrush, 1);
        Point a2 = a + n * 14, b2 = b + n * 14, m2 = mid + n * 14;
        dc.DrawLine(pen, a2, b2);
        dc.DrawLine(pen, a2 - n * 4, a2 + n * 4);
        dc.DrawLine(pen, b2 - n * 4, b2 + n * 4);
        var angle = Math.Atan2(d.Y, d.X) * 180 / Math.PI;
        if (angle > 90) angle -= 180;
        if (angle < -90) angle += 180;
        var text = Text($"{Fmt.Num(length, 0)} mm", 11, Theme.CanvasTextBrush);
        dc.PushTransform(new RotateTransform(angle, m2.X, m2.Y));
        dc.DrawRectangle(Theme.CanvasBrush, null, new Rect(m2.X - text.Width / 2 - 4, m2.Y - text.Height / 2, text.Width + 8, text.Height));
        dc.DrawText(text, new Point(m2.X - text.Width / 2, m2.Y - text.Height / 2));
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

    /// <summary>Ende eines Flexgleises zum Nachformen: offene Enden immer, verbundene nur, wenn das Flexgleis gewählt ist.</summary>
    private ConnectorRef? HitFlexEnd(Point screen)
    {
        ConnectorRef? best = null;
        var bestD = 9.0;
        foreach (var c in _session.Connections.All)
        {
            if (_session.Plan.Find(c.PieceId) is not { Def.IsFlex: true } || !(c.IsOpen || _session.Selection.Contains(c.PieceId))) continue;
            var d = (ToScreen(c.Pose.X, c.Pose.Y) - screen).Length;
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
        return best ?? TableShape.Hit(_session.Plan, world.X, world.Y);
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
        foreach (var s in plan.Signals)
        {
            var (x, y, heading) = Electrics.SignalPose(plan, s);
            foreach (var (hx, hy) in SignalFace.PlanHitPoints(x, y, heading, SignalFace.PlanScale(_gauge, _zoom), SignalFace.For(s.Type))) Try(s.Id, hx, hy);
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

        if (e.ChangedButton == MouseButton.Left && _rot != 0 && (screen - CompassCenter).Length <= 22)
        {
            SetViewRotation(0);
            e.Handled = true;
            return;
        }
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
            case CanvasTool.Board:
                CaptureMouse();
                if (_session.BeginBoardDrag(world.X, world.Y, 10 / _zoom)) _drag = DragMode.Board;
                else { _drag = DragMode.Pan; _panStartX = _ox; _panStartY = _oy; }
                return;
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
            case CanvasTool.Label:
                LabelRequested?.Invoke(Math.Round(world.X), Math.Round(world.Y));
                return;
            case CanvasTool.Dimension:
                var snap = Annotation.Snap(_session.Plan, world.X, world.Y, 12 / _zoom);
                if (_dimStart is not { } from) { _dimStart = snap; _session.Say(Lang.T("Maß: jetzt den zweiten Punkt anklicken (Gleisenden und Plattenecken rasten ein, Esc bricht ab).")); }
                else if (Math.Abs(snap.X - from.X) + Math.Abs(snap.Y - from.Y) > 1) { _dimStart = null; _session.AddDimension(from, snap); }
                InvalidateVisual();
                return;
            case CanvasTool.Tree:
                var size = _gauge * (2.6 + _random.NextDouble() * 1.4);
                _session.AddScenery(new SceneryItem { Kind = SceneryKind.Tree, X = Math.Round(world.X), Y = Math.Round(world.Y), Width = Math.Round(size), Height = Math.Round(size * 2.2), Name = Lang.T("Baum") });
                return;
            case CanvasTool.Feeder:
                // Klick auf eine vorhandene Einspeisung entfernt sie wieder
                if (HitSmallObject(screen, world) is { } fo && _session.Plan.Feeders.Any(f => f.Id == fo)) _session.RemoveObject(fo);
                else if (HitPiece(world) is { } fp) _session.AddFeeder(fp.Id);
                else _session.Say(Lang.T("Für eine Einspeisung bitte direkt auf ein Gleis klicken."));
                return;
            case CanvasTool.Isolation:
                if (HitConnector(screen, openOnly: false, radius: 14) is { } ic) _session.ToggleIsolation(ic.PieceId, ic.Index);
                else _session.Say(Lang.T("Für eine Trennstelle bitte auf eine Gleisverbindung (grüner Punkt) klicken."));
                return;
            case CanvasTool.Signal:
                // Klick auf ein vorhandenes Signal entfernt es wieder
                if (HitSmallObject(screen, world) is { } so && _session.Plan.Signals.Any(s => s.Id == so)) _session.RemoveObject(so);
                else if (HitConnector(screen, openOnly: false, radius: 14) is { } sc) _session.AddSignal(sc.PieceId, sc.Index);
                else _session.Say(Lang.T("Signale stehen an Gleisenden: bitte auf einen Punkt an einer Gleisverbindung klicken."));
                return;
            case CanvasTool.Flex:
                CaptureMouse();
                _flexDraft = _session.BeginFlexDraw(world.X, world.Y, _session.FlexTarget(world.X, world.Y, 12 / _zoom));
                _drag = DragMode.FlexDraw;
                return;
        }

        CaptureMouse();
        if (HitFlexEnd(screen) is { } fe)
        {
            if (fe.IsOpen) _session.SetActive(fe.PieceId, fe.Index);
            _flexEnd = fe;
            _reshaping = false;
            _drag = DragMode.FlexEnd;
            return;
        }
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
        if (_polygon.Count > 0 || _dimStart is not null) InvalidateVisual();
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
            case DragMode.Board:
                if (!_moved && (screen - _dragStartScreen).Length < 3) return;
                _moved = true;
                _session.BoardDragTo(world.X, world.Y);
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
            case DragMode.FlexDraw when _flexDraft is { } draft:
                if (!_moved && (screen - _dragStartScreen).Length < 4) return;
                _moved = true;
                draft.Update(world.X, world.Y, _session.FlexTarget(world.X, world.Y, 14 / _zoom, exclude: draft.From));
                if (draft.Segments.Count > 0) _session.Say(FlexTrack.Describe(draft.Segments) + (draft.Target is not null ? Lang.T(" – rastet am Gleisende ein") : ""));
                InvalidateVisual();
                break;
            case DragMode.FlexEnd when _flexEnd is { } fe:
                if (!_moved && (screen - _dragStartScreen).Length < 3) return;
                _moved = true;
                if (!_reshaping)
                {
                    if (!_session.BeginReshape(fe.PieceId, fe.Index)) { _drag = DragMode.None; return; }
                    _reshaping = true;
                }
                var shaped = _session.Reshape(world.X, world.Y, _session.FlexTarget(world.X, world.Y, 14 / _zoom, fe.PieceId));
                if (shaped.Count > 0)
                {
                    _reshapeLast = shaped;
                    _session.Say(FlexTrack.Describe(shaped));
                }
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
            case DragMode.Board:
                _session.EndBoardDrag();
                break;
            case DragMode.Band:
                if (!_moved || _band is not { } band)
                {
                    _session.ClearSelection();
                    break;
                }
                _band = null;
                var ids = _session.Plan.Pieces.Where(p => { var m = p.Middle(); return band.Contains(ToScreen(m.X, m.Y)); }).Select(p => p.Id).ToList();
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
            case DragMode.FlexDraw:
                var draft = _flexDraft;
                _flexDraft = null;
                if (_moved && draft is not null) _session.FinishFlexDraw(draft);
                else _session.Say(Lang.T("Flexgleis ziehen: an einem offenen Gleisende (oder frei) drücken und mit gedrückter Maustaste ziehen."));
                InvalidateVisual();
                break;
            case DragMode.FlexEnd:
                if (_reshaping) _session.EndReshape(_reshapeLast);
                _reshaping = false;
                _reshapeLast = [];
                _flexEnd = null;
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
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            // Strg + Mausrad dreht die Ansicht (mit Umschalt in feinen Schritten)
            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1 : 5;
            RotateViewBy(Math.Sign(e.Delta) * step, e.GetPosition(this));
        }
        else ZoomBy(Math.Exp(e.Delta * 0.0015), e.GetPosition(this));
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
