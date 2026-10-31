using Microsoft.AspNetCore.Components;
using TrackMaster.Core;
using TrackMaster.Web.Interop;
using TrackMaster.Web.Plan;

namespace TrackMaster.Web;

/// <summary>
/// 3D-Ansicht im Browser (WebGL): Rundumsicht, Mitfahren im Führerstand, fahrender Zug und Licht nach Tageszeit.
/// Modell, Licht und Fahrweg kommen aus dem gemeinsamen Kern; gezeichnet wird in trackmaster.js.
/// </summary>
public partial class App
{
    public const string View3DElementId = "view3d-canvas";

    private bool _open3D, _glReady;
    private View3DMode _mode3D;
    private LayoutPlan? _plan3D;
    private bool _riding, _trainOn;
    private bool _scenery3D = true;
    private Train3D? _train;
    private readonly Dictionary<int, int> _routes = [];
    private RidePath? _path;
    private double _position, _sinceStatus;
    private int? _startPiece;
    private int _hourStep = 32;   // 5:00 + 0,25 h je Schritt
    private int _speedStep = 50;  // 10 km/h + Schritt
    private string _status3D = "";

    private double Hour => 5 + _hourStep * 0.25;
    private double Speed => 10 + _speedStep;

    private void Open3D(View3DMode mode)
    {
        _plan3D = _session.View;
        _mode3D = mode;
        _startPiece = _session.SelectedPieces.FirstOrDefault()?.Id;
        _routes.Clear();
        _path = null;
        _position = 0;
        _riding = _trainOn = false;
        _status3D = "";
        if (mode == View3DMode.Evening) _hourStep = (int)((19.5 - 5) * 4);
        _open3D = true;
        _glReady = false;
        StateHasChanged();
    }

    /// <summary>Nach dem ersten Zeichnen der 3D-Fläche: WebGL öffnen und das Modell hochladen.</summary>
    private void Init3D()
    {
        _glReady = true;
        var plan = _plan3D!;
        if (!Js.GlOpen(View3DElementId))
        {
            _open3D = false;
            Message(Lang.T("3D-Ansicht"), Lang.T("Dieser Browser kann keine 3D-Grafik (WebGL) anzeigen. Bitte die Hardwarebeschleunigung einschalten oder einen aktuellen Browser verwenden."));
            StateHasChanged();
            return;
        }
        try
        {
            var model = new Scene3DModel(plan, _scenery3D);
            var (data, meta) = ToData(model.Meshes);
            Js.GlSetMeshes(data, meta, false);
            _train = new Train3D(plan);
            var (x0, y0, x1, y1) = plan.Bounds();
            Js.GlSetCamera([0, (x0 + x1) / 2, 0, (y0 + y1) / 2, Math.Max(x1 - x0, y1 - y0) * 1.15]);
            ApplyTime();
            if (_mode3D == View3DMode.Ride) StartRide();
        }
        catch (Exception e)
        {
            Report(e);
        }
    }

    /// <summary>3D mit Gelände oder nur Platte und Gleise.</summary>
    private void SetScenery3D(bool on)
    {
        if (_scenery3D == on || _plan3D is null) return;
        _scenery3D = on;
        try
        {
            var (data, meta) = ToData(new Scene3DModel(_plan3D, on).Meshes);
            Js.GlSetMeshes(data, meta, false);
        }
        catch (Exception e)
        {
            Report(e);
        }
    }

    private void Close3D()
    {
        Js.GlAnimate(false);
        Js.GlClose();
        _open3D = _glReady = false;
        _riding = _trainOn = false;
        _plan3D = null;
        _path = null;
        _train = null;
        StateHasChanged();
        Js.Focus(PlanView.ElementId);
    }

    /// <summary>Dreiecke als Folge von Position und Flächennormale (je 6 Zahlen), dazu je Teil Anzahl, Farbe und Art.</summary>
    private static (List<double> Data, List<double> Meta) ToData(IReadOnlyDictionary<(Rgb Color, SurfaceKind Kind), SceneMesh> meshes)
    {
        var data = new List<double>();
        var meta = new List<double>();
        foreach (var ((color, kind), mesh) in meshes)
        {
            if (mesh.IsEmpty) continue;
            var pts = mesh.Points;
            var count = pts.Count / 3 * 3;
            data.EnsureCapacity(data.Count + count * 6);
            for (var i = 0; i + 2 < pts.Count; i += 3)
            {
                var n = Vec3.Cross(pts[i + 1] - pts[i], pts[i + 2] - pts[i]).Normalized();
                for (var k = 0; k < 3; k++)
                {
                    var p = pts[i + k];
                    data.Add(p.X); data.Add(p.Y); data.Add(p.Z);
                    data.Add(n.X); data.Add(n.Y); data.Add(n.Z);
                }
            }
            meta.Add(count);
            meta.Add(color.R / 255.0); meta.Add(color.G / 255.0); meta.Add(color.B / 255.0);
            meta.Add((int)kind);
        }
        return (data, meta);
    }

    private void ApplyTime()
    {
        var l = DayLight.At(Hour);
        static IEnumerable<double> C(Rgb c) => [c.R / 255.0, c.G / 255.0, c.B / 255.0];
        static IEnumerable<double> V(Vec3 v) => [v.X, v.Y, v.Z];
        var values = C(l.Sun).Concat(V(l.SunDirection)).Concat(C(l.Fill)).Concat(V(l.FillDirection)).Concat(C(l.Ambient))
            .Concat(C(l.SkyTop)).Concat(C(l.SkyBottom)).Append(l.WindowsLit ? 1 : 0).Concat(C(DayLight.WindowGlow)).ToArray();
        Js.GlSetLight(values);
    }

    private void SetHour(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var v)) _hourStep = Math.Clamp(v, 0, 72);
        if (_glReady) ApplyTime();
    }

    private void SetSpeed(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var v)) _speedStep = Math.Clamp(v, 0, 150);
    }

    // ---------- Mitfahren und Zug ----------

    private bool EnsurePath()
    {
        _path ??= RidePath.Build(_plan3D!, _startPiece, _routes);
        if (!_path.IsEmpty) return true;
        _status3D = Lang.T("Keine Gleise zum Mitfahren.");
        return false;
    }

    private void StartMoving()
    {
        Js.GlAnimate(true);
        UpdateStatus3D();
    }

    private void StopMovingIfIdle()
    {
        if (_riding || _trainOn) return;
        Js.GlAnimate(false);
        _status3D = "";
    }

    private void Orbit3D()
    {
        if (_riding) StopRide();
    }

    private void ToggleRide()
    {
        if (_riding) StopRide();
        else StartRide();
    }

    private void StartRide()
    {
        if (!EnsurePath()) { _riding = false; return; }
        _riding = true;
        Js.GlSetMeshes([], [], true);
        StartMoving();
    }

    private void StopRide()
    {
        _riding = false;
        Js.GlSetCamera([2]);
        StopMovingIfIdle();
        Js.GlRender();
    }

    private void ToggleTrain()
    {
        if (_trainOn)
        {
            _trainOn = false;
            Js.GlSetMeshes([], [], true);
            StopMovingIfIdle();
            Js.GlRender();
            return;
        }
        if (!EnsurePath()) return;
        if (_position < _train!.Length) _position = Math.Min(_train.Length, _path!.Length);
        _trainOn = true;
        StartMoving();
    }

    /// <summary>Stellt die nächste spitz befahrene Weiche vor dem Zug um; der Weg dahinter wird neu bestimmt.</summary>
    private void ToggleNextSwitch()
    {
        if (_path?.NextSwitch(_position) is not { } next || _plan3D!.Find(next.PieceId) is not { } piece) return;
        _routes[piece.Id] = ((_routes.TryGetValue(piece.Id, out var r) ? r : 0) + 1) % RidePath.RouteCount(piece);
        _path = RidePath.Build(_plan3D, _startPiece, _routes);
        UpdateStatus3D();
    }

    private void UpdateStatus3D()
    {
        if (_path is not { } path) return;
        var text = path.Closed ? Lang.T("Rundkurs") : Lang.T("Wendet an Prellböcken");
        if (path.NextSwitch(_position) is { } next && _plan3D!.Find(next.PieceId) is { } piece)
        {
            var route = _routes.TryGetValue(piece.Id, out var r) ? r : 0;
            var setting = RidePath.RouteCount(piece) > 2 ? Lang.F($"Stellung {route + 1}") : route == 0 ? Lang.T("gerade") : Lang.T("abzweigend");
            text += Lang.F($" · Weiche Nr. {piece.Id} in {Fmt.Num((next.Distance - _position) / 10, 0)} cm: {setting}");
        }
        _status3D = text;
    }

    /// <summary>Vor jedem Bild, solange etwas fährt.</summary>
    public void Tick3D(double dt)
    {
        if (!_open3D || _path is not { IsEmpty: false } path || _plan3D is null) return;
        _position += RidePath.ModelSpeed(Speed, _plan3D.Gauge.Scale) * dt;
        if (_position >= path.Length) _position = path.Closed ? _position % path.Length : Math.Min(_train?.Length ?? 0, path.Length);
        if (_riding)
        {
            var g = _plan3D.Gauge.TrackWidth;
            var (eye, target) = path.Camera(_position, g);
            Js.GlSetCamera([1, eye.X, eye.Y, eye.Z, target.X, target.Y, target.Z, 65, g * 0.3]);
        }
        else if (_trainOn && _train is not null)
        {
            var (data, meta) = ToData(_train.Build(path, _position));
            Js.GlSetMeshes(data, meta, true);
        }
        _sinceStatus += dt;
        if (_sinceStatus > 0.4)
        {
            _sinceStatus = 0;
            var before = _status3D;
            UpdateStatus3D();
            if (before != _status3D) StateHasChanged();
        }
    }
}
