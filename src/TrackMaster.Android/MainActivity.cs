using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using TrackMaster.Core;
using TrackMaster.Licensing;
using Signal = TrackMaster.Core.Signal;

namespace TrackMaster.Mobile;

/// <summary>
/// Hauptfenster der Android-Version: Kopfzeile mit Rückgängig, Register wie das Ribbon unter Windows, Zeichenfläche,
/// Gleisbibliothek und Statuszeile. Der Plan wird beim Verlassen automatisch gesichert und beim Start wiederhergestellt.
/// </summary>
[Activity(Name = "de.moba4n.trackmaster.MainActivity", Label = "TrackMaster Pro", MainLauncher = true, Theme = "@android:style/Theme.Material.Light.NoActionBar",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.KeyboardHidden | ConfigChanges.UiMode,
    WindowSoftInputMode = SoftInput.AdjustResize)]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault], DataMimeType = "application/json")]
public sealed class MainActivity : Activity
{
    private const int RequestOpen = 1, RequestSave = 2, RequestLicense = 3, RequestExport = 4;

    private readonly PlanSession _session = new();
    private LicenseManager _license = null!;
    private PlanView _canvas = null!;
    private LibraryPanel _library = null!;
    private LinearLayout _tabs = null!;
    private LinearLayout _actions = null!;
    private HorizontalScrollView _actionScroll = null!;
    private TextView _title = null!, _edition = null!, _status = null!, _toast = null!;
    private LinearLayout _toolBar = null!;
    private TextView _toolText = null!;
    private Button _toolFinish = null!, _toolBack = null!;
    private Button _undo = null!, _redo = null!;
    private string _tab = "Start";
    private readonly Dictionary<string, Command> _commands = [];
    private readonly List<(Button Button, Func<bool> Active)> _toggles = [];
    private (string Content, string Mime)? _pendingExport;
    private readonly Handler _handler = new(Looper.MainLooper!);
    private Action? _hideToast;

    private sealed record Command(string Name, Action Execute, bool Pro, Func<bool>? CanExecute);

    private string AutosavePath => System.IO.Path.Combine(FilesDir!.AbsolutePath, "autosave.trackmaster.json");

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Ui.Density = Resources!.DisplayMetrics!.Density;
#if DEBUG
        const bool developer = true;
#else
        const bool developer = false;
#endif
        _license = new LicenseManager(System.IO.Path.Combine(FilesDir!.AbsolutePath, AppFolders.LicenseFileName), LicensePublicKey.Pem, developer, machineId: AndroidDeviceId());
        _session.IsPro = () => _license.IsPro;
        _session.ProRequired += feature => Dialogs.ShowProRequired(this, feature, ShowLicense);
        _session.Message += ShowMessage;
        RestoreAutosave();
        RegisterCommands();
        BuildUi();
        _session.Changed += Refresh;
        if (Intent?.Data is { } data && Intent.Action == Intent.ActionView) OpenUri(data);
        Refresh();
        RegisterBack();
#if DEBUG
        // Für den Build-Test: adb shell am start … --es demo 3d|ride|tab:Gelände|check
        if (Intent?.GetStringExtra("demo") is { } demo) _canvas.PostDelayed(() => Demo(demo), 1500);
#endif
    }

#if DEBUG
    private void Demo(string demo)
    {
        switch (demo)
        {
            case "3d": Run("view3d"); break;
            case "ride": Run("ride"); break;
            case "evening": Run("daytime"); break;
            case "check": Run("check"); break;
            case "props":
                if (_session.Plan.Pieces.FirstOrDefault(p => p.Def.Kind == PieceKind.Switch) is { } sw) _session.Select([sw.Id], add: false);
                Run("props");
                break;
            default:
                if (demo.StartsWith("tab:")) { _tab = demo[4..]; BuildActions(); }
                break;
        }
    }
#endif

    /// <summary>Gerätekennung für gerätegebundene Lizenzen: Android-ID als Hash im Format der Windows-Rechnerkennung.</summary>
    private string AndroidDeviceId()
    {
        var raw = Settings.Secure.GetString(ContentResolver, Settings.Secure.AndroidId) ?? Build.Model ?? "android";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("android|" + raw));
        return Convert.ToHexString(hash, 0, 10);
    }

    private void RestoreAutosave()
    {
        try
        {
            if (File.Exists(AutosavePath))
            {
                var plan = PlanSerializer.FromJson(File.ReadAllText(AutosavePath), out _);
                _session.Replace(plan, null);
            }
        }
        catch (Exception)
        {
            // Defekte Sicherung: mit der Beispielanlage weitermachen
        }
    }

    protected override void OnPause()
    {
        base.OnPause();
        try { File.WriteAllText(AutosavePath, PlanSerializer.ToJson(_session.Plan)); }
        catch (IOException) { }
    }

    // ---------- Befehle ----------

    private void Add(string id, string name, Action execute, Func<bool>? canExecute = null) => _commands[id] = new Command(name, execute, false, canExecute);
    private void Pro(string id, string name, Action execute, Func<bool>? canExecute = null) => _commands[id] = new Command(name, execute, true, canExecute);

    private void Run(string id)
    {
        if (!_commands.TryGetValue(id, out var cmd)) return;
        if (cmd.CanExecute is { } can && !can()) return;
        if (cmd.Pro && !_session.RequirePro(cmd.Name)) return;
        cmd.Execute();
    }

    private bool HasSelection() => _session.Selection.Count > 0;

    private void RegisterCommands()
    {
        Add("new", "Neu", () => Dialogs.AskNewPlan(this, _session.Plan.GaugeId, gauge =>
        {
            _session.NewPlan();
            _session.SetGauge(gauge);
            _library.Refresh(force: true);
            _canvas.Fit();
        }));
        Add("open", "Öffnen", () => Pick(RequestOpen));
        Add("save", "Speichern", SavePlan);
        Add("sample", "Beispielanlage", () => { _session.LoadSample(); _library.Refresh(force: true); _canvas.Fit(); });
        Add("license", "Lizenz", ShowLicense);
        Add("help", "Bedienung", () => Dialogs.ShowHelp(this));
        Add("undo", "Rückgängig", _session.Undo, () => _session.History.CanUndo);
        Add("redo", "Wiederholen", _session.Redo, () => _session.History.CanRedo);
        Add("rotL", "Drehen", () => _session.Rotate(-15), HasSelection);
        Add("rotR", "Drehen", () => _session.Rotate(15), HasSelection);
        Add("flip", "Spiegeln", _session.Flip, HasSelection);
        Add("swap", "Anschluss wechseln", _session.SwapAnchor, () => _session.Selection.Count == 1);
        Add("duplicate", "Duplizieren", _session.Duplicate, HasSelection);
        Add("delete", "Löschen", _session.Delete, () => HasSelection() || _session.SelectedObject is not null);
        Add("selectAll", "Alles auswählen", _session.SelectAll);
        Add("selectNone", "Auswahl aufheben", _session.ClearSelection);
        Add("nextEnd", "Nächstes Gleisende", () => { if (_session.NextOpenEnd() is { } p) _canvas.Reveal(p.X, p.Y); });
        Add("props", "Eigenschaften", () => Dialogs.ShowProperties(this, _session, Run));
        Add("bom", "Stückliste", () => Dialogs.ShowBillOfMaterials(this, _session.Plan, _license.IsPro, () => Run("csv")));
        Add("check", "Planprüfung", ShowCheck);
        Add("fit", "Einpassen", _canvas_Fit);
        Add("zoomIn", "Vergrößern", () => _canvas.ZoomBy(1.3f));
        Add("zoomOut", "Verkleinern", () => _canvas.ZoomBy(1 / 1.3f));
        Add("straight", "Gerade", () => _session.Quick(PieceKind.Straight, null, _canvas.CenterWorld()));
        Add("curve", "Bogen", () => _session.Quick(PieceKind.Curve, null, _canvas.CenterWorld()));
        Add("switchL", "Weiche links", () => _session.Quick(PieceKind.Switch, false, _canvas.CenterWorld()));
        Add("switchR", "Weiche rechts", () => _session.Quick(PieceKind.Switch, true, _canvas.CenterWorld()));
        Add("buffer", "Prellbock", () => _session.Quick(PieceKind.Buffer, null, _canvas.CenterWorld()));
        Add("crossing", "Kreuzung", () => _session.Quick(PieceKind.Crossing, null, _canvas.CenterWorld()));
        Add("flex", "Flexgleis", () => Dialogs.AskFlex(this, _session.Plan.Gauge, Catalog.Flex(_session.Plan.Gauge, _session.CurrentSystem), key => _session.AttachFlex(key, _canvas.CenterWorld())));
        Add("board", "Anlagenplatte", () => { _session.ClearSelection(); Dialogs.ShowProperties(this, _session, Run); });
        Add("grid", "Raster", () => Toggle(() => _session.ShowGrid = !_session.ShowGrid));
        Add("ends", "Gleisenden", () => Toggle(() => _session.ShowEnds = !_session.ShowEnds));
        Add("labels", "Beschriftung", () => Toggle(() => _session.ShowLabels = !_session.ShowLabels));
        Add("snap", "Einrasten", () => Toggle(() => _session.SnapEnabled = !_session.SnapEnabled));
        Add("toolSelect", "Auswählen", () => SetTool(CanvasTool.Select));

        Pro("heights", "Höhen zeigen", () => Toggle(() => _session.ShowHeights = !_session.ShowHeights));
        Pro("scenery", "Gelände zeigen", () => Toggle(() => _session.ShowScenery = !_session.ShowScenery));
        Pro("sections", "Stromkreise zeigen", () => Toggle(() => _session.ShowSections = !_session.ShowSections));
        Pro("gradient", "Steigung", AskGradient, HasSelection);
        Pro("helix", "Gleiswendel", AskHelix);
        Pro("tunnel", "Tunnel", () => _session.SetTunnel(!_session.SelectedPieces.All(p => p.Tunnel)), HasSelection);
        Pro("toolTerrain", "Gelände formen", () => SetTool(CanvasTool.Terrain));
        Pro("toolWater", "Gewässer", () => SetTool(CanvasTool.Water));
        Pro("toolBuilding", "Gebäude", () => SetTool(CanvasTool.Building));
        Pro("toolTree", "Vegetation", () => SetTool(CanvasTool.Tree));
        Pro("toolFeeder", "Einspeisung", () => SetTool(CanvasTool.Feeder));
        Pro("toolIsolation", "Trennstelle", () => SetTool(CanvasTool.Isolation));
        Pro("toolSignal", "Signal setzen", () => Dialogs.AskSignalType(this, _session.SignalType, t => { _session.SignalType = t; SetTool(CanvasTool.Signal); }));
        Pro("elecCheck", "Elektrik prüfen", ShowElectricCheck);
        Pro("addresses", "Decoder und Adressen", () => Dialogs.ShowAddresses(this, _session, ShowWiring));
        Pro("wiring", "Kabelplan", ShowWiring);
        Pro("view3d", "3D-Ansicht", () => Open3D(View3DMode.Orbit));
        Pro("ride", "Mitfahren", () => Open3D(View3DMode.Ride));
        Pro("daytime", "Tageszeit", () => Open3D(View3DMode.Evening));
        Pro("csv", "Export als CSV", () => Export(BillOfMaterials.ToCsv(_session.Plan), "text/csv", $"{SafeName} – Stückliste.csv"));
        Pro("svg", "Export als SVG", () => Export(SvgExport.Build(_session.Plan), "image/svg+xml", $"{SafeName}.svg"));
    }

    private void _canvas_Fit() => _canvas.Fit();

    private void Toggle(Action change)
    {
        change();
        _canvas.Invalidate();
        _session.Commit();
    }

    private string SafeName
    {
        get
        {
            var n = string.Concat(_session.Plan.Name.Select(ch => System.IO.Path.GetInvalidFileNameChars().Contains(ch) || ch is '/' or ':' ? '_' : ch)).Trim();
            return n.Length == 0 ? "Plan" : n;
        }
    }

    // ---------- Oberfläche ----------

    private static readonly (string Tab, (string Label, string Command, bool Toggle)[] Items)[] Ribbon =
    [
        ("Start", [("Eigenschaften", "props", false), ("↺ 15°", "rotL", false), ("↻ 15°", "rotR", false), ("Spiegeln", "flip", false), ("Anschluss wechseln", "swap", false),
                   ("Duplizieren", "duplicate", false), ("Löschen", "delete", false), ("Alles wählen", "selectAll", false), ("Auswahl aufheben", "selectNone", false),
                   ("Nächstes Gleisende", "nextEnd", false), ("Stückliste", "bom", false), ("Planprüfung", "check", false)]),
        ("Gleise", [("Gerade", "straight", false), ("Bogen", "curve", false), ("Weiche links", "switchL", false), ("Weiche rechts", "switchR", false),
                    ("Kreuzung", "crossing", false), ("Prellbock", "buffer", false), ("Flexgleis…", "flex", false),
                    ("Steigung…", "gradient", false), ("Gleiswendel…", "helix", false), ("Tunnel", "tunnel", false)]),
        ("Gelände", [("Anlagenplatte…", "board", false), ("Gelände formen", "toolTerrain", true), ("Gewässer", "toolWater", true), ("Gebäude", "toolBuilding", true),
                     ("Vegetation", "toolTree", true), ("Gelände zeigen", "scenery", true)]),
        ("Elektrik", [("Einspeisung", "toolFeeder", true), ("Trennstelle", "toolIsolation", true), ("Signal setzen…", "toolSignal", true),
                      ("Adressen…", "addresses", false), ("Stromkreise zeigen", "sections", true), ("Elektrik prüfen", "elecCheck", false), ("Kabelplan", "wiring", false)]),
        ("Ansicht", [("Einpassen", "fit", false), ("Raster", "grid", true), ("Gleisenden", "ends", true), ("Beschriftung", "labels", true),
                     ("Höhen", "heights", true), ("Einrasten", "snap", true)]),
        ("3D", [("3D-Ansicht", "view3d", false), ("Mitfahren", "ride", false), ("Abendstimmung", "daytime", false)]),
        ("Datei", [("Neu…", "new", false), ("Öffnen…", "open", false), ("Speichern…", "save", false), ("Beispielanlage", "sample", false),
                   ("Stückliste als CSV", "csv", false), ("Plan als SVG", "svg", false), ("Lizenz…", "license", false), ("Bedienung", "help", false)]),
    ];

    private void BuildUi()
    {
        Window!.SetStatusBarColor(Color.White);
        Window.SetNavigationBarColor(Color.White);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            Window.DecorView.SystemUiFlags = (SystemUiFlags)((int)SystemUiFlags.LightStatusBar | (Build.VERSION.SdkInt >= BuildVersionCodes.O ? (int)SystemUiFlags.LightNavigationBar : 0));

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(Ui.Window);

        // Kopfzeile
        var header = Ui.Row(this, 8);
        header.SetBackgroundColor(Color.White);
        header.SetPadding(Ui.Dp(12), Ui.Dp(6), Ui.Dp(8), Ui.Dp(2));
        var titles = Ui.Column(this, 0);
        titles.AddView(Ui.Label(this, "TrackMaster Pro", 12, color: Ui.TextSecondary));
        _title = Ui.Label(this, "", 16, true);
        _title.SetSingleLine(true);
        _title.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        titles.AddView(_title);
        header.AddView(titles, Ui.Weight(1, vertical: false));
        _edition = Ui.Label(this, "", 12, true, Color.White);
        _edition.SetPadding(Ui.Dp(8), Ui.Dp(2), Ui.Dp(8), Ui.Dp(2));
        _edition.Click += (_, _) => ShowLicense();
        header.AddView(_edition);
        _undo = Ui.Button(this, "↶", () => Run("undo"));
        _redo = Ui.Button(this, "↷", () => Run("redo"));
        _undo.SetTextSize(Android.Util.ComplexUnitType.Sp, 18);
        _redo.SetTextSize(Android.Util.ComplexUnitType.Sp, 18);
        header.AddView(_undo);
        header.AddView(_redo);
        root.AddView(header, Ui.Fill());

        // Register
        _tabs = Ui.Row(this, 0);
        _tabs.SetBackgroundColor(Color.White);
        _tabs.SetPadding(Ui.Dp(4), 0, Ui.Dp(4), 0);
        foreach (var (tab, _) in Ribbon)
        {
            var name = tab;
            var t = Ui.Label(this, tab, 14);
            t.SetPadding(Ui.Dp(12), Ui.Dp(10), Ui.Dp(12), Ui.Dp(8));
            t.Click += (_, _) => { _tab = name; BuildActions(); };
            _tabs.AddView(t);
        }
        var tabScroll = new HorizontalScrollView(this) { HorizontalScrollBarEnabled = false };
        tabScroll.SetBackgroundColor(Color.White);
        tabScroll.AddView(_tabs);
        root.AddView(tabScroll, Ui.Fill());

        _actions = Ui.Row(this, 6);
        _actions.SetPadding(Ui.Dp(8), Ui.Dp(6), Ui.Dp(8), Ui.Dp(8));
        _actionScroll = new HorizontalScrollView(this) { HorizontalScrollBarEnabled = false };
        _actionScroll.SetBackgroundColor(Color.Rgb(0xF9, 0xF9, 0xF9));
        _actionScroll.AddView(_actions);
        root.AddView(_actionScroll, Ui.Fill());
        root.AddView(new View(this) { Background = new Android.Graphics.Drawables.ColorDrawable(Ui.Border) }, new LinearLayout.LayoutParams(-1, Math.Max(1, Ui.Dp(1))));

        // Zeichenfläche mit Werkzeugleiste, Meldungen und Zoomknöpfen
        var stage = new FrameLayout(this);
        _canvas = new PlanView(this, _session);
        _canvas.PropertiesRequested += () => Run("props");
        _canvas.PolygonChanged += UpdateToolBar;
        stage.AddView(_canvas, new FrameLayout.LayoutParams(-1, -1));

        _toolBar = Ui.Row(this, 8);
        _toolBar.Background = Ui.Rounded(Color.Argb(0xF2, 0xFF, 0xFF, 0xFF), Ui.Border, 8);
        _toolBar.SetPadding(Ui.Dp(10), Ui.Dp(6), Ui.Dp(6), Ui.Dp(6));
        _toolText = Ui.Label(this, "", 13);
        _toolBar.AddView(_toolText, Ui.Weight(1, vertical: false));
        _toolBack = Ui.Button(this, "Punkt zurück", () => _canvas.UndoPolygonPoint());
        _toolFinish = Ui.Button(this, "Fertig", () => _canvas.FinishPolygon(), accent: true);
        _toolBar.AddView(_toolBack);
        _toolBar.AddView(_toolFinish);
        _toolBar.AddView(Ui.Button(this, "Beenden", () => SetTool(CanvasTool.Select)));
        var tlp = new FrameLayout.LayoutParams(-1, -2, GravityFlags.Top) { LeftMargin = Ui.Dp(8), RightMargin = Ui.Dp(8), TopMargin = Ui.Dp(8) };
        stage.AddView(_toolBar, tlp);

        var zoom = Ui.Column(this, 6);
        zoom.AddView(Ui.Button(this, "+", () => Run("zoomIn")));
        zoom.AddView(Ui.Button(this, "−", () => Run("zoomOut")));
        zoom.AddView(Ui.Button(this, "⤢", () => Run("fit")));
        stage.AddView(zoom, new FrameLayout.LayoutParams(-2, -2, GravityFlags.Bottom | GravityFlags.End) { RightMargin = Ui.Dp(10), BottomMargin = Ui.Dp(10) });

        _toast = Ui.Label(this, "", 13.5, color: Color.White);
        _toast.Background = Ui.Rounded(Color.Argb(0xE6, 0x2B, 0x2B, 0x2B), radius: 8);
        _toast.SetPadding(Ui.Dp(12), Ui.Dp(8), Ui.Dp(12), Ui.Dp(8));
        _toast.Visibility = ViewStates.Gone;
        stage.AddView(_toast, new FrameLayout.LayoutParams(-2, -2, GravityFlags.Bottom | GravityFlags.Start) { LeftMargin = Ui.Dp(10), RightMargin = Ui.Dp(70), BottomMargin = Ui.Dp(10) });
        root.AddView(stage, Ui.Weight(1));

        // Bibliothek und Statuszeile
        root.AddView(new View(this) { Background = new Android.Graphics.Drawables.ColorDrawable(Ui.Border) }, new LinearLayout.LayoutParams(-1, Math.Max(1, Ui.Dp(1))));
        _library = new LibraryPanel(this, _session, () => _canvas.CenterWorld());
        root.AddView(_library, Ui.Fill());
        _status = Ui.Label(this, "", 12, color: Ui.TextSecondary);
        _status.SetBackgroundColor(Color.Rgb(0xF0, 0xF0, 0xF0));
        _status.SetPadding(Ui.Dp(12), Ui.Dp(4), Ui.Dp(12), Ui.Dp(4));
        _status.SetSingleLine(true);
        _status.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        root.AddView(_status, Ui.Fill());

        // Randlos ab Android 15: Inhalt nicht unter Status- und Navigationsleiste legen
        root.SetOnApplyWindowInsetsListener(new InsetsListener());
        SetContentView(root);
        BuildActions();
    }

    private sealed class InsetsListener : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View v, WindowInsets insets)
        {
            // Erst ab Android 15 zeichnet die App unter Status- und Navigationsleiste; davor macht das System Platz
            if (Build.VERSION.SdkInt >= BuildVersionCodes.VanillaIceCream)
            {
                var bars = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout() | WindowInsets.Type.Ime());
                v.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);
            }
            return insets;
        }
    }

    private void BuildActions()
    {
        for (var i = 0; i < _tabs.ChildCount; i++)
            if (_tabs.GetChildAt(i) is TextView t)
            {
                var active = t.Text == _tab;
                t.SetTextColor(active ? Ui.Accent : Ui.Text);
                t.SetTypeface(null, active ? TypefaceStyle.Bold : TypefaceStyle.Normal);
                t.Background = active ? new UnderlineDrawable(Ui.Accent, Ui.Dp(3)) : null;
            }
        _actions.RemoveAllViews();
        _toggles.Clear();
        var items = Ribbon.First(r => r.Tab == _tab).Items;
        foreach (var (label, command, toggle) in items)
        {
            var id = command;
            var cmd = _commands[id];
            var text = cmd.Pro && !_license.IsPro ? label + " 🔒" : label;
            var b = Ui.Button(this, text, () => { Run(id); RefreshActions(); });
            b.Tag = id;
            if (toggle) _toggles.Add((b, () => IsOn(id)));
            _actions.AddView(b);
        }
        _actionScroll.ScrollTo(0, 0);
        RefreshActions();
    }

    private bool IsOn(string id) => id switch
    {
        "grid" => _session.ShowGrid,
        "ends" => _session.ShowEnds,
        "labels" => _session.ShowLabels,
        "snap" => _session.SnapEnabled,
        "heights" => _session.ShowHeights,
        "scenery" => _session.ShowScenery,
        "sections" => _session.ShowSections,
        "toolTerrain" => _session.Tool == CanvasTool.Terrain,
        "toolWater" => _session.Tool == CanvasTool.Water,
        "toolBuilding" => _session.Tool == CanvasTool.Building,
        "toolTree" => _session.Tool == CanvasTool.Tree,
        "toolFeeder" => _session.Tool == CanvasTool.Feeder,
        "toolIsolation" => _session.Tool == CanvasTool.Isolation,
        "toolSignal" => _session.Tool == CanvasTool.Signal,
        _ => false,
    };

    private void RefreshActions()
    {
        for (var i = 0; i < _actions.ChildCount; i++)
        {
            if (_actions.GetChildAt(i) is not Button b || b.Tag?.ToString() is not { } id || !_commands.TryGetValue(id, out var cmd)) continue;
            b.Enabled = cmd.CanExecute?.Invoke() ?? true;
            Ui.SetButtonState(b, false, _toggles.Any(t => t.Button == b) && IsOn(id));
        }
        _undo.Enabled = _session.History.CanUndo;
        _redo.Enabled = _session.History.CanRedo;
        Ui.SetButtonState(_undo, false, false);
        Ui.SetButtonState(_redo, false, false);
    }

    private void Refresh()
    {
        var plan = _session.Plan;
        _title.Text = plan.Name + (_session.Dirty ? " •" : "");
        _edition.Text = _license.EditionName == "Pro (Entwicklerversion)" ? "Pro (Entwickler)" : _license.EditionName;
        _edition.Background = Ui.Rounded(_license.IsPro ? Ui.Pro : Ui.TextSecondary, radius: 10);
        var stats = plan.Statistics(_session.Connections);
        var sel = _session.Selection.Count;
        _status.Text = $"Spur {plan.GaugeId} · {stats.Pieces} Gleise · {Fmt.Num(stats.TrackLengthMm / 1000, 2)} m · offene Enden {stats.OpenEnds}" + (sel > 0 ? $" · {sel} gewählt" : "");
        RefreshActions();
        UpdateToolBar();
    }

    private void UpdateToolBar()
    {
        var tool = _session.Tool;
        _toolBar.Visibility = tool == CanvasTool.Select ? ViewStates.Gone : ViewStates.Visible;
        var polygon = tool is CanvasTool.Terrain or CanvasTool.Water;
        _toolFinish.Visibility = _toolBack.Visibility = polygon ? ViewStates.Visible : ViewStates.Gone;
        _toolFinish.Enabled = _canvas.PolygonPoints >= 3;
        _toolBack.Enabled = _canvas.PolygonPoints > 0;
        Ui.SetButtonState(_toolFinish, _toolFinish.Enabled, false);
        Ui.SetButtonState(_toolBack, false, false);
        _toolText.Text = tool switch
        {
            CanvasTool.Terrain => $"Gelände formen: Eckpunkte antippen ({_canvas.PolygonPoints}), dann „Fertig“.",
            CanvasTool.Water => $"Gewässer: Eckpunkte antippen ({_canvas.PolygonPoints}), dann „Fertig“.",
            CanvasTool.Building => "Gebäude: antippen oder Rechteck aufziehen.",
            CanvasTool.Tree => "Vegetation: jeder Tipp setzt einen Baum.",
            CanvasTool.Feeder => "Einspeisung: auf ein Gleis tippen.",
            CanvasTool.Isolation => "Trennstelle: auf eine Gleisverbindung (grüner Punkt) tippen.",
            CanvasTool.Signal => $"{Signal.TypeName(_session.SignalType)}: auf einen Punkt an einer Gleisverbindung tippen.",
            _ => "",
        };
    }

    private void SetTool(CanvasTool tool)
    {
        if (_canvas.IsDrawingPolygon && tool != _session.Tool) _canvas.CancelPolygon();
        _session.Tool = _session.Tool == tool && tool != CanvasTool.Select ? CanvasTool.Select : tool;
        if (_session.Tool != CanvasTool.Select) _session.ClearSelection();
        _canvas.Invalidate();
        UpdateToolBar();
        RefreshActions();
    }

    private void ShowMessage(string text)
    {
        _toast.Text = text;
        _toast.Visibility = ViewStates.Visible;
        if (_hideToast is not null) _handler.RemoveCallbacksAndMessages(null);
        _hideToast = () => _toast.Visibility = ViewStates.Gone;
        _handler.PostDelayed(_hideToast, Math.Clamp(text.Length * 60, 2500, 7000));
    }

    private void RegisterBack()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(0, new BackCallback(this));
    }

    private sealed class BackCallback(MainActivity activity) : Java.Lang.Object, Android.Window.IOnBackInvokedCallback
    {
        public void OnBackInvoked() => activity.HandleBack();
    }

    public override void OnBackPressed() => HandleBack();

    private void HandleBack()
    {
        if (_session.Tool != CanvasTool.Select) { SetTool(CanvasTool.Select); return; }
        if (_session.Selection.Count > 0 || _session.SelectedObject is not null) { _session.ClearSelection(); return; }
        Finish();
    }

    // ---------- Pro-Funktionen ----------

    private void ShowCheck()
    {
        var issues = PlanCheck.Run(_session.Plan);
        _session.Issues = issues;
        _canvas.Invalidate();
        Dialogs.ShowIssues(this, "Planprüfung", issues, Pick);
    }

    private void ShowElectricCheck()
    {
        var report = Electrics.Analyze(_session.Plan);
        _session.Issues = report.Issues;
        if (!_session.ShowSections) { _session.ShowSections = true; _session.Commit(); }
        _canvas.Invalidate();
        Dialogs.ShowIssues(this, "Elektrik prüfen", report.Issues, Pick,
            $"{report.Sections.Count} Stromabschnitte, {report.ReverseLoops.Count} Kehrschleifen. {(report.ThreeRail ? "Mittelleiter: Kehrschleifen ohne Sonderschaltung möglich." : "Zweileiter-System.")}");
    }

    private void Pick(PlanIssue issue)
    {
        if (issue.PieceIds.Count > 0) _session.Select(issue.PieceIds, add: false);
        _canvas.Reveal(issue.X, issue.Y);
    }

    private void AskGradient()
    {
        var sel = _session.SelectedPieces;
        var start = sel.Count > 0 ? sel.Min(p => p.ConnectorHeights().Min()) : 0;
        Dialogs.AskGradient(this, sel.Count, start, (s, p, r) => _session.ApplyGradient(s, p, r));
    }

    private void AskHelix()
    {
        var gauge = _session.Plan.Gauge;
        var curves = _session.CurrentSystem.Pieces.Where(p => p.Kind == PieceKind.Curve && 360 % p.Angle < 0.01).ToList();
        if (curves.Count == 0) curves = gauge.Systems.SelectMany(x => x.Pieces).Where(p => p.Kind == PieceKind.Curve && 360 % p.Angle < 0.01).ToList();
        Dialogs.AskHelix(this, gauge, curves, _session.ActiveConnector is not null, (key, turns, rise, right) =>
        {
            _session.BuildHelix(key, turns, rise, right, _canvas.CenterWorld());
            _canvas.Fit();
        });
    }

    private void ShowWiring()
    {
        var text = Electrics.WiringPlan(_session.Plan);
        Dialogs.ShowText(this, "Kabelplan", text, "Als Text speichern", () =>
        {
            if (_session.RequirePro("Kabelplan")) Export(text, "text/plain", $"{SafeName} – Kabelplan.txt");
        });
    }

    private void Open3D(View3DMode mode)
    {
        View3DActivity.Plan = _session.Plan;
        var intent = new Intent(this, typeof(View3DActivity));
        intent.PutExtra("mode", (int)mode);
        intent.PutExtra("start", _session.SelectedPieces.FirstOrDefault()?.Id ?? -1);
        StartActivity(intent);
    }

    // ---------- Lizenz ----------

    private void ShowLicense() => Dialogs.ShowLicense(this, _license, () => Pick(RequestLicense));

    private void InstallLicense(string text)
    {
        var result = _license.Install(text);
        if (result.IsValid)
        {
            Dialogs.Message(this, "Lizenz", $"Pro-Lizenz eingespielt: {_license.Summary}");
            BuildActions();
            Refresh();
            _canvas.Invalidate();
        }
        else
            Dialogs.Message(this, "Lizenz", $"Die Lizenzdatei wurde nicht übernommen: {result.StatusText}.");
    }

    // ---------- Dateien ----------

    private void Pick(int request)
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        StartActivityForResult(intent, request);
    }

    private void SavePlan()
    {
        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("application/json");
        intent.PutExtra(Intent.ExtraTitle, $"{SafeName}.trackmaster.json");
        StartActivityForResult(intent, RequestSave);
    }

    private void Export(string content, string mime, string fileName)
    {
        _pendingExport = (content, mime);
        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType(mime);
        intent.PutExtra(Intent.ExtraTitle, fileName);
        StartActivityForResult(intent, RequestExport);
    }

    protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (resultCode != Result.Ok || data?.Data is not { } uri) return;
        try
        {
            switch (requestCode)
            {
                case RequestOpen:
                    OpenUri(uri);
                    break;
                case RequestSave:
                    WriteText(uri, PlanSerializer.ToJson(_session.Plan));
                    _session.MarkSaved(DisplayName(uri) ?? $"{SafeName}.trackmaster.json");
                    break;
                case RequestLicense:
                    InstallLicense(ReadText(uri));
                    break;
                case RequestExport when _pendingExport is { } export:
                    WriteText(uri, export.Content);
                    ShowMessage($"Gespeichert: {DisplayName(uri)}");
                    _pendingExport = null;
                    break;
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or Java.Lang.Exception)
        {
            Dialogs.Message(this, "Datei", $"Die Datei konnte nicht verarbeitet werden: {e.Message}");
        }
    }

    private void OpenUri(Android.Net.Uri uri)
    {
        try
        {
            var text = ReadText(uri);
            _session.OpenJson(text, DisplayName(uri));
            _library.Refresh(force: true);
            _canvas.Fit();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or Java.Lang.Exception)
        {
            Dialogs.Message(this, "Öffnen", $"Die Datei ist kein TrackMaster-Plan: {e.Message}");
        }
    }

    private string ReadText(Android.Net.Uri uri)
    {
        using var stream = ContentResolver!.OpenInputStream(uri) ?? throw new IOException("Datei nicht lesbar.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void WriteText(Android.Net.Uri uri, string text)
    {
        using var stream = ContentResolver!.OpenOutputStream(uri, "wt") ?? throw new IOException("Datei nicht beschreibbar.");
        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
        writer.Write(text);
    }

    private string? DisplayName(Android.Net.Uri uri)
    {
        using var cursor = ContentResolver!.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
        return cursor is not null && cursor.MoveToFirst() ? cursor.GetString(0) : uri.LastPathSegment;
    }
}

/// <summary>Unterstrich unter dem aktiven Register.</summary>
public sealed class UnderlineDrawable(Color color, int thickness) : Android.Graphics.Drawables.Drawable
{
    private readonly Paint _paint = new() { Color = color };

    public override void Draw(Canvas canvas)
    {
        var b = Bounds;
        canvas.DrawRect(b.Left + thickness * 2, b.Bottom - thickness, b.Right - thickness * 2, b.Bottom, _paint);
    }

    public override void SetAlpha(int alpha) { }
    public override void SetColorFilter(ColorFilter? colorFilter) { }
    public override int Opacity => (int)Format.Translucent;
}
