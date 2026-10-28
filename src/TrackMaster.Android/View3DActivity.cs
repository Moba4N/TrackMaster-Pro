using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.Opengl;
using Android.OS;
using Android.Views;
using Android.Widget;
using Java.Nio;
using TrackMaster.Core;
using EGLConfig = Javax.Microedition.Khronos.Egl.EGLConfig;
using GLMatrix = Android.Opengl.Matrix;
using IGL10 = Javax.Microedition.Khronos.Opengles.IGL10;

namespace TrackMaster.Mobile;

public enum View3DMode { Orbit, Ride, Evening }

/// <summary>
/// 3D-Ansicht mit OpenGL ES: Rundumsicht (ein Finger dreht, zwei Finger zoomen und verschieben),
/// Mitfahren im Führerstand und Licht nach Tageszeit. Modell, Licht und Fahrweg kommen aus dem gemeinsamen Kern.
/// </summary>
[Activity(Name = "de.moba4n.trackmaster.View3DActivity", Label = "3D-Ansicht", Theme = "@android:style/Theme.Material.NoActionBar",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.KeyboardHidden)]
public sealed class View3DActivity : Activity
{
    /// <summary>Plan aus dem Hauptfenster (gleicher Prozess).</summary>
    public static LayoutPlan? Plan { get; set; }

    private LayoutPlan _plan = null!;
    private GLSurfaceView _view = null!;
    private SceneRenderer _renderer = null!;
    private Button _ride = null!, _trainButton = null!, _switch = null!;
    private bool _riding, _trainOn;
    private Train3D _train = null!;
    private readonly Dictionary<int, int> _routes = [];
    private long _lastStatus;
    private SeekBar _time = null!, _speed = null!;
    private TextView _timeText = null!, _speedText = null!, _status = null!;
    private RidePath? _path;
    private double _position;
    private long _lastFrame;
    private int? _startPiece;
    private volatile float _speedKmh = 60;

    // Rundumsicht
    private double _yaw = -60, _pitch = 35, _distance;
    private Vec3 _target;
    private ScaleGestureDetector _scale = null!;
    private float _lastX, _lastY;
    private bool _multi;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _plan = Plan ?? SamplePlan.Build();
        var mode = (View3DMode)Intent!.GetIntExtra("mode", 0);
        var start = Intent.GetIntExtra("start", -1);
        _startPiece = start >= 0 ? start : null;
        Ui.Density = Resources!.DisplayMetrics!.Density;

        var (x0, y0, x1, y1) = _plan.Bounds();
        _target = new Vec3((x0 + x1) / 2, 0, (y0 + y1) / 2);
        _distance = Math.Max(x1 - x0, y1 - y0) * 1.15;

        var root = new FrameLayout(this);
        _view = new GLSurfaceView(this);
        _view.SetEGLContextClientVersion(2);
        _view.SetEGLConfigChooser(8, 8, 8, 8, 24, 0);
        _renderer = new SceneRenderer(new Scene3DModel(_plan));
        _train = new Train3D(_plan);
        _view.SetRenderer(_renderer);
        _view.RenderMode = Rendermode.WhenDirty;
        root.AddView(_view, new FrameLayout.LayoutParams(-1, -1));

        // Bedienleiste oben
        var bar = Ui.Column(this, 4);
        bar.SetBackgroundColor(Color.Argb(0xF0, 0xFF, 0xFF, 0xFF));
        bar.SetPadding(Ui.Dp(10), Ui.Dp(6), Ui.Dp(10), Ui.Dp(6));
        var row1 = Ui.Row(this, 8);
        row1.AddView(Ui.Button(this, Lang.T("Rundumsicht"), StopRide));
        _ride = Ui.Button(this, Lang.T("Mitfahren"), () => { if (_riding) StopRide(); else StartRide(); });
        row1.AddView(_ride);
        _trainButton = Ui.Button(this, Lang.T("Zug"), () => { if (_trainOn) StopTrain(); else StartTrain(); });
        row1.AddView(_trainButton);
        _switch = Ui.Button(this, Lang.T("Weiche"), ToggleNextSwitch);
        _switch.Enabled = false;
        row1.AddView(_switch);
        _status = Ui.Label(this, "", 12.5, color: Ui.TextSecondary);
        row1.AddView(_status, Ui.Weight(1, vertical: false));
        row1.AddView(Ui.Button(this, Lang.T("Schließen"), Finish));
        bar.AddView(row1, Ui.Fill());
        var row2 = Ui.Row(this, 6);
        row2.AddView(Ui.Label(this, Lang.T("Tageszeit"), 12.5, color: Ui.TextSecondary));
        _time = new SeekBar(this) { Max = 72, Progress = 32 };
        row2.AddView(_time, Ui.Weight(1, vertical: false));
        _timeText = Ui.Label(this, "", 12.5);
        _timeText.SetMinWidth(Ui.Dp(68));
        row2.AddView(_timeText);
        row2.AddView(Ui.Label(this, Lang.T("Tempo"), 12.5, color: Ui.TextSecondary));
        _speed = new SeekBar(this) { Max = 150, Progress = 50 };
        row2.AddView(_speed, Ui.Weight(1, vertical: false));
        _speedText = Ui.Label(this, "", 12.5);
        _speedText.SetMinWidth(Ui.Dp(62));
        row2.AddView(_speedText);
        bar.AddView(row2, Ui.Fill());
        root.AddView(bar, new FrameLayout.LayoutParams(-1, -2, GravityFlags.Top));

        var help = Ui.Label(this, Lang.T("Ein Finger dreht · zwei Finger zoomen und verschieben · Mitfahren startet am gewählten Gleis"), 12, color: Ui.TextSecondary);
        help.Background = Ui.Rounded(Color.Argb(0xE0, 0xFF, 0xFF, 0xFF), radius: 6);
        help.SetPadding(Ui.Dp(10), Ui.Dp(6), Ui.Dp(10), Ui.Dp(6));
        root.AddView(help, new FrameLayout.LayoutParams(-2, -2, GravityFlags.Bottom | GravityFlags.Start) { LeftMargin = Ui.Dp(10), BottomMargin = Ui.Dp(10), RightMargin = Ui.Dp(10) });

        root.SetOnApplyWindowInsetsListener(new Insets());
        SetContentView(root);

        _time.ProgressChanged += (_, _) => ApplyTime();
        _speed.ProgressChanged += (_, _) => { _speedKmh = (float)Speed; _speedText.Text = $"{Speed:0} km/h"; };
        _speedText.Text = $"{Speed:0} km/h";
        if (mode == View3DMode.Evening) _time.Progress = (int)((19.5 - 5) * 4);
        ApplyTime();
        UpdateCamera();

        _scale = new ScaleGestureDetector(this, new Zoom(this));
        _view.Touch += OnTouch;
        if (mode == View3DMode.Ride) _view.Post(StartRide);
    }

    private double Hour => 5 + _time.Progress * 0.25;
    private double Speed => 10 + _speed.Progress;

    private sealed class Insets : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View v, WindowInsets insets)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.VanillaIceCream)
            {
                var bars = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());
                v.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);
            }
            return insets;
        }
    }

    protected override void OnPause()
    {
        base.OnPause();
        _view.OnPause();
    }

    protected override void OnResume()
    {
        base.OnResume();
        _view.OnResume();
    }

    private void ApplyTime()
    {
        _timeText.Text = DayLight.TimeText(Hour);
        _renderer.Light = DayLight.At(Hour);
        _view.RequestRender();
    }

    // ---------- Kamera ----------

    private void UpdateCamera()
    {
        var yaw = _yaw * Math.PI / 180;
        var pitch = Math.Clamp(_pitch, 5, 89) * Math.PI / 180;
        var eye = new Vec3(
            _target.X + _distance * Math.Cos(pitch) * Math.Sin(yaw),
            _target.Y + _distance * Math.Sin(pitch),
            _target.Z + _distance * Math.Cos(pitch) * Math.Cos(yaw));
        _renderer.SetCamera(eye, _target, 50, Math.Max(2, _distance / 2000));
        _view.RequestRender();
    }

    private void OnTouch(object? sender, View.TouchEventArgs e)
    {
        var ev = e.Event!;
        e.Handled = true;
        if (_riding) return;
        _scale.OnTouchEvent(ev);
        switch (ev.ActionMasked)
        {
            case MotionEventActions.Down:
                _multi = false;
                _lastX = ev.GetX();
                _lastY = ev.GetY();
                break;
            case MotionEventActions.PointerDown:
                _multi = true;
                break;
            case MotionEventActions.Move when !_multi && ev.PointerCount == 1:
                var dx = ev.GetX() - _lastX;
                var dy = ev.GetY() - _lastY;
                _lastX = ev.GetX();
                _lastY = ev.GetY();
                _yaw -= dx / Ui.Density * 0.35;
                _pitch = Math.Clamp(_pitch + dy / Ui.Density * 0.3, 5, 89);
                UpdateCamera();
                break;
        }
    }

    private sealed class Zoom(View3DActivity a) : ScaleGestureDetector.SimpleOnScaleGestureListener
    {
        private float _fx, _fy;

        public override bool OnScaleBegin(ScaleGestureDetector d)
        {
            _fx = d.FocusX;
            _fy = d.FocusY;
            return true;
        }

        public override bool OnScale(ScaleGestureDetector d)
        {
            a._distance = Math.Clamp(a._distance / d.ScaleFactor, 50, 100000);
            // Mit zwei Fingern verschieben
            var yaw = a._yaw * Math.PI / 180;
            var scale = a._distance / 900 / Ui.Density;
            double mx = d.FocusX - _fx, my = d.FocusY - _fy;
            var right = new Vec3(Math.Cos(yaw), 0, -Math.Sin(yaw));
            var forward = new Vec3(-Math.Sin(yaw), 0, -Math.Cos(yaw));
            a._target = a._target - right * (mx * scale) + forward * (my * scale);
            _fx = d.FocusX;
            _fy = d.FocusY;
            a.UpdateCamera();
            return true;
        }
    }

    // ---------- Mitfahren ----------

    private void SetRiding(bool on)
    {
        _riding = on;
        Ui.SetButtonState(_ride, false, on);
    }

    private bool EnsurePath()
    {
        _path ??= RidePath.Build(_plan, _startPiece, _routes);
        if (!_path.IsEmpty) return true;
        _status.Text = Lang.T("Keine Gleise zum Mitfahren.");
        return false;
    }

    private void StartMoving()
    {
        _lastFrame = 0;
        _renderer.Frame = OnFrame;
        _view.RenderMode = Rendermode.Continuously;
        _switch.Enabled = true;
        UpdateStatus();
    }

    private void StopMovingIfIdle()
    {
        if (_riding || _trainOn) return;
        _renderer.Frame = null;
        _view.RenderMode = Rendermode.WhenDirty;
        _switch.Enabled = false;
        _status.Text = "";
    }

    private void StartRide()
    {
        if (!EnsurePath()) { SetRiding(false); return; }
        SetRiding(true);
        _renderer.Dynamic = null;
        StartMoving();
    }

    private void StopRide()
    {
        SetRiding(false);
        UpdateCamera();
        StopMovingIfIdle();
        _view.RequestRender();
    }

    private void StartTrain()
    {
        if (!EnsurePath()) return;
        if (_position < _train.Length) _position = Math.Min(_train.Length, _path!.Length);
        _trainOn = true;
        Ui.SetButtonState(_trainButton, false, true);
        StartMoving();
    }

    private void StopTrain()
    {
        _trainOn = false;
        Ui.SetButtonState(_trainButton, false, false);
        _renderer.Dynamic = null;
        StopMovingIfIdle();
        _view.RequestRender();
    }

    /// <summary>Stellt die nächste spitz befahrene Weiche vor dem Zug um; der Weg dahinter wird neu bestimmt.</summary>
    private void ToggleNextSwitch()
    {
        if (_path?.NextSwitch(_position) is not { } next || _plan.Find(next.PieceId) is not { } piece) return;
        lock (_routes)
        {
            _routes[piece.Id] = ((_routes.TryGetValue(piece.Id, out var r) ? r : 0) + 1) % RidePath.RouteCount(piece);
            _path = RidePath.Build(_plan, _startPiece, _routes);
        }
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_path is not { } path) return;
        var text = path.Closed ? Lang.T("Rundkurs") : Lang.T("Wendet an Prellböcken");
        if (path.NextSwitch(_position) is { } next && _plan.Find(next.PieceId) is { } piece)
        {
            int route;
            lock (_routes) route = _routes.TryGetValue(piece.Id, out var r) ? r : 0;
            var setting = RidePath.RouteCount(piece) > 2 ? Lang.F($"Stellung {route + 1}") : route == 0 ? Lang.T("gerade") : Lang.T("abzweigend");
            text += Lang.F($" · Weiche Nr. {piece.Id} in {Fmt.Num((next.Distance - _position) / 10, 0)} cm: {setting}");
        }
        _status.Text = text;
    }

    /// <summary>Läuft im Zeichen-Thread vor jedem Bild.</summary>
    private void OnFrame()
    {
        if (_path is not { IsEmpty: false } path) return;
        var now = SystemClock.ElapsedRealtime();
        var dt = _lastFrame == 0 ? 0 : Math.Min((now - _lastFrame) / 1000.0, 0.1);
        _lastFrame = now;
        _position += RidePath.ModelSpeed(_speedKmh, _plan.Gauge.Scale) * dt;
        if (_position >= path.Length) _position = path.Closed ? _position % path.Length : Math.Min(_train.Length, path.Length);
        if (now - _lastStatus > 400) { _lastStatus = now; RunOnUiThread(UpdateStatus); }
        if (_riding)
        {
            var g = _plan.Gauge.TrackWidth;
            var (eye, target) = path.Camera(_position, g);
            _renderer.SetCamera(eye, target, 65, g * 0.3);
        }
        else if (_trainOn) _renderer.Dynamic = SceneRenderer.ToData(_train.Build(path, _position));
    }
}

/// <summary>Zeichnet das 3D-Modell mit OpenGL ES 2: beidseitig beleuchtete Dreiecke je Farbe, Himmel als Verlauf.</summary>
public sealed class SceneRenderer : Java.Lang.Object, GLSurfaceView.IRenderer
{
    private const string VertexShader = """
        uniform mat4 uMvp;
        attribute vec3 aPos;
        attribute vec3 aNormal;
        varying vec3 vNormal;
        varying vec3 vPos;
        void main() {
            vNormal = aNormal;
            vPos = aPos;
            gl_Position = uMvp * vec4(aPos, 1.0);
        }
        """;

    private const string FragmentShader = """
        #ifdef GL_FRAGMENT_PRECISION_HIGH
        precision highp float;
        #else
        precision mediump float;
        #endif
        uniform vec3 uColor;
        uniform vec3 uSunDir;
        uniform vec3 uSunColor;
        uniform vec3 uFillDir;
        uniform vec3 uFillColor;
        uniform vec3 uAmbient;
        uniform vec3 uEmissive;
        uniform vec3 uEye;
        uniform float uSpec;
        varying vec3 vNormal;
        varying vec3 vPos;
        void main() {
            vec3 n = normalize(vNormal);
            if (!gl_FrontFacing) n = -n;
            float ds = max(dot(n, -uSunDir), 0.0);
            float df = max(dot(n, -uFillDir), 0.0);
            vec3 c = uColor * (uAmbient + uSunColor * ds + uFillColor * df) + uEmissive;
            if (uSpec > 0.0) {
                vec3 v = normalize(uEye - vPos);
                vec3 h = normalize(v - uSunDir);
                c += uSunColor * pow(max(dot(n, h), 0.0), uSpec) * 0.5;
            }
            gl_FragColor = vec4(min(c, vec3(1.0)), 1.0);
        }
        """;

    private const string SkyVertex = """
        attribute vec2 aPos;
        varying float vT;
        void main() {
            vT = aPos.y * 0.5 + 0.5;
            gl_Position = vec4(aPos, 0.999, 1.0);
        }
        """;

    private const string SkyFragment = """
        precision mediump float;
        uniform vec3 uTop;
        uniform vec3 uBottom;
        varying float vT;
        void main() { gl_FragColor = vec4(mix(uBottom, uTop, vT), 1.0); }
        """;

    private sealed record MeshBuffer(int Buffer, int Count, Rgb Color, SurfaceKind Kind);

    private readonly List<(float[] Data, Rgb Color, SurfaceKind Kind)> _pending = [];
    private readonly List<MeshBuffer> _meshes = [];
    private int _program, _sky, _skyBuffer;
    private int _uMvp, _uColor, _uSunDir, _uSunColor, _uFillDir, _uFillColor, _uAmbient, _uEmissive, _uEye, _uSpec, _aPos, _aNormal;
    private int _skyPos, _uTop, _uBottom;
    private float _aspect = 1;
    private readonly float[] _proj = new float[16], _viewM = new float[16], _mvp = new float[16];
    private volatile CameraState _camera = new(new Vec3(0, 1000, 1000), new Vec3(0, 0, 0), 50, 2);

    private sealed record CameraState(Vec3 Eye, Vec3 Target, double FieldOfView, double Near);

    public DayLight Light { get; set; } = DayLight.At(13);

    /// <summary>Wird vor jedem Bild im Zeichen-Thread aufgerufen (Mitfahren).</summary>
    public Action? Frame { get; set; }

    /// <summary>Bewegte Teile (der fahrende Zug); werden in jedem Bild neu hochgeladen.</summary>
    public volatile IReadOnlyList<(float[] Data, Rgb Color, SurfaceKind Kind)>? Dynamic;
    private int _dynamicBuffer;

    public SceneRenderer(Scene3DModel model) => _pending.AddRange(ToData(model.Meshes));

    /// <summary>Dreiecke als Folge von Position und Flächennormale (je 6 Zahlen).</summary>
    public static List<(float[] Data, Rgb Color, SurfaceKind Kind)> ToData(IReadOnlyDictionary<(Rgb Color, SurfaceKind Kind), SceneMesh> meshes)
    {
        var result = new List<(float[], Rgb, SurfaceKind)>();
        foreach (var ((color, kind), mesh) in meshes)
        {
            if (mesh.IsEmpty) continue;
            var pts = mesh.Points;
            var data = new float[pts.Count * 6];
            for (var i = 0; i + 2 < pts.Count; i += 3)
            {
                var n = Vec3.Cross(pts[i + 1] - pts[i], pts[i + 2] - pts[i]).Normalized();
                for (var k = 0; k < 3; k++)
                {
                    var p = pts[i + k];
                    var o = (i + k) * 6;
                    data[o] = (float)p.X; data[o + 1] = (float)p.Y; data[o + 2] = (float)p.Z;
                    data[o + 3] = (float)n.X; data[o + 4] = (float)n.Y; data[o + 5] = (float)n.Z;
                }
            }
            result.Add((data, color, kind));
        }
        return result;
    }

    /// <summary>Kamera setzen. <paramref name="fieldOfView"/> ist wie unter Windows der waagrechte Blickwinkel.</summary>
    public void SetCamera(Vec3 eye, Vec3 target, double fieldOfView, double near) => _camera = new CameraState(eye, target, fieldOfView, near);

    private static int Compile(int type, string source)
    {
        var s = GLES20.GlCreateShader(type);
        GLES20.GlShaderSource(s, source);
        GLES20.GlCompileShader(s);
        var ok = new int[1];
        GLES20.GlGetShaderiv(s, GLES20.GlCompileStatus, ok, 0);
        if (ok[0] == 0) throw new InvalidOperationException(Lang.T("Shader: ") + GLES20.GlGetShaderInfoLog(s));
        return s;
    }

    private static int Link(string vertex, string fragment)
    {
        var p = GLES20.GlCreateProgram();
        GLES20.GlAttachShader(p, Compile(GLES20.GlVertexShader, vertex));
        GLES20.GlAttachShader(p, Compile(GLES20.GlFragmentShader, fragment));
        GLES20.GlLinkProgram(p);
        return p;
    }

    private static int Upload(float[] data)
    {
        var ids = new int[1];
        GLES20.GlGenBuffers(1, ids, 0);
        var buffer = ByteBuffer.AllocateDirect(data.Length * 4).Order(ByteOrder.NativeOrder()!)!.AsFloatBuffer()!;
        buffer.Put(data);
        buffer.Position(0);
        GLES20.GlBindBuffer(GLES20.GlArrayBuffer, ids[0]);
        GLES20.GlBufferData(GLES20.GlArrayBuffer, data.Length * 4, buffer, GLES20.GlStaticDraw);
        GLES20.GlBindBuffer(GLES20.GlArrayBuffer, 0);
        return ids[0];
    }

    public void OnSurfaceCreated(IGL10? gl, EGLConfig? config)
    {
        _program = Link(VertexShader, FragmentShader);
        _uMvp = GLES20.GlGetUniformLocation(_program, "uMvp");
        _uColor = GLES20.GlGetUniformLocation(_program, "uColor");
        _uSunDir = GLES20.GlGetUniformLocation(_program, "uSunDir");
        _uSunColor = GLES20.GlGetUniformLocation(_program, "uSunColor");
        _uFillDir = GLES20.GlGetUniformLocation(_program, "uFillDir");
        _uFillColor = GLES20.GlGetUniformLocation(_program, "uFillColor");
        _uAmbient = GLES20.GlGetUniformLocation(_program, "uAmbient");
        _uEmissive = GLES20.GlGetUniformLocation(_program, "uEmissive");
        _uEye = GLES20.GlGetUniformLocation(_program, "uEye");
        _uSpec = GLES20.GlGetUniformLocation(_program, "uSpec");
        _aPos = GLES20.GlGetAttribLocation(_program, "aPos");
        _aNormal = GLES20.GlGetAttribLocation(_program, "aNormal");
        _sky = Link(SkyVertex, SkyFragment);
        _skyPos = GLES20.GlGetAttribLocation(_sky, "aPos");
        _uTop = GLES20.GlGetUniformLocation(_sky, "uTop");
        _uBottom = GLES20.GlGetUniformLocation(_sky, "uBottom");
        _skyBuffer = Upload([-1, -1, 1, -1, 1, 1, -1, -1, 1, 1, -1, 1]);
        var dyn = new int[1];
        GLES20.GlGenBuffers(1, dyn, 0);
        _dynamicBuffer = dyn[0];
        // Nach Verlust des Kontexts (App im Hintergrund) alles neu hochladen
        _meshes.Clear();
        foreach (var (data, color, kind) in _pending)
            _meshes.Add(new MeshBuffer(Upload(data), data.Length / 6, color, kind));
    }

    public void OnSurfaceChanged(IGL10? gl, int width, int height)
    {
        GLES20.GlViewport(0, 0, width, height);
        _aspect = height == 0 ? 1 : (float)width / height;
    }

    private static void Uniform(int location, Rgb c) => GLES20.GlUniform3f(location, c.R / 255f, c.G / 255f, c.B / 255f);

    private static void Uniform(int location, Vec3 v)
    {
        var n = v.Normalized();
        GLES20.GlUniform3f(location, (float)n.X, (float)n.Y, (float)n.Z);
    }

    public void OnDrawFrame(IGL10? gl)
    {
        Frame?.Invoke();
        var light = Light;
        var cam = _camera;

        GLES20.GlClearColor(light.SkyBottom.R / 255f, light.SkyBottom.G / 255f, light.SkyBottom.B / 255f, 1);
        GLES20.GlClear(GLES20.GlColorBufferBit | GLES20.GlDepthBufferBit);

        // Himmel
        GLES20.GlDisable(GLES20.GlDepthTest);
        GLES20.GlUseProgram(_sky);
        Uniform(_uTop, light.SkyTop);
        Uniform(_uBottom, light.SkyBottom);
        GLES20.GlBindBuffer(GLES20.GlArrayBuffer, _skyBuffer);
        GLES20.GlEnableVertexAttribArray(_skyPos);
        GLES20.GlVertexAttribPointer(_skyPos, 2, GLES20.GlFloat, false, 8, 0);
        GLES20.GlDrawArrays(GLES20.GlTriangles, 0, 6);
        GLES20.GlDisableVertexAttribArray(_skyPos);

        // Kamera: waagrechter Blickwinkel → senkrechter
        var fovY = 2 * Math.Atan(Math.Tan(cam.FieldOfView * Math.PI / 360) / _aspect) * 180 / Math.PI;
        GLMatrix.PerspectiveM(_proj, 0, (float)fovY, _aspect, (float)cam.Near, 200000f);
        GLMatrix.SetLookAtM(_viewM, 0, (float)cam.Eye.X, (float)cam.Eye.Y, (float)cam.Eye.Z, (float)cam.Target.X, (float)cam.Target.Y, (float)cam.Target.Z, 0, 1, 0);
        GLMatrix.MultiplyMM(_mvp, 0, _proj, 0, _viewM, 0);

        GLES20.GlEnable(GLES20.GlDepthTest);
        GLES20.GlUseProgram(_program);
        GLES20.GlUniformMatrix4fv(_uMvp, 1, false, _mvp, 0);
        Uniform(_uSunDir, light.SunDirection);
        Uniform(_uSunColor, light.Sun);
        Uniform(_uFillDir, light.FillDirection);
        Uniform(_uFillColor, light.Fill);
        Uniform(_uAmbient, light.Ambient);
        GLES20.GlUniform3f(_uEye, (float)cam.Eye.X, (float)cam.Eye.Y, (float)cam.Eye.Z);
        GLES20.GlEnableVertexAttribArray(_aPos);
        GLES20.GlEnableVertexAttribArray(_aNormal);
        var dynamic = Dynamic;
        var parts = dynamic is null ? _meshes : _meshes.Concat(dynamic.Select(d => new MeshBuffer(-1, d.Data.Length / 6, d.Color, d.Kind)));
        var dynamicIndex = 0;
        foreach (var m in parts)
        {
            var black = new Rgb(0, 0, 0);
            Uniform(_uColor, m.Kind == SurfaceKind.Lamp ? black : m.Color);
            Uniform(_uEmissive, m.Kind switch
            {
                SurfaceKind.Lamp => m.Color,
                SurfaceKind.Window when light.WindowsLit => DayLight.WindowGlow,
                _ => black,
            });
            GLES20.GlUniform1f(_uSpec, m.Kind switch { SurfaceKind.Water => 40f, SurfaceKind.Rail => 20f, _ => 0f });
            if (m.Buffer >= 0) GLES20.GlBindBuffer(GLES20.GlArrayBuffer, m.Buffer);
            else
            {
                var data = dynamic![dynamicIndex++].Data;
                var buffer = ByteBuffer.AllocateDirect(data.Length * 4).Order(ByteOrder.NativeOrder()!)!.AsFloatBuffer()!;
                buffer.Put(data);
                buffer.Position(0);
                GLES20.GlBindBuffer(GLES20.GlArrayBuffer, _dynamicBuffer);
                GLES20.GlBufferData(GLES20.GlArrayBuffer, data.Length * 4, buffer, GLES20.GlDynamicDraw);
            }
            GLES20.GlVertexAttribPointer(_aPos, 3, GLES20.GlFloat, false, 24, 0);
            GLES20.GlVertexAttribPointer(_aNormal, 3, GLES20.GlFloat, false, 24, 12);
            GLES20.GlDrawArrays(GLES20.GlTriangles, 0, m.Count);
        }
        GLES20.GlDisableVertexAttribArray(_aPos);
        GLES20.GlDisableVertexAttribArray(_aNormal);
        GLES20.GlBindBuffer(GLES20.GlArrayBuffer, 0);
    }
}
