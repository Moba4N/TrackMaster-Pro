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
    private readonly TextBlock _status = new() { Foreground = Theme.TextSecondaryBrush, VerticalAlignment = VerticalAlignment.Center };

    // Rundumsicht
    private Point3D _target;
    private double _yaw = -60, _pitch = 35, _distance;
    private Point? _dragStart;
    private bool _panning;
    private (double Yaw, double Pitch, Point3D Target) _dragOrigin;

    // Mitfahren
    private RidePath? _path;
    private double _position;
    private TimeSpan _lastFrame;
    private readonly int? _startPiece;

    public View3DWindow(LayoutPlan plan, int? startPiece, View3DMode mode)
    {
        _plan = plan;
        _startPiece = startPiece;
        Title = $"3D-Ansicht – {plan.Name}";
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
        _viewport.Children.Add(new ModelVisual3D { Content = models });
        _viewport.Camera = _camera;

        var (x0, y0, x1, y1) = plan.Bounds();
        _target = new Point3D((x0 + x1) / 2, 0, (y0 + y1) / 2);
        _distance = Math.Max(x1 - x0, y1 - y0) * 1.15;

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 12, 8) };
        var orbit = Theme.Button("Rundumsicht", "Cube");
        orbit.Click += (_, _) => StopRide();
        toolbar.Children.Add(orbit);
        _rideButton.Content = "Mitfahren";
        _rideButton.Height = 30;
        _rideButton.Padding = new Thickness(10, 0, 10, 0);
        _rideButton.Margin = new Thickness(0, 0, 16, 6);
        _rideButton.Template = Theme.RoundedTemplate();
        _rideButton.BorderThickness = new Thickness(1);
        _rideButton.Click += (_, _) => { if (_rideButton.IsChecked == true) StartRide(); else StopRide(); };
        toolbar.Children.Add(_rideButton);
        toolbar.Children.Add(Theme.Label("Tageszeit", 12, false, Theme.TextSecondaryBrush));
        toolbar.Children[^1].SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        toolbar.Children.Add(new Border { Width = 8 });
        toolbar.Children.Add(_time);
        toolbar.Children.Add(_timeText);
        toolbar.Children.Add(Theme.Label("Geschwindigkeit", 12, false, Theme.TextSecondaryBrush));
        toolbar.Children[^1].SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        toolbar.Children.Add(new Border { Width = 8 });
        toolbar.Children.Add(_speed);
        toolbar.Children.Add(_speedText);
        toolbar.Children.Add(_status);
        var top = new Border { Background = Brushes.White, BorderBrush = Theme.BorderStrongBrush, BorderThickness = new Thickness(0, 0, 0, 1), Child = toolbar };
        DockPanel.SetDock(top, Dock.Top);

        var stage = new Grid();
        stage.Children.Add(_sky);
        stage.Children.Add(_viewport);
        var help = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false,
            Child = Theme.Label("Linke Maustaste: drehen · Rechte Maustaste: verschieben · Mausrad: zoomen · Mitfahren startet am gewählten Gleis", 11.5, false, Theme.TextSecondaryBrush),
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
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
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
        var pitch = Math.Clamp(_pitch, 5, 89) * Math.PI / 180;
        var pos = new Point3D(
            _target.X + _distance * Math.Cos(pitch) * Math.Sin(yaw),
            _target.Y + _distance * Math.Sin(pitch),
            _target.Z + _distance * Math.Cos(pitch) * Math.Cos(yaw));
        _camera.Position = pos;
        _camera.LookDirection = _target - pos;
        _camera.UpDirection = new Vector3D(0, 1, 0);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_rideButton.IsChecked == true) return;
        var input = (IInputElement)sender;
        _dragStart = e.GetPosition(input);
        _panning = e.ChangedButton is MouseButton.Right or MouseButton.Middle;
        _dragOrigin = (_yaw, _pitch, _target);
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
        else
        {
            _yaw = _dragOrigin.Yaw - dx * 0.3;
            _pitch = Math.Clamp(_dragOrigin.Pitch + dy * 0.25, 5, 89);
        }
        UpdateCamera();
    }

    // ---------- Mitfahren ----------

    private void StartRide()
    {
        _path ??= RidePath.Build(_plan, _startPiece);
        if (_path.IsEmpty)
        {
            _status.Text = "Keine Gleise zum Mitfahren.";
            _rideButton.IsChecked = false;
            return;
        }
        _status.Text = _path.Closed ? "Rundkurs – Esc schließt das Fenster" : "Die Fahrt wendet an Prellböcken und Gleisenden.";
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering -= OnFrame;
        CompositionTarget.Rendering += OnFrame;
        PlaceRideCamera();
    }

    private void StopRide()
    {
        CompositionTarget.Rendering -= OnFrame;
        _rideButton.IsChecked = false;
        _status.Text = "";
        _camera.FieldOfView = 50;
        UpdateCamera();
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (_lastFrame == TimeSpan.Zero) { _lastFrame = now; return; }
        var dt = Math.Min((now - _lastFrame).TotalSeconds, 0.1);
        _lastFrame = now;
        // Vorbildgeschwindigkeit im Maßstab
        _position += RidePath.ModelSpeed(_speed.Value, _plan.Gauge.Scale) * dt;
        if (_path is { } path && _position >= path.Length) _position = path.Closed ? _position % path.Length : 0;
        PlaceRideCamera();
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
