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
    private List<(double X, double Y, double Z)> _path = [];
    private double[] _distances = [];
    private bool _pathClosed;
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
        FontSize = 12;
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
        _timeText.Text = $"{(int)h:00}:{(int)((h - (int)h) * 60):00} Uhr";
        // Sonnenhöhe: Aufgang 6 Uhr, Höchststand 13 Uhr, Untergang 20 Uhr
        var dayPhase = Math.Clamp((h - 6) / 14, 0, 1);
        var elevation = Math.Sin(dayPhase * Math.PI);
        var night = h < 6 || h > 20.5;
        var azimuth = (h - 13) / 14 * Math.PI;
        var warm = 1 - Math.Clamp(elevation * 2.2, 0, 1);
        Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        var sunColor = night ? Color.FromRgb(0x30, 0x3A, 0x60) : Mix(Color.FromRgb(0xFF, 0xF6, 0xE8), Color.FromRgb(0xFF, 0xA8, 0x60), warm);
        var intensity = night ? 0.35 : 0.45 + 0.55 * elevation;
        _sun.Color = Scale(sunColor, intensity);
        _sun.Direction = new Vector3D(-Math.Sin(azimuth), -Math.Max(elevation, 0.15), -Math.Cos(azimuth) * 0.6 - 0.4);
        _fill.Color = Scale(night ? Color.FromRgb(0x20, 0x28, 0x48) : Color.FromRgb(0x90, 0xA0, 0xB8), night ? 0.4 : 0.35);
        _fill.Direction = new Vector3D(0.6, -0.5, 0.7);
        _ambient.Color = night ? Color.FromRgb(0x22, 0x26, 0x3A) : Scale(Mix(Color.FromRgb(0x70, 0x74, 0x7A), Color.FromRgb(0x6A, 0x58, 0x50), warm), 0.6 + 0.4 * elevation);
        var skyTop = night ? Color.FromRgb(0x08, 0x0C, 0x20) : Mix(Color.FromRgb(0x5E, 0x9C, 0xE0), Color.FromRgb(0x3A, 0x4A, 0x80), warm);
        var skyBottom = night ? Color.FromRgb(0x1C, 0x24, 0x40) : Mix(Color.FromRgb(0xCF, 0xE4, 0xF7), Color.FromRgb(0xF4, 0xA2, 0x6A), warm);
        _sky.Background = new LinearGradientBrush(skyTop, skyBottom, 90);
        _scene.WindowGlow.Color = h > 19 || h < 7 ? Color.FromRgb(0xFF, 0xD0, 0x70) : Colors.Black;
    }

    private static Color Scale(Color c, double f) => Color.FromRgb((byte)Math.Min(255, c.R * f), (byte)Math.Min(255, c.G * f), (byte)Math.Min(255, c.B * f));

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
        if (_path.Count == 0) BuildPath();
        if (_path.Count < 2)
        {
            _status.Text = "Keine Gleise zum Mitfahren.";
            _rideButton.IsChecked = false;
            return;
        }
        _status.Text = _pathClosed ? "Rundkurs – Esc schließt das Fenster" : "Die Fahrt wendet an Prellböcken und Gleisenden.";
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
        // Vorbildgeschwindigkeit im Maßstab: km/h → mm/s geteilt durch den Maßstab
        var scale = ScaleDivisor(_plan.Gauge.Scale);
        _position += _speed.Value / 3.6 * 1000 / scale * dt;
        var total = _distances[^1];
        if (_position >= total) _position = _pathClosed ? _position % total : 0;
        PlaceRideCamera();
    }

    private static double ScaleDivisor(string scale)
        => double.TryParse(scale.Split(':').Last().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 87;

    private (double X, double Y, double Z) PointAt(double s)
    {
        var total = _distances[^1];
        if (_pathClosed) s = ((s % total) + total) % total;
        else s = Math.Clamp(s, 0, total);
        var i = Array.BinarySearch(_distances, s);
        if (i < 0) i = ~i;
        i = Math.Clamp(i, 1, _distances.Length - 1);
        var seg = _distances[i] - _distances[i - 1];
        var t = seg <= 0 ? 0 : (s - _distances[i - 1]) / seg;
        var a = _path[i - 1];
        var b = _path[i];
        return (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
    }

    private void PlaceRideCamera()
    {
        var g = _plan.Gauge.TrackWidth;
        var eye = PointAt(_position);
        var ahead = PointAt(_position + g * 14);
        var eyeHeight = g * 2.6;
        _camera.FieldOfView = 65;
        _camera.Position = new Point3D(eye.X, eye.Z + eyeHeight, eye.Y);
        var look = new Point3D(ahead.X, ahead.Z + eyeHeight * 0.85, ahead.Y) - _camera.Position;
        if (look.Length > 0.001) _camera.LookDirection = look;
        _camera.UpDirection = new Vector3D(0, 1, 0);
    }

    /// <summary>Fahrweg ab dem gewählten Gleis: an Weichen geradeaus, an Prellböcken und offenen Enden wenden.</summary>
    private void BuildPath()
    {
        var plan = _plan;
        if (plan.Pieces.Count == 0) return;
        var conn = Connectivity.Analyze(plan);
        var piece = (_startPiece is { } id ? plan.Find(id) : null) ?? plan.Pieces.FirstOrDefault(p => p.Def.Kind == PieceKind.Straight) ?? plan.Pieces[0];
        var entry = 0;
        var limit = plan.Pieces.Sum(p => p.Def.TrackLength) * 3 + 1000;
        double length = 0;
        var visits = new HashSet<(int, int)>();
        var points = new List<(double, double, double)>();
        var step = plan.Gauge.TrackWidth;
        var startState = (piece.Id, entry);
        while (length < limit)
        {
            if (!visits.Add((piece.Id, entry)) && (piece.Id, entry) == startState)
            {
                _pathClosed = true;
                break;
            }
            var d = piece.Def;
            var count = TrackGeometry.LocalConnectors(d, piece.Flipped).Count;
            var exit = d.Kind switch
            {
                PieceKind.Crossing => entry switch { 0 => 1, 1 => 0, 2 => 3, _ => 2 },
                PieceKind.Buffer => 0,
                _ when count == 1 => 0,
                _ => entry == 0 ? 1 : 0,
            };
            var (leg, forward) = d.Kind switch
            {
                PieceKind.Switch => entry == 0 ? (exit - 1, true) : (entry - 1, false),
                PieceKind.Crossing => (entry / 2, entry % 2 == 0),
                PieceKind.Buffer => (0, true),
                _ => (0, entry == 0),
            };
            var l = piece.WorldLegs()[leg];
            var n = Math.Max(2, (int)Math.Ceiling(l.TrackLength / step));
            for (var i = 0; i <= n; i++)
            {
                var t = forward ? (double)i / n : 1 - (double)i / n;
                var q = l.At(t);
                points.Add((q.X, q.Y, piece.HeightAt(leg, t)));
            }
            if (d.Kind == PieceKind.Buffer)
            {
                // Am Prellbock zurück
                for (var i = n; i >= 0; i--)
                {
                    var q = l.At((double)i / n);
                    points.Add((q.X, q.Y, piece.HeightAt(leg, (double)i / n)));
                }
            }
            length += l.TrackLength;
            if (conn.Find(piece.Id, exit)?.Link is { } next)
            {
                piece = plan.Find(next.PieceId)!;
                entry = next.Index;
            }
            else
            {
                // Offenes Ende: auf demselben Gleis zurückfahren
                entry = exit;
            }
        }
        // Doppelte Punkte entfernen und Längen aufaddieren
        var path = new List<(double X, double Y, double Z)>();
        foreach (var p in points)
            if (path.Count == 0 || Math.Abs(path[^1].X - p.Item1) + Math.Abs(path[^1].Y - p.Item2) > 0.01) path.Add(p);
        _path = path;
        _distances = new double[path.Count];
        for (var i = 1; i < path.Count; i++)
        {
            var a = path[i - 1];
            var b = path[i];
            _distances[i] = _distances[i - 1] + Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y) + (b.Z - a.Z) * (b.Z - a.Z));
        }
        _position = 0;
    }
}
