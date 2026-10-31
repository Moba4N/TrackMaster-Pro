using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using TrackMaster.Core;

namespace TrackMaster.App;

public enum View3DMode { Orbit, Ride, Evening }

/// <summary>
/// 3D-Ansicht der Anlage: Rundumsicht (ziehen dreht, rechte Taste verschiebt, Mausrad zoomt),
/// Mitfahren im Führerstand und Licht nach Tageszeit.
/// </summary>
public sealed class View3DWindow : Window
{
    private readonly LayoutPlan _plan;
    private readonly Scene3D _scene;
    private readonly Viewport3D _viewport = new() { ClipToBounds = true };
    private readonly PerspectiveCamera _camera = new() { FieldOfView = 50, NearPlaneDistance = 2, FarPlaneDistance = 200000 };
    private readonly AmbientLight _ambient = new();
    private readonly DirectionalLight _sun = new();
    private readonly DirectionalLight _fill = new();
    private readonly Border _sky = new();
    private readonly Slider _time = new() { Minimum = 5, Maximum = 23, Value = 13, Width = 180, SmallChange = 0.25, LargeChange = 1, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _timeText = new() { Width = 70, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 16, 0) };
    private readonly Slider _speed = new() { Minimum = 10, Maximum = 160, Value = 60, Width = 140, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _speedText = new() { Width = 70, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 16, 0) };
    private readonly ToggleButton _rideButton = new();
    private readonly ToggleButton _trainButton = new();
    private readonly Button _switchButton;
    private readonly Model3DGroup _trainModel = new();
    private readonly Train3D _train;
    private readonly Dictionary<int, int> _routes = [];
    private readonly TextBlock _status = new() { Foreground = Theme.TextSecondaryBrush, VerticalAlignment = VerticalAlignment.Center };

    // Rundumsicht
    private Point3D _target;
    private double _yaw = -60, _pitch = 35, _roll, _distance;
    // Neigung frei von oben bis unter die Platte; mit dem Kippen um die Blickachse lässt sich die Anlage um alle drei Achsen drehen
    private const double PitchMin = -89, PitchMax = 89;
    private Point? _dragStart;
    private bool _panning;
    private (double Yaw, double Pitch, double Roll, Point3D Target) _dragOrigin;

    // Mitfahren
    private RidePath? _path;
    private double _position;
    private TimeSpan _lastFrame;
    private readonly int? _startPiece;

    public View3DWindow(LayoutPlan plan, int? startPiece, View3DMode mode)
    {
        _plan = plan;
        _startPiece = startPiece;
        Title = Lang.F($"3D-Ansicht – {plan.Name}");
        Width = 1280; Height = 800; MinWidth = 640; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = Theme.Font;
        FontSize = 12 * Theme.FontScale;
        Background = Theme.WindowBrush;
        ShowInTaskbar = false;

        _scene = new Scene3D(plan);
        var models = new Model3DGroup();
        models.Children.Add(_ambient);
        models.Children.Add(_sun);
        models.Children.Add(_fill);
        models.Children.Add(_scene.Model);
        models.Children.Add(_trainModel);
        _train = new Train3D(plan);
        _viewport.Children.Add(new ModelVisual3D { Content = models });
        _viewport.Camera = _camera;

        var (x0, y0, x1, y1) = plan.Bounds();
        _target = new Point3D((x0 + x1) / 2, 0, (y0 + y1) / 2);
        _distance = Math.Max(x1 - x0, y1 - y0) * 1.15;

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 12, 8) };
        var orbit = Theme.Button(Lang.T("Rundumsicht"), "Cube");
        orbit.Click += (_, _) => { StopRide(); };
        toolbar.Children.Add(orbit);
        _rideButton.Content = Lang.T("Mitfahren");
        _rideButton.Height = 30;
        _rideButton.Padding = new Thickness(10, 0, 10, 0);
        _rideButton.Margin = new Thickness(0, 0, 16, 6);
        _rideButton.Template = Theme.RoundedTemplate();
        _rideButton.BorderThickness = new Thickness(1);
        _rideButton.Click += (_, _) => { if (_rideButton.IsChecked == true) StartRide(); else StopRide(); };
        toolbar.Children.Add(_rideButton);
        _trainButton.Content = Lang.T("Zug fahren");
        _trainButton.Height = 30;
        _trainButton.Padding = new Thickness(10, 0, 10, 0);
        _trainButton.Margin = new Thickness(0, 0, 8, 6);
        _trainButton.Template = Theme.RoundedTemplate();
        _trainButton.BorderThickness = new Thickness(1);
        _trainButton.ToolTip = Lang.T("Ein Zug fährt über die Anlage, die Ansicht bleibt frei drehbar.");
        _trainButton.Click += (_, _) => { if (_trainButton.IsChecked == true) StartTrain(); else StopTrain(); };
        toolbar.Children.Add(_trainButton);
        _switchButton = Theme.Button(Lang.T("Weiche umstellen"), "Swap");
        _switchButton.ToolTip = Lang.T("Stellt die nächste Weiche vor dem Zug um (W).");
        _switchButton.Click += (_, _) => ToggleNextSwitch();
        _switchButton.IsEnabled = false;
        toolbar.Children.Add(_switchButton);
        toolbar.Children.Add(Theme.Label(Lang.T("Tageszeit"), 12, false, Theme.TextSecondaryBrush));
        toolbar.Children[^1].SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        toolbar.Children.Add(new Border { Width = 8 });
        toolbar.Children.Add(_time);
        toolbar.Children.Add(_timeText);
        toolbar.Children.Add(Theme.Label(Lang.T("Geschwindigkeit"), 12, false, Theme.TextSecondaryBrush));
        toolbar.Children[^1].SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        toolbar.Children.Add(new Border { Width = 8 });
        toolbar.Children.Add(_speed);
        toolbar.Children.Add(_speedText);
        toolbar.Children.Add(_status);
        var second = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 12, 8) };
        var withScenery = new RadioButton { Content = Lang.T("Mit Gelände"), IsChecked = true, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = Lang.T("Gelände, Gewässer, Gebäude und Bäume zeigen") };
        var tracksOnly = new RadioButton { Content = Lang.T("Nur Gleisplan"), Margin = new Thickness(0, 0, 24, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = Lang.T("Nur Platte, Gleise und Pfeiler zeigen") };
        withScenery.Checked += (_, _) => _scene.Rebuild(_plan, true);
        tracksOnly.Checked += (_, _) => _scene.Rebuild(_plan, false);
        second.Children.Add(withScenery);
        second.Children.Add(tracksOnly);
        second.Children.Add(Theme.Label(Lang.T("Drehen:"), 12, false, Theme.TextSecondaryBrush));
        second.Children[^1].SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        foreach (var (text, tip, axis) in new[] { ("X ↻", Lang.T("Um die Querachse kippen (X)"), 0), ("Y ↻", Lang.T("Um die Hochachse drehen (Y)"), 1), ("Z ↻", Lang.T("Um die Blickachse drehen (Z)"), 2), (Lang.T("Gerade"), "", -1) })
        {
            var b = Theme.Button(text);
            b.Margin = new Thickness(8, 0, 0, 0);
            if (tip.Length > 0) b.ToolTip = tip;
            var a = axis;
            b.Click += (_, _) => Turn(a, 15);
            second.Children.Add(b);
        }
        var bars = new StackPanel();
        bars.Children.Add(toolbar);
        bars.Children.Add(second);
        var top = new Border { Background = Brushes.White, BorderBrush = Theme.BorderStrongBrush, BorderThickness = new Thickness(0, 0, 0, 1), Child = bars };
        DockPanel.SetDock(top, Dock.Top);

        var stage = new Grid();
        stage.Children.Add(_sky);
        stage.Children.Add(_viewport);
        var help = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false,
            Child = Theme.Label(Lang.T("Linke Maustaste: drehen und kippen (auch von unten) · Strg + linke Maustaste: um die Blickachse drehen · Rechte Maustaste: verschieben · Mausrad: zoomen · Mitfahren startet am gewählten Gleis"), 11.5, false, Theme.TextSecondaryBrush),
        };
        stage.Children.Add(help);
        // Eingaben auf einer durchsichtigen Fläche, damit auch Klicks in den Himmel ankommen
        var input = new Border { Background = Brushes.Transparent };
        stage.Children.Add(input);
        input.MouseDown += OnMouseDown;
        input.MouseMove += OnMouseMove;
        input.MouseUp += (_, _) => { _dragStart = null; input.ReleaseMouseCapture(); };
        input.MouseWheel += (_, e) => { if (_rideButton.IsChecked != true) { _distance = Math.Clamp(_distance * Math.Exp(-e.Delta * 0.0012), 50, 100000); UpdateCamera(); } };

        var root = new DockPanel();
        root.Children.Add(top);
        root.Children.Add(stage);
        Content = root;

        _time.ValueChanged += (_, _) => ApplyTime();
        _speed.ValueChanged += (_, _) => _speedText.Text = $"{_speed.Value:0} km/h";
        _speedText.Text = $"{_speed.Value:0} km/h";
        if (mode == View3DMode.Evening) _time.Value = 19.5;
        ApplyTime();
        UpdateCamera();
        Loaded += (_, _) => { if (mode == View3DMode.Ride) { _rideButton.IsChecked = true; StartRide(); } };
        Closed += (_, _) => CompositionTarget.Rendering -= OnFrame;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            if (e.Key == Key.W) ToggleNextSwitch();
        };
    }

    // ---------- Licht ----------

    private void ApplyTime()
    {
        var h = _time.Value;
        _timeText.Text = DayLight.TimeText(h);
        var light = DayLight.At(h);
        static Color C(Rgb c) => Color.FromRgb(c.R, c.G, c.B);
        _sun.Color = C(light.Sun);
        _sun.Direction = new Vector3D(light.SunDirection.X, light.SunDirection.Y, light.SunDirection.Z);
        _fill.Color = C(light.Fill);
        _fill.Direction = new Vector3D(light.FillDirection.X, light.FillDirection.Y, light.FillDirection.Z);
        _ambient.Color = C(light.Ambient);
        _sky.Background = new LinearGradientBrush(C(light.SkyTop), C(light.SkyBottom), 90);
        _scene.WindowGlow.Color = light.WindowsLit ? C(DayLight.WindowGlow) : Colors.Black;
    }

    // ---------- Kamera ----------

    private void UpdateCamera()
    {
        var yaw = _yaw * Math.PI / 180;
        var pitch = Math.Clamp(_pitch, PitchMin, PitchMax) * Math.PI / 180;
        var pos = new Point3D(
            _target.X + _distance * Math.Cos(pitch) * Math.Sin(yaw),
            _target.Y + _distance * Math.Sin(pitch),
            _target.Z + _distance * Math.Cos(pitch) * Math.Cos(yaw));
        var look = _target - pos;
        var forward = look; forward.Normalize();
        var right = Vector3D.CrossProduct(forward, new Vector3D(0, 1, 0)); right.Normalize();
        var up = Vector3D.CrossProduct(right, forward);
        var roll = _roll * Math.PI / 180;
        _camera.Position = pos;
        _camera.LookDirection = look;
        _camera.UpDirection = up * Math.Cos(roll) + right * Math.Sin(roll);
    }

    private static double Wrap(double degrees) => ((degrees + 540) % 360 + 360) % 360 - 180;

    /// <summary>Drehen per Knopf: 0 = X (kippen), 1 = Y (Hochachse), 2 = Z (Blickachse), −1 = gerade ausrichten.</summary>
    private void Turn(int axis, double degrees)
    {
        if (_rideButton.IsChecked == true) return;
        switch (axis)
        {
            case 0: _pitch = Math.Clamp(_pitch + degrees, PitchMin, PitchMax); break;
            case 1: _yaw = Wrap(_yaw + degrees); break;
            case 2: _roll = Wrap(_roll + degrees); break;
            default: (_yaw, _pitch, _roll) = (-60, 35, 0); break;
        }
        UpdateCamera();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_rideButton.IsChecked == true) return;
        var input = (IInputElement)sender;
        _dragStart = e.GetPosition(input);
        _panning = e.ChangedButton is MouseButton.Right or MouseButton.Middle;
        _dragOrigin = (_yaw, _pitch, _roll, _target);
        input.CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start) return;
        var p = e.GetPosition((IInputElement)sender);
        var dx = p.X - start.X;
        var dy = p.Y - start.Y;
        if (_panning)
        {
            var yaw = _yaw * Math.PI / 180;
            var scale = _distance / 900;
            var right = new Vector3D(Math.Cos(yaw), 0, -Math.Sin(yaw));
            var forward = new Vector3D(-Math.Sin(yaw), 0, -Math.Cos(yaw));
            _target = _dragOrigin.Target - right * dx * scale + forward * dy * scale;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            _roll = Wrap(_dragOrigin.Roll + dx * 0.3);
        else
        {
            _yaw = _dragOrigin.Yaw - dx * 0.3;
            _pitch = Math.Clamp(_dragOrigin.Pitch + dy * 0.25, PitchMin, PitchMax);
        }
        UpdateCamera();
    }

    // ---------- Mitfahren ----------

    private bool Moving => _rideButton.IsChecked == true || _trainButton.IsChecked == true;

    private bool EnsurePath()
    {
        _path ??= RidePath.Build(_plan, _startPiece, _routes);
        if (!_path.IsEmpty) return true;
        _status.Text = Lang.T("Keine Gleise zum Mitfahren.");
        return false;
    }

    private void StartMoving()
    {
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering -= OnFrame;
        CompositionTarget.Rendering += OnFrame;
        _switchButton.IsEnabled = true;
        UpdateStatus();
    }

    private void StopMovingIfIdle()
    {
        if (Moving) return;
        CompositionTarget.Rendering -= OnFrame;
        _switchButton.IsEnabled = false;
        _status.Text = "";
    }

    private void StartRide()
    {
        if (!EnsurePath()) { _rideButton.IsChecked = false; return; }
        _trainModel.Children.Clear();
        StartMoving();
        PlaceRideCamera();
    }

    private void StopRide()
    {
        _rideButton.IsChecked = false;
        _camera.FieldOfView = 50;
        UpdateCamera();
        StopMovingIfIdle();
    }

    private void StartTrain()
    {
        if (!EnsurePath()) { _trainButton.IsChecked = false; return; }
        // Der Zug startet mit dem letzten Wagen am Anfang des Wegs
        if (_position < _train.Length) _position = Math.Min(_train.Length, _path!.Length);
        StartMoving();
        PlaceTrain();
    }

    private void StopTrain()
    {
        _trainButton.IsChecked = false;
        _trainModel.Children.Clear();
        StopMovingIfIdle();
    }

    /// <summary>Stellt die nächste spitz befahrene Weiche vor dem Zug um; der Weg dahinter wird neu bestimmt.</summary>
    private void ToggleNextSwitch()
    {
        if (!Moving || _path?.NextSwitch(_position) is not { } next || _plan.Find(next.PieceId) is not { } piece) return;
        _routes[piece.Id] = ((_routes.TryGetValue(piece.Id, out var r) ? r : 0) + 1) % RidePath.RouteCount(piece);
        _path = RidePath.Build(_plan, _startPiece, _routes);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_path is null) return;
        var text = _path.Closed ? Lang.T("Rundkurs") : Lang.T("Wendet an Prellböcken und Gleisenden");
        if (_path.NextSwitch(_position) is { } next && _plan.Find(next.PieceId) is { } piece)
        {
            var route = _routes.TryGetValue(piece.Id, out var r) ? r : 0;
            var setting = RidePath.RouteCount(piece) > 2 ? Lang.F($"Stellung {route + 1}") : route == 0 ? Lang.T("Grundstellung") : Lang.T("abzweigend");
            text += Lang.F($" · Nächste Weiche Nr. {piece.Id} in {Fmt.Num((next.Distance - _position) / 10, 0)} cm: {setting} (W stellt um)");
        }
        _status.Text = text;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (_lastFrame == TimeSpan.Zero) { _lastFrame = now; return; }
        var dt = Math.Min((now - _lastFrame).TotalSeconds, 0.1);
        _lastFrame = now;
        // Vorbildgeschwindigkeit im Maßstab
        var before = _path?.NextSwitch(_position);
        _position += RidePath.ModelSpeed(_speed.Value, _plan.Gauge.Scale) * dt;
        if (_path is { } path && _position >= path.Length) _position = path.Closed ? _position % path.Length : Math.Min(_train.Length, path.Length);
        if (!Equals(before, _path?.NextSwitch(_position)) || (now.TotalMilliseconds % 500) < 20) UpdateStatus();
        if (_rideButton.IsChecked == true) PlaceRideCamera();
        else if (_trainButton.IsChecked == true) PlaceTrain();
    }

    private void PlaceTrain()
    {
        if (_path is null) return;
        _scene.Fill(_trainModel, _train.Build(_path, _position));
    }

    private void PlaceRideCamera()
    {
        if (_path is null) return;
        var (eye, target) = _path.Camera(_position, _plan.Gauge.TrackWidth);
        _camera.FieldOfView = 65;
        _camera.Position = new Point3D(eye.X, eye.Y, eye.Z);
        var look = new Vector3D(target.X - eye.X, target.Y - eye.Y, target.Z - eye.Z);
        if (look.Length > 0.001) _camera.LookDirection = look;
        _camera.UpDirection = new Vector3D(0, 1, 0);
    }
}
