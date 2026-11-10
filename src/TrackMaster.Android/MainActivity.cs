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

// Nur für die Update-Prüfung (version.json), wenn eine Adresse eingetragen ist
[assembly: UsesPermission(Android.Manifest.Permission.Internet)]

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
    private const int RequestOpen = 1, RequestSave = 2, RequestLicense = 3, RequestExport = 4, RequestModule = 5, RequestBackground = 6, RequestPrices = 7;

    private readonly PlanSession _session = new();
    private LicenseManager _license = null!;
    private PlanView _canvas = null!;
    private LibraryPanel _library = null!;
    private LinearLayout _tabs = null!;
    private LinearLayout _actions = null!;
    private LinearLayout _levelBar = null!;
    private HorizontalScrollView _actionScroll = null!;
    private TextView _title = null!, _edition = null!, _status = null!, _toast = null!;
    private LinearLayout _toolBar = null!;
    private TextView _toolText = null!;
    private Button _toolFinish = null!, _toolBack = null!;
    private Button _undo = null!, _redo = null!, _history = null!;
    private string _tab = Lang.T("Start");
    private readonly Dictionary<string, Command> _commands = [];
    private readonly List<(Button Button, Func<bool> Active)> _toggles = [];
    private (byte[] Data, string Mime)? _pendingExport;
    private readonly Handler _handler = new(Looper.MainLooper!);
    private Action? _hideToast;

    private sealed record Command(string Name, Action Execute, bool Pro, Func<bool>? CanExecute);

    private string AutosavePath => System.IO.Path.Combine(FilesDir!.AbsolutePath, "autosave.trackmaster.json");

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Ui.Density = Resources!.DisplayMetrics!.Density;
        _appearance = Appearance.Load(this);
        Appearance.Use(_appearance);
#if DEBUG
        const bool developer = true;
#else
        const bool developer = false;
#endif
        var deviceId = AndroidDeviceId();
        LicenseActivation? activation = null;
        if (LicenseActivation.DefaultServerUrl is { } activationUrl)
        {
            var (load, save) = LicenseActivation.FileStore(System.IO.Path.Combine(FilesDir!.AbsolutePath, LicenseActivation.FileName));
            activation = new LicenseActivation(activationUrl, deviceId, "Android", $"{Build.Manufacturer} {Build.Model}".Trim(), load, save);
        }
        _license = new LicenseManager(System.IO.Path.Combine(FilesDir!.AbsolutePath, AppFolders.LicenseFileName), LicensePublicKey.Pem, developer, machineId: deviceId, activation: activation);
        _session.IsPro = () => _license.IsPro;
        _session.ProRequired += feature => Dialogs.ShowProRequired(this, feature, ShowLicense);
        _session.Message += ShowMessage;
        _session.BoardPartClicked += (module, leg) => _canvas.Post(() =>
        {
            if (leg >= 0) Run("legs");
            else Dialogs.AskBoardPart(this, _session, module, () => Run("modules"), () => Run("legs"));
        });
        RestoreAutosave();
        RegisterCommands();
        BuildUi();
        _session.Changed += Refresh;
        if (Intent?.Data is { } data && Intent.Action == Intent.ActionView) OpenUri(data);
        Refresh();
        RegisterBack();
        _ = CheckForUpdates(manual: false);
        if (_license.ActivationDue) _ = SyncActivation(interactive: false);
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
                if (demo.StartsWith("tab:")) { _tab = Lang.T(demo[4..]); BuildActions(); }
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
        Add("new", Lang.T("Neu"), () => Dialogs.AskNewPlan(this, _session.Plan.GaugeId, setup =>
        {
            _session.NewPlan(setup);
            if (setup.Benchwork is { Manual: true }) SetTool(CanvasTool.Beam);
            _library.Refresh(force: true);
            _canvas.Fit();
        }));
        Add("open", Lang.T("Öffnen"), () => Pick(RequestOpen));
        Add("save", Lang.T("Speichern"), () => { if (_session.RequireSave()) SavePlan(); });
        Add("sample", Lang.T("Beispielanlage"), () => { _session.LoadSample(); _library.Refresh(force: true); _canvas.Fit(); });
        Add("license", Lang.T("Lizenz"), ShowLicense);
        Add("help", Lang.T("Bedienung"), () => Dialogs.ShowHelp(this));
        Add("undo", Lang.T("Rückgängig"), _session.Undo, () => _session.History.CanUndo);
        Add("redo", Lang.T("Wiederholen"), _session.Redo, () => _session.History.CanRedo);
        Add("history", Lang.T("Verlauf"), ShowHistory, () => _session.History.CanUndo || _session.History.CanRedo);
        Add("rotL", Lang.T("Drehen"), () => _session.Rotate(-15), HasSelection);
        Add("rotR", Lang.T("Drehen"), () => _session.Rotate(15), HasSelection);
        Add("rotFree", Lang.T("Frei drehen"), AskFreeRotation, HasSelection);
        Add("flip", Lang.T("Spiegeln"), _session.Flip, HasSelection);
        Add("swap", Lang.T("Anschluss wechseln"), _session.SwapAnchor, () => _session.Selection.Count == 1);
        Add("duplicate", Lang.T("Duplizieren"), _session.Duplicate, HasSelection);
        Add("copy", Lang.T("Kopieren"), CopyToClipboard, () => HasSelection() || _session.SelectedObject is not null);
        Add("paste", Lang.T("Einfügen"), PasteFromClipboard);
        Add("delete", Lang.T("Löschen"), _session.Delete, () => HasSelection() || _session.SelectedObject is not null);
        Add("selectAll", Lang.T("Alles auswählen"), _session.SelectAll);
        Add("selectNone", Lang.T("Auswahl aufheben"), _session.ClearSelection);
        Add("nextEnd", Lang.T("Nächstes Gleisende"), () => { if (_session.NextOpenEnd() is { } p) _canvas.Reveal(p.X, p.Y); });
        Add("repeat", Lang.T("Gleis wiederholen"), () => _session.RepeatLast(_canvas.CenterWorld()));
        Add("props", Lang.T("Eigenschaften"), () => Dialogs.ShowProperties(this, _session, Run));
        Pro("bom", Lang.T("Stückliste"), () => Dialogs.ShowBillOfMaterials(this, _session, _license.IsPro, () => Run("csv"), () => Pick(RequestPrices, "text/*")));
        Add("check", Lang.T("Planprüfung"), ShowCheck);
        Add("clearIssues", Lang.T("Markierungen entfernen"), ClearIssues, () => _session.Issues.Count > 0);
        Add("fit", Lang.T("Einpassen"), _canvas_Fit);
        Add("zoomIn", Lang.T("Vergrößern"), () => _canvas.ZoomBy(1.3f));
        Add("zoomOut", Lang.T("Verkleinern"), () => _canvas.ZoomBy(1 / 1.3f));
        Add("viewRotL", Lang.T("Ansicht nach links drehen"), () => _canvas.RotateViewBy(-15));
        Add("viewRotR", Lang.T("Ansicht nach rechts drehen"), () => _canvas.RotateViewBy(15));
        Add("viewReset", Lang.T("Ansicht gerade ausrichten"), () => _canvas.SetViewRotation(0));
        Add("viewAngle", Lang.T("Ansicht drehen"), AskViewAngle);
        Add("appearance", Lang.T("Darstellung"), ChangeAppearance);
        Add("language", "Sprache / Language", ChangeLanguage);
        Add("about", Lang.T("Über das Programm"), ShowAbout);
        Add("update", Lang.T("Nach Updates suchen"), () => _ = CheckForUpdates(manual: true), () => UpdateCheck.ConfiguredUrl(typeof(MainActivity).Assembly) is not null);
        Add("straight", Lang.T("Gerade"), () => _session.Quick(PieceKind.Straight, null, _canvas.CenterWorld()));
        Add("curve", Lang.T("Bogen"), () => _session.Quick(PieceKind.Curve, null, _canvas.CenterWorld()));
        Add("switchL", Lang.T("Weiche links"), () => _session.Quick(PieceKind.Switch, false, _canvas.CenterWorld()));
        Add("switchR", Lang.T("Weiche rechts"), () => _session.Quick(PieceKind.Switch, true, _canvas.CenterWorld()));
        Add("buffer", Lang.T("Prellbock"), () => _session.Quick(PieceKind.Buffer, null, _canvas.CenterWorld()));
        Add("crossing", Lang.T("Kreuzung"), () => _session.Quick(PieceKind.Crossing, null, _canvas.CenterWorld()));
        Add("flex", Lang.T("Flexgleis"), () => Dialogs.AskFlex(this, _session.Plan.Gauge, _session.CurrentFlex, key => _session.AttachFlex(key, _canvas.CenterWorld())));
        Add("toolFlex", Lang.T("Flexgleis ziehen"), () => SetTool(CanvasTool.Flex));
        Add("flexConnect", Lang.T("Lücke schließen"), () => _session.ConnectEnds(_session.CurrentFlex));
        Add("board", Lang.T("Anlagenplatte"), () => { _session.ClearSelection(); Dialogs.ShowProperties(this, _session, Run); });
        Add("modules", Lang.T("Module"), () => Dialogs.AskModules(this, _session.Plan, edits => { _session.ApplyModules(edits); _canvas.Fit(); }, () => Pick(RequestModule)));
        Add("legs", Lang.T("Schenkel einzeln"), () => Dialogs.AskLegs(this, _session));
        Add("levels", Lang.T("Ebenen"), () => Dialogs.AskLevels(this, _session));
        Add("boardRotate", Lang.T("Anlage drehen"), () => _session.RotateBoard());
        Add("benchwork", Lang.T("Unterbau"), () => { if (_session.Plan.Benchwork is null) AskBenchwork(); else ShowBenchwork(); });
        Add("grid", Lang.T("Raster"), () => Toggle(() => _session.ShowGrid = !_session.ShowGrid));
        Add("ends", Lang.T("Gleisenden"), () => Toggle(() => _session.ShowEnds = !_session.ShowEnds));
        Add("labels", Lang.T("Beschriftung"), () => Toggle(() => _session.ShowLabels = !_session.ShowLabels));
        Add("snap", Lang.T("Einrasten"), () => Toggle(() => _session.SnapEnabled = !_session.SnapEnabled));
        Add("toolSelect", Lang.T("Auswählen"), () => SetTool(CanvasTool.Select));

        Add("heights", Lang.T("Höhen zeigen"), () => Toggle(() => _session.ShowHeights = !_session.ShowHeights));
        Add("scenery", Lang.T("Gelände zeigen"), () => Toggle(() => _session.ShowScenery = !_session.ShowScenery));
        Add("sections", Lang.T("Stromkreise zeigen"), () => Toggle(() => _session.ShowSections = !_session.ShowSections));
        Add("gradient", Lang.T("Steigung"), AskGradient, HasSelection);
        Add("helix", Lang.T("Gleiswendel"), AskHelix);
        Add("gauge", Lang.T("Spurweite"), AskGauge);
        Pro("frame", Lang.T("Spantenbau"), AskFrame);
        Pro("rampCut", Lang.T("Trasse aus Auswahl"), AskRampCut, HasSelection);
        Add("tunnel", "Tunnel", () => _session.SetTunnel(!_session.SelectedPieces.All(p => p.Tunnel)), HasSelection);
        Add("toolTerrain", Lang.T("Gelände formen"), () => SetTool(CanvasTool.Terrain));
        Add("toolBoard", Lang.T("Platte ziehen"), () => SetTool(CanvasTool.Board));
        Add("toolWater", Lang.T("Gewässer"), () => SetTool(CanvasTool.Water));
        Add("toolBuilding", Lang.T("Gebäude"), () => SetTool(CanvasTool.Building));
        foreach (var (cmd, cat) in ModelCommands)
            Add(cmd, cat, () =>
            {
                if (ModelToolIn(cat)) { SetTool(CanvasTool.Select); return; }
                Dialogs.AskModel(this, cat, ModelLibrary.All.Where(m => m.Category == cat).ToList(), _session.Tool == CanvasTool.Model ? _session.ModelId : null, SetModelTool);
            });
        Add("toolTree", Lang.T("Vegetation"), () => SetTool(CanvasTool.Tree));
        Add("toolLabel", "Text", () => SetTool(CanvasTool.Label));
        Add("toolDimension", Lang.T("Maß"), () => SetTool(CanvasTool.Dimension));
        Add("toolBeam", Lang.T("Leiste ziehen"), () => SetTool(CanvasTool.Beam));
        Add("addPlate", Lang.T("Platte auflegen"), () => { SetTool(CanvasTool.Select); _session.AddPlate(); });
        Add("toolDistributor", Lang.T("Verteiler"), () =>
        {
            if (_session.Tool == CanvasTool.Power && _session.PowerType == PowerSourceType.Verteiler) { SetTool(CanvasTool.Select); return; }
            var colors = PowerSupply.DistributorColors;
            Dialogs.Choose(this, Lang.T("Verteiler setzen"), colors.Select(c => (Lang.F($"Verteiler {Lang.T(c.Name)}"), (string?)null, string.Equals(c.Hex, _session.DistributorColor, StringComparison.OrdinalIgnoreCase))).ToList(),
                i => { _session.DistributorColor = colors[i].Hex; _session.PowerType = PowerSourceType.Verteiler; SetTool(CanvasTool.Power); },
                Lang.T("Farbe wählen, dann an die Stelle im Plan tippen. Üblich: braun für Masse, rot für Bahnstrom, gelb für Weichen und Signale, blau und schwarz für Licht und Zubehör."));
        });
        Add("toolPower", Lang.T("Trafo / Zentrale"), () =>
        {
            if (_session.Tool == CanvasTool.Power && _session.PowerType != PowerSourceType.Verteiler && _session.ModuleModel is null) { SetTool(CanvasTool.Select); return; }
            var types = PowerSupply.TypesFor(_session.Plan.Operation);
            Dialogs.Choose(this, Lang.T("Trafo / Zentrale setzen"), types.Select(t => (PowerSupply.TypeName(t), (string?)PowerSupply.Description(t), false)).ToList(),
                i => { _session.PowerType = types[i]; SetTool(CanvasTool.Power); },
                Lang.F($"Betrieb: {PowerSupply.ModeName(_session.Plan.Operation)}. Art wählen, dann an die Stelle im Plan tippen – auch neben die Platte, wo das Gerät später steht."));
        });
        Add("toolIsolation", Lang.T("Trennstelle"), () => SetTool(CanvasTool.Isolation));
        Add("toolSignal", Lang.T("Signal setzen"), () => Dialogs.AskSignalType(this, _session.SignalType, _session.SignalSystem, (t, sy) => { _session.SignalType = t; _session.SignalSystem = sy; SetTool(CanvasTool.Signal); }));
        Add("elecCheck", Lang.T("Elektrik prüfen"), ShowElectricCheck);
        Add("current", Lang.T("Betrieb und Stromart"), () =>
        {
            // erst Betrieb (digital / analog), dann Stromart (Gleich- / Wechselstrom)
            var modes = Enum.GetValues<PowerMode>();
            var kinds = Enum.GetValues<CurrentKind>();
            var items = modes.Select(m => (Lang.F($"Betrieb: {PowerSupply.ModeName(m)}"), (string?)(m == PowerMode.Digital ? Lang.T("Zentrale und Booster") : Lang.T("Fahrtrafo")), _session.Plan.Operation == m))
                .Concat(kinds.Select(k => (Electrics.CurrentName(k), (string?)null, _session.Plan.Current == k))).ToList();
            Dialogs.Choose(this, Lang.T("Betrieb und Stromart"), items,
                i => { if (i < modes.Length) _session.SetOperation(modes[i]); else _session.SetCurrent(kinds[i - modes.Length]); }, Electrics.CurrentHint(_session.Plan));
        });
        Add("elecLabels", Lang.T("Nur Elektrik beschriften"), () => Toggle(() => _session.ElectricLabelsOnly = !_session.ElectricLabelsOnly));
        Add("toolWire", Lang.T("Kabel verlegen"), () =>
        {
            if (_session.Tool == CanvasTool.Wire) { SetTool(CanvasTool.Select); return; }
            var uses = Enum.GetValues<WireUse>();
            var current = Electrics.Current(_session.Plan);
            Dialogs.Choose(this, Lang.T("Kabel verlegen"), uses.Select(u => (WireLayout.UseName(u), (string?)WireLayout.ColorName(WireLayout.DefaultColor(u, current)), u == _session.WireUse)).ToList(),
                i => { _session.WireUse = uses[i]; SetTool(CanvasTool.Wire); });
        });
        Add("wireFromSource", Lang.T("Kabel von hier verlegen"), () => StartWireFromSource(false));
        Add("groundFromSource", Lang.T("Masse-Kabel von hier"), () => StartWireFromSource(true));
        Add("addresses", Lang.T("Decoder und Adressen"), () => Dialogs.ShowAddresses(this, _session, ShowWiring));
        Add("wiring", Lang.T("Kabelplan"), ShowWiring);
        Add("wires", Lang.T("Kabel zeigen"), () => Toggle(() => _session.ShowWires = !_session.ShowWires));
        Add("numberElectrics", Lang.T("Elektrik nummerieren"), () => _session.NumberElectrics());
        Add("detectorWires", Lang.T("Rückmelder mit Modul verbinden"), () => { var on = !_session.Plan.DetectorWiring; if (on) _session.ShowWires = true; _session.SetDetectorWiring(on); });
        Add("routes", Lang.T("Fahrstraßen"), ShowRoutes);
        Pro("controlExport", Lang.T("Export für Steuerprogramme"), ShowControlExport);
        Add("view3d", Lang.T("3D-Ansicht"), () => Open3D(View3DMode.Orbit));
        Add("ride", Lang.T("Mitfahren"), () => Open3D(View3DMode.Ride));
        Add("daytime", Lang.T("Tageszeit"), () => Open3D(View3DMode.Evening));
        Pro("csv", Lang.T("Export als CSV"), () => Export(BillOfMaterials.ToCsv(_session.Plan), "text/csv", Lang.F($"{SafeName} – Stückliste.csv")));
        Pro("pdf", Lang.T("Drucken als PDF"), () => Export(PlanPdf.Build(_session.Plan, _license.IsPro), "application/pdf", $"{SafeName}.pdf"));
        Pro("svg", Lang.T("Export als SVG"), () => Export(SvgExport.Build(_session.Plan), "image/svg+xml", $"{SafeName}.svg"));
        Pro("dxf", Lang.T("Export als DXF"), () => Export(DxfExport.Build(_session.Plan), "application/dxf", $"{SafeName}.dxf"));
        Pro("png", Lang.T("Export als PNG"), ExportPng);

        // Raum, Hintergrundbild, Anordnen, Parallelgleis, Höhenprofil, Rückmelder
        bool Several() => _session.Selection.Count > 1;
        bool HasBackground() => _session.Plan.Background is not null;
        Add("lock", Lang.T("Sperren"), _session.ToggleLock, HasSelection);
        Add("group", Lang.T("Gruppieren"), _session.GroupSelection, Several);
        Add("ungroup", Lang.T("Gruppe auflösen"), _session.Ungroup, HasSelection);
        Add("alignL", Lang.T("Links ausrichten"), () => _session.Align(AlignMode.Left), Several);
        Add("alignR", Lang.T("Rechts ausrichten"), () => _session.Align(AlignMode.Right), Several);
        Add("alignT", Lang.T("Oben ausrichten"), () => _session.Align(AlignMode.Top), Several);
        Add("alignB", Lang.T("Unten ausrichten"), () => _session.Align(AlignMode.Bottom), Several);
        Add("alignCX", Lang.T("Mittig waagrecht"), () => _session.Align(AlignMode.CenterX), Several);
        Add("alignCY", Lang.T("Mittig senkrecht"), () => _session.Align(AlignMode.CenterY), Several);
        Add("parallel", Lang.T("Parallelgleis"), () => Dialogs.AskParallel(this, _session.Plan.Gauge, _session.CurrentSystem, _session.AddParallel), HasSelection);
        Add("profile", Lang.T("Höhenprofil"), () => { if (_session.Profile() is { } profile) Dialogs.ShowProfile(this, profile); });
        Add("roomRect", Lang.T("Raum"), () => Dialogs.AskRoom(this, _session.Plan, (x, y, w, d, wall) => { _session.AddRoom(x, y, w, d, wall); _canvas.Fit(); }));
        Add("toolRoom", Lang.T("Raum zeichnen"), () => SetTool(CanvasTool.Room));
        Add("toolDoor", Lang.T("Tür"), () => SetObstacleTool(ObstacleType.Tuer));
        Add("toolWindow", Lang.T("Fenster"), () => SetObstacleTool(ObstacleType.Fenster));
        Add("toolColumn", Lang.T("Säule"), () => SetObstacleTool(ObstacleType.Saeule));
        Add("toolHeater", Lang.T("Heizkörper"), () => SetObstacleTool(ObstacleType.Heizkoerper));
        Add("toolFurniture", Lang.T("Möbel"), () => SetObstacleTool(ObstacleType.Moebel));
        Add("bgLoad", Lang.T("Hintergrundbild laden"), () => Pick(RequestBackground, "image/*"));
        Add("bgEdit", Lang.T("Hintergrundbild"), AskBackground, HasBackground);
        Add("bgScale", Lang.T("Bild nach Maß skalieren"), AskScaleBackground, HasBackground);
        Add("bgToggle", Lang.T("Hintergrundbild zeigen"), () => _session.EditBackground(b => b.Visible = !b.Visible), HasBackground);
        Add("toolDetector", Lang.T("Rückmelder"), () =>
        {
            if (_session.Tool == CanvasTool.Detector) { SetTool(CanvasTool.Select); return; }
            Dialogs.AskDetectorType(this, _session.DetectorType, t => { _session.DetectorType = t; SetTool(CanvasTool.Detector); });
        });
    }

    /// <summary>Befehle für die Modellkategorien im Register „Ausstattung“.</summary>
    private static IReadOnlyList<(string Command, string Category)> ModelCommands =>
        [("modelsTunnel", BuiltinModels.CatTunnel), ("modelsStation", BuiltinModels.CatStation), ("modelsRailway", BuiltinModels.CatRailway), ("modelsHouses", BuiltinModels.CatHouses)];

    private bool ModelToolIn(string category) => _session.Tool == CanvasTool.Model && _session.CurrentModel is { } m && m.Category == category;

    private void SetModelTool(string id)
    {
        // Gleise gewählt und Brücke, Stützmauer oder Bahnsteig: gleich entlang der Auswahl setzen
        if (_session.Tool != CanvasTool.Model && _session.PlaceModelOnSelection(id) is not null) { _session.ModelId = id; UpdateToolBar(); RefreshActions(); return; }
        _session.ModelId = id;
        if (_session.Tool == CanvasTool.Model) { UpdateToolBar(); RefreshActions(); return; }
        SetTool(CanvasTool.Model);
    }

    private void SetObstacleTool(ObstacleType type)
    {
        if (_session.Tool == CanvasTool.Obstacle && _session.ObstacleType == type) { SetTool(CanvasTool.Select); return; }
        _session.ObstacleType = type;
        if (_session.Tool == CanvasTool.Obstacle) { UpdateToolBar(); RefreshActions(); return; }
        SetTool(CanvasTool.Obstacle);
    }

    private void AskBackground()
    {
        if (_session.Plan.Background is not { } bg) return;
        Dialogs.AskBackground(this, bg,
            (x, y, w, opacity, visible) => _session.EditBackground(b => { b.X = x; b.Y = y; b.Width = w; b.Opacity = opacity; b.Visible = visible; }),
            () => _session.SetBackground(null), () => Pick(RequestBackground, "image/*"));
    }

    private void AskScaleBackground()
    {
        var dim = _session.SelectedObject is { } id ? _session.Plan.Scenery.FirstOrDefault(s => s.Id == id && s.Kind == SceneryKind.Dimension) : null;
        if (dim is null || dim.Points.Count != 2)
        {
            Dialogs.Message(this, Lang.T("Nach Maß skalieren"), Lang.T("So geht’s: Mit dem Werkzeug „Maß“ (Start › Beschriften) eine Linie über eine Strecke im Bild ziehen, deren echte Länge du kennst – z. B. eine Wand oder ein Gleis. Die Maßlinie anklicken und dann hier die echte Länge eingeben."));
            return;
        }
        var measured = Math.Sqrt(Math.Pow(dim.Points[1].X - dim.Points[0].X, 2) + Math.Pow(dim.Points[1].Y - dim.Points[0].Y, 2));
        Dialogs.AskScaleBackground(this, measured, len => _session.ScaleBackground(len));
    }

    /// <summary>Plan als PNG: alles im Blick, längste Seite etwa 4000 Pixel.</summary>
    private void ExportPng()
    {
        var plan = _session.Plan;
        var (x0, y0, x1, y1) = plan.Bounds();
        const double margin = 40;
        double w = x1 - x0 + 2 * margin, h = y1 - y0 + 2 * margin;
        var scale = (float)Math.Min(4, 4000 / Math.Max(w, h));
        int pw = (int)Math.Ceiling(w * scale), ph = (int)Math.Ceiling(h * scale);
        using var bitmap = Bitmap.CreateBitmap(pw, ph, Bitmap.Config.Argb8888!)!;
        using (var c = new Canvas(bitmap))
        {
            c.DrawColor(Color.White);
            c.Scale(scale, scale);
            c.Translate((float)(margin - x0), (float)(margin - y0));
            new PlanPainter(plan, new HashSet<int>(), false).Draw(c, scale, _session.ShowScenery, new HashSet<int>(), null);
        }
        using var stream = new MemoryStream();
        bitmap.Compress(Bitmap.CompressFormat.Png!, 100, stream);
        Export(stream.ToArray(), "image/png", $"{SafeName}.png");
    }

    /// <summary>Bild als Hintergrund übernehmen; große Bilder werden auf 2400 Pixel verkleinert und als JPEG gespeichert.</summary>
    private void LoadBackground(byte[] bytes)
    {
        if (ImageInfo.Read(bytes) is not { Width: > 0, Height: > 0 } info)
        {
            Dialogs.Message(this, Lang.T("Hintergrundbild"), Lang.T("Das Bild ließ sich nicht lesen (PNG, JPEG, GIF oder WebP)."));
            return;
        }
        const int max = 2400;
        var mime = info.Mime;
        int pw = info.Width, ph = info.Height;
        if (Math.Max(pw, ph) > max || bytes.Length > 3_000_000)
        {
            var sample = 1;
            while (Math.Max(pw, ph) / (sample * 2) >= max) sample *= 2;
            using var decoded = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, new BitmapFactory.Options { InSampleSize = sample });
            if (decoded is null) { Dialogs.Message(this, Lang.T("Hintergrundbild"), Lang.T("Das Bild ließ sich nicht lesen (PNG, JPEG, GIF oder WebP).")); return; }
            var f = Math.Min(1, (double)max / Math.Max(decoded.Width, decoded.Height));
            using var scaled = Bitmap.CreateScaledBitmap(decoded, Math.Max(1, (int)(decoded.Width * f)), Math.Max(1, (int)(decoded.Height * f)), true)!;
            using var ms = new MemoryStream();
            scaled.Compress(Bitmap.CompressFormat.Jpeg!, 85, ms);
            bytes = ms.ToArray();
            mime = "image/jpeg";
            pw = scaled.Width;
            ph = scaled.Height;
        }
        var old = _session.Plan.Background;
        _session.SetBackground(new PlanBackground
        {
            Data = Convert.ToBase64String(bytes), Mime = mime, PixelWidth = pw, PixelHeight = ph,
            X = old?.X ?? 0, Y = old?.Y ?? 0, Width = old?.Width ?? _session.Plan.BoardWidth, Opacity = old?.Opacity ?? 0.5,
        });
        _canvas.Fit();
    }

    private void _canvas_Fit() => _canvas.Fit();

    private AppearanceSettings _appearance = new();

    private void ChangeAppearance() => Dialogs.AskAppearance(this, _appearance, chosen =>
    {
        // Neu aufbauen: der Plan kommt über die automatische Sicherung zurück
        Appearance.Save(this, chosen);
        Recreate();
    });

    /// <summary>Verlauf: bis zu 20 Schritte in jede Richtung, ein Tipp springt dorthin.</summary>
    private void ShowHistory()
    {
        var redo = _session.History.RedoLabels;
        var undo = _session.History.UndoLabels;
        var items = new List<string>();
        var steps = new List<int>();
        for (var i = redo.Count - 1; i >= 0; i--) { items.Add($"↷ {Lang.F($"{i + 1} vor")}  {redo[i]}"); steps.Add(i + 1); }
        items.Add($"● {Lang.T("Aktueller Stand")}"); steps.Add(0);
        for (var i = 0; i < undo.Count; i++) { items.Add($"↶ {Lang.F($"{i + 1} zurück")}  {undo[i]}"); steps.Add(-(i + 1)); }
        new AlertDialog.Builder(this).SetTitle(Lang.T("Verlauf"))!
            .SetItems(items.ToArray(), (_, e) =>
            {
                var n = steps[e.Which];
                if (n > 0) _session.Redo(n);
                else if (n < 0) _session.Undo(-n);
            })!
            .SetNegativeButton(Lang.T("Schließen"), (_, _) => { })!
            .Show();
    }

    /// <summary>Spurweite wählen (früher als Knöpfe in der Gleisbibliothek).</summary>
    private void AskGauge()
    {
        var gauges = Catalog.Gauges.ToList();
        var items = gauges.Select(g => (g.Id == _session.Plan.GaugeId ? "● " : "   ") + Lang.F($"Spur {g.Id} · {g.Scale} · {Fmt.Num(g.TrackWidth)} mm")).ToArray();
        new AlertDialog.Builder(this).SetTitle(Lang.T("Spurweite"))!
            .SetItems(items, (_, e) => { if (_session.SetGauge(gauges[e.Which].Id)) _library.Refresh(force: true); })!
            .SetNegativeButton(Lang.T("Schließen"), (_, _) => { })!
            .Show();
    }

    private void ChangeLanguage()
    {
        var langs = new[] { Language.Deutsch, Language.English };
        new AlertDialog.Builder(this).SetTitle("Sprache / Language")!
            .SetSingleChoiceItems(new[] { "Deutsch", "English" }, Array.IndexOf(langs, Lang.Current), (_, e) =>
            {
                var chosen = langs[e.Which];
                if (chosen == Lang.Current) return;
                Lang.Save(chosen);
                Dialogs.Message(this, "Sprache / Language", "Die Sprache gilt nach dem nächsten Start der App.\nThe language applies after the app is restarted.");
            })!
            .SetNegativeButton("OK", (_, _) => { })!
            .Show();
    }

    private void AskViewAngle() => Dialogs.AskAngle(this, Lang.T("Ansicht drehen"),
        Lang.T("Dreht nur die Ansicht – der Plan selbst bleibt unverändert. Mit zwei Fingern lässt sich die Ansicht auch direkt drehen; Tippen auf den Kompass richtet sie wieder gerade aus."),
        Math.Round(_canvas.ViewRotation, 1), Lang.T("Drehen"), a => _canvas.SetViewRotation(a));

    private void AskFreeRotation() => Dialogs.AskAngle(this, Lang.T("Auswahl frei drehen"),
        Lang.T("Dreht die gewählten Gleise um ihren gemeinsamen Mittelpunkt, um einen beliebigen Winkel."),
        7.5, Lang.T("Drehen"), a => _session.Rotate(a));

    private async Task CheckForUpdates(bool manual)
    {
        if (UpdateCheck.ConfiguredUrl(typeof(MainActivity).Assembly) is not { } url) return;
        if (!manual && !UpdateCheck.AutoEnabled) return;
        var info = await UpdateCheck.FetchAsync(url, TimeSpan.FromSeconds(manual ? 15 : 5));
        var current = typeof(MainActivity).Assembly.GetName().Version;
        RunOnUiThread(() =>
        {
            if (IsFinishing) return;
            string? text = info is null ? (manual ? Lang.T("Die Update-Prüfung hat keine Antwort bekommen. Bitte die Internetverbindung prüfen.") : null)
                : !UpdateCheck.IsNewer(info, current) ? (manual ? Lang.F($"Version {current?.ToString(3)} ist aktuell.") : null)
                : UpdateCheck.Describe(info);
            if (text is null) return;
            if (!manual) { _session.Say(text + Lang.T(" Datei › Updates…")); return; }
            var b = new AlertDialog.Builder(this).SetTitle(Lang.T("Updates"))!.SetMessage(text)!;
            if (info?.Url is { } link && UpdateCheck.IsNewer(info, current))
                b.SetPositiveButton(Lang.T("Download-Seite öffnen"), (_, _) => StartActivity(new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(link))))!.SetNegativeButton(Lang.T("Später"), (_, _) => { });
            else b.SetPositiveButton("OK", (_, _) => { });
            b.SetNeutralButton(Lang.T("Beim Start…"), (_, _) => new AlertDialog.Builder(this).SetTitle(Lang.T("Updates"))!.SetMessage(UpdateCheck.AutoQuestion)!
                .SetPositiveButton(Lang.T("Ja"), (_, _) => UpdateCheck.AutoEnabled = true)!.SetNegativeButton(Lang.T("Nein"), (_, _) => UpdateCheck.AutoEnabled = false)!.Show());
            b.Show();
        });
    }

    private void ShowAbout()
    {
        var v = typeof(MainActivity).Assembly.GetName().Version;
        Dialogs.ShowAbout(this, _license, $"{v?.Major}.{v?.Minor}.{v?.Build}");
    }

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
            return n.Length == 0 ? Lang.T("Plan") : n;
        }
    }

    // ---------- Zwischenablage ----------

    private void CopyToClipboard()
    {
        if (_session.CopySelection() is not { } json) return;
        if (GetSystemService(ClipboardService) is global::Android.Content.ClipboardManager cm)
            cm.PrimaryClip = global::Android.Content.ClipData.NewPlainText("TrackMaster", json);
    }

    private void PasteFromClipboard()
    {
        var cm = GetSystemService(ClipboardService) as global::Android.Content.ClipboardManager;
        var text = cm?.PrimaryClip is { ItemCount: > 0 } clip ? clip.GetItemAt(0)?.CoerceToText(this) : null;
        _session.Paste(text, _canvas.CenterWorld());
    }

    // ---------- Oberfläche ----------

    private static readonly (string Tab, (string Label, string Command, bool Toggle)[] Items)[] Ribbon =
    [
        (Lang.T("Start"), [(Lang.T("Spurweite…"), "gauge", false), (Lang.T("Eigenschaften"), "props", false), (Lang.T("Gleis wiederholen"), "repeat", false), ("↺ 15°", "rotL", false), ("↻ 15°", "rotR", false), (Lang.T("Frei drehen…"), "rotFree", false), (Lang.T("Spiegeln"), "flip", false), (Lang.T("Anschluss wechseln"), "swap", false),
                   (Lang.T("Duplizieren"), "duplicate", false), (Lang.T("Kopieren"), "copy", false), (Lang.T("Einfügen"), "paste", false), (Lang.T("Löschen"), "delete", false), (Lang.T("Alles wählen"), "selectAll", false), (Lang.T("Auswahl aufheben"), "selectNone", false),
                   (Lang.T("Nächstes Gleisende"), "nextEnd", false), (Lang.T("Sperren"), "lock", false), (Lang.T("Gruppieren"), "group", false), (Lang.T("Gruppe auflösen"), "ungroup", false),
                   (Lang.T("Links ausrichten"), "alignL", false), (Lang.T("Rechts ausrichten"), "alignR", false), (Lang.T("Oben ausrichten"), "alignT", false), (Lang.T("Unten ausrichten"), "alignB", false),
                   (Lang.T("Mittig waagrecht"), "alignCX", false), (Lang.T("Mittig senkrecht"), "alignCY", false),
                   ("Text", "toolLabel", true), (Lang.T("Maß"), "toolDimension", true), (Lang.T("Stückliste"), "bom", false), (Lang.T("Planprüfung"), "check", false), (Lang.T("Markierungen entfernen"), "clearIssues", false)]),
        (Lang.T("Gleise"), [(Lang.T("Gerade"), "straight", false), (Lang.T("Bogen"), "curve", false), (Lang.T("Weiche links"), "switchL", false), (Lang.T("Weiche rechts"), "switchR", false),
                    (Lang.T("Kreuzung"), "crossing", false), (Lang.T("Prellbock"), "buffer", false), (Lang.T("Flexgleis…"), "flex", false),
                    (Lang.T("Flexgleis ziehen"), "toolFlex", true), (Lang.T("Lücke schließen"), "flexConnect", false), (Lang.T("Parallelgleis…"), "parallel", false), (Lang.T("Ebenen…"), "levels", false),
                    (Lang.T("Steigung…"), "gradient", false), (Lang.T("Gleiswendel…"), "helix", false), (Lang.T("Höhenprofil"), "profile", false), (Lang.T("Spantenbau…"), "frame", false), (Lang.T("Trasse…"), "rampCut", false), ("Tunnel", "tunnel", false)]),
        (Lang.T("Gelände"), [(Lang.T("Anlagenplatte…"), "board", false), (Lang.T("Module…"), "modules", false), (Lang.T("Platte ziehen"), "toolBoard", true), (Lang.T("Anlage 90° drehen"), "boardRotate", false), (Lang.T("Unterbau…"), "benchwork", false), (Lang.T("Leiste ziehen"), "toolBeam", true), (Lang.T("Platte auflegen"), "addPlate", false), (Lang.T("Gelände formen"), "toolTerrain", true), (Lang.T("Gewässer"), "toolWater", true), (Lang.T("Gebäude"), "toolBuilding", true),
                     (Lang.T("Vegetation"), "toolTree", true), (Lang.T("Gelände zeigen"), "scenery", true)]),
        (Lang.T("Ausstattung"), [.. ModelCommands.Select(m => (m.Category + "…", m.Command, true)), (Lang.T("Einfaches Gebäude"), "toolBuilding", true)]),
        (Lang.T("Raum"), [(Lang.T("Raum…"), "roomRect", false), (Lang.T("Raum zeichnen"), "toolRoom", true), (Lang.T("Tür"), "toolDoor", true), (Lang.T("Fenster"), "toolWindow", true),
                  (Lang.T("Säule"), "toolColumn", true), (Lang.T("Heizkörper"), "toolHeater", true), (Lang.T("Möbel"), "toolFurniture", true),
                  (Lang.T("Bild laden…"), "bgLoad", false), (Lang.T("Lage und Deckkraft…"), "bgEdit", false), (Lang.T("Nach Maß skalieren…"), "bgScale", false), (Lang.T("Bild zeigen"), "bgToggle", true)]),
        (Lang.T("Elektrik"), [(Lang.T("Betrieb / Stromart…"), "current", false), (Lang.T("Trafo / Zentrale…"), "toolPower", true), (Lang.T("Trennstelle"), "toolIsolation", true), (Lang.T("Signal setzen…"), "toolSignal", true), (Lang.T("Rückmelder…"), "toolDetector", true), (Lang.T("Verteiler…"), "toolDistributor", true),
                      (Lang.T("Kabel verlegen…"), "toolWire", true),
                      (Lang.T("Adressen…"), "addresses", false), (Lang.T("Stromkreise zeigen"), "sections", true), (Lang.T("Nur Elektrik beschriften"), "elecLabels", true), (Lang.T("Elektrik prüfen"), "elecCheck", false), (Lang.T("Markierungen entfernen"), "clearIssues", false), (Lang.T("Kabel zeigen"), "wires", true), (Lang.T("Melder verkabeln"), "detectorWires", true), (Lang.T("Elektrik nummerieren"), "numberElectrics", false), (Lang.T("Kabelplan"), "wiring", false), (Lang.T("Fahrstraßen"), "routes", false), (Lang.T("Export Steuerung"), "controlExport", false)]),
        (Lang.T("Ansicht"), [(Lang.T("Einpassen"), "fit", false), (Lang.T("Raster"), "grid", true), (Lang.T("Gleisenden"), "ends", true), (Lang.T("Beschriftung"), "labels", true),
                     (Lang.T("Höhen"), "heights", true), (Lang.T("Einrasten"), "snap", true), (Lang.T("Ebenen…"), "levels", false), (Lang.T("Ansicht ↺ 15°"), "viewRotL", false), (Lang.T("Ansicht ↻ 15°"), "viewRotR", false),
                     (Lang.T("Gerade ausrichten"), "viewReset", false), (Lang.T("Ansicht drehen…"), "viewAngle", false), (Lang.T("Design und Schrift…"), "appearance", false)]),
        ("3D", [(Lang.T("3D-Ansicht"), "view3d", false), (Lang.T("Mitfahren"), "ride", false), (Lang.T("Abendstimmung"), "daytime", false)]),
        (Lang.T("Datei"), [(Lang.T("Neu…"), "new", false), (Lang.T("Öffnen…"), "open", false), (Lang.T("Speichern…"), "save", false), (Lang.T("Beispielanlage"), "sample", false),
                   (Lang.T("Stückliste als CSV"), "csv", false), (Lang.T("Plan als SVG"), "svg", false), (Lang.T("Plan als PNG"), "png", false), (Lang.T("Plan als DXF"), "dxf", false), (Lang.T("Drucken (PDF, auch 1:1)…"), "pdf", false), (Lang.T("Lizenz…"), "license", false), (Lang.T("Darstellung…"), "appearance", false), ("Sprache / Language", "language", false), (Lang.T("Bedienung"), "help", false), (Lang.T("Updates…"), "update", false), (Lang.T("Über…"), "about", false)]),
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
        var logo = new ImageView(this);
        logo.SetImageResource(Resources!.GetIdentifier("logo", "drawable", PackageName));
        logo.SetAdjustViewBounds(true);
        logo.Click += (_, _) => Run("about");
        header.AddView(logo, new LinearLayout.LayoutParams(Ui.Dp(40), Ui.Dp(40)));
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
        _history = Ui.Button(this, "⏱", () => Run("history"));
        _history.SetTextSize(Android.Util.ComplexUnitType.Sp, 16);
        header.AddView(_history);
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
        _canvas.PowerPlaced += () => SetTool(CanvasTool.Select);
        _canvas.LabelRequested += (x, y) => Dialogs.AskText(this, Lang.T("Beschriftung"), "Text", text => _session.AddLabel(text, x, y));
        stage.AddView(_canvas, new FrameLayout.LayoutParams(-1, -1));

        _toolBar = Ui.Row(this, 8);
        _toolBar.Background = Ui.Rounded(Color.Argb(0xF2, 0xFF, 0xFF, 0xFF), Ui.Border, 8);
        _toolBar.SetPadding(Ui.Dp(10), Ui.Dp(6), Ui.Dp(6), Ui.Dp(6));
        _toolText = Ui.Label(this, "", 13);
        _toolBar.AddView(_toolText, Ui.Weight(1, vertical: false));
        _toolBack = Ui.Button(this, Lang.T("Punkt zurück"), () => _canvas.UndoPolygonPoint());
        _toolFinish = Ui.Button(this, Lang.T("Fertig"), () => _canvas.FinishPolygon(), accent: true);
        _toolBar.AddView(_toolBack);
        _toolBar.AddView(_toolFinish);
        _toolBar.AddView(Ui.Button(this, Lang.T("Beenden"), () => SetTool(CanvasTool.Select)));
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

        // Ebenenleiste: jede Ebene mit Haken zum Ein- und Ausblenden, Antippen macht sie aktiv
        _levelBar = Ui.Row(this, 6);
        _levelBar.SetPadding(Ui.Dp(8), Ui.Dp(4), Ui.Dp(8), Ui.Dp(4));
        var levelScroll = new HorizontalScrollView(this) { HorizontalScrollBarEnabled = false };
        levelScroll.SetBackgroundColor(Color.White);
        levelScroll.AddView(_levelBar);
        root.AddView(new View(this) { Background = new Android.Graphics.Drawables.ColorDrawable(Ui.Border) }, new LinearLayout.LayoutParams(-1, Math.Max(1, Ui.Dp(1))));
        root.AddView(levelScroll, Ui.Fill());

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
        var items = (Ribbon.FirstOrDefault(r => r.Tab == _tab).Items ?? Ribbon[0].Items);
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
        "elecLabels" => _session.ElectricLabelsOnly,
        "toolWire" => _session.Tool == CanvasTool.Wire,
        "toolFlex" => _session.Tool == CanvasTool.Flex,
        "toolTerrain" => _session.Tool == CanvasTool.Terrain,
        "toolBoard" => _session.Tool == CanvasTool.Board,
        "toolWater" => _session.Tool == CanvasTool.Water,
        "toolBuilding" => _session.Tool == CanvasTool.Building,
        "toolTree" => _session.Tool == CanvasTool.Tree,
        "toolLabel" => _session.Tool == CanvasTool.Label,
        "toolDimension" => _session.Tool == CanvasTool.Dimension,
        "toolBeam" => _session.Tool == CanvasTool.Beam,
        "wires" => _session.ShowWires,
        "detectorWires" => _session.Plan.DetectorWiring,
        "toolPower" => _session.Tool == CanvasTool.Power && _session.PowerType != PowerSourceType.Verteiler && _session.ModuleModel is null,
        "toolDistributor" => _session.Tool == CanvasTool.Power && _session.PowerType == PowerSourceType.Verteiler,
        "toolIsolation" => _session.Tool == CanvasTool.Isolation,
        "toolSignal" => _session.Tool == CanvasTool.Signal,
        "toolRoom" => _session.Tool == CanvasTool.Room,
        "toolDetector" => _session.Tool == CanvasTool.Detector,
        "toolDoor" => _session.Tool == CanvasTool.Obstacle && _session.ObstacleType == ObstacleType.Tuer,
        "toolWindow" => _session.Tool == CanvasTool.Obstacle && _session.ObstacleType == ObstacleType.Fenster,
        "toolColumn" => _session.Tool == CanvasTool.Obstacle && _session.ObstacleType == ObstacleType.Saeule,
        "toolHeater" => _session.Tool == CanvasTool.Obstacle && _session.ObstacleType == ObstacleType.Heizkoerper,
        "toolFurniture" => _session.Tool == CanvasTool.Obstacle && _session.ObstacleType == ObstacleType.Moebel,
        "bgToggle" => _session.Plan.Background is { Visible: true },
        _ when ModelCommands.FirstOrDefault(m => m.Command == id) is { Command: not null } mc => ModelToolIn(mc.Category),
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
        _history.Enabled = _session.History.CanUndo || _session.History.CanRedo;
        Ui.SetButtonState(_history, false, false);
    }

    private void Refresh()
    {
        var plan = _session.Plan;
        _title.Text = plan.Name + (_session.Dirty ? " •" : "");
        _edition.Text = _license.EditionName == Lang.T("Pro (Entwicklerversion)") ? Lang.T("Pro (Entwickler)") : _license.EditionName;
        _edition.Background = Ui.Rounded(_license.IsPro ? Ui.Pro : Ui.TextSecondary, radius: 10);
        var stats = plan.Statistics(_session.Connections);
        var sel = _session.Selection.Count;
        _status.Text = Lang.F($"Spur {plan.GaugeId} · {stats.Pieces} Gleise · {Fmt.Num(stats.TrackLengthMm / 1000, 2)} m · offene Enden {stats.OpenEnds}") + (sel > 0 ? Lang.F($" · {sel} gewählt") : "");
        RefreshActions();
        UpdateToolBar();
        UpdateLevelBar();
    }

    private void UpdateLevelBar()
    {
        var plan = _session.Plan;
        _levelBar.RemoveAllViews();
        var title = Ui.Label(this, Lang.T("Ebenen"), 12, true, Ui.TextSecondary);
        title.Gravity = GravityFlags.CenterVertical;
        _levelBar.AddView(title);
        for (var i = 0; i < plan.Levels.Count; i++)
        {
            var index = i;
            var l = plan.Levels[i];
            var active = i == plan.ActiveLevel;
            var chip = Ui.Row(this, 2);
            chip.Background = Ui.Rounded(active ? Ui.AccentLight : Color.White, active ? Ui.AccentBorder : Ui.Border, 14);
            chip.SetPadding(Ui.Dp(2), 0, Ui.Dp(10), 0);
            var check = new CheckBox(this) { Checked = l.Visible };
            check.Click += (_, _) => _session.SetLevelVisible(index, check.Checked);
            chip.AddView(check);
            var name = Ui.Label(this, $"{l.Name}  {Fmt.Num(l.Height, 0)}", 13, active);
            name.Alpha = l.Visible ? 1f : 0.55f;
            name.Click += (_, _) => _session.SetActiveLevel(index);
            chip.AddView(name);
            chip.Click += (_, _) => _session.SetActiveLevel(index);
            _levelBar.AddView(chip);
        }
        var add = Ui.Button(this, "+", _session.AddLevel);
        add.Enabled = plan.Levels.Count < LayoutPlan.MaxLevels;
        _levelBar.AddView(add);
        _levelBar.AddView(Ui.Button(this, "…", () => Run("levels")));
        var anyVisible = plan.Levels.Any(l => l.Visible);
        _levelBar.AddView(Ui.Button(this, anyVisible ? Lang.T("Alle ausblenden") : Lang.T("Alle einblenden"), () => _session.SetAllLevelsVisible(!anyVisible)));
    }

    private void UpdateToolBar()
    {
        var tool = _session.Tool;
        _toolBar.Visibility = tool == CanvasTool.Select ? ViewStates.Gone : ViewStates.Visible;
        var polygon = tool is CanvasTool.Terrain or CanvasTool.Water or CanvasTool.Room or CanvasTool.Wire;
        _toolFinish.Visibility = _toolBack.Visibility = polygon ? ViewStates.Visible : ViewStates.Gone;
        _toolFinish.Enabled = _canvas.PolygonPoints >= (tool == CanvasTool.Wire ? 2 : 3);
        _toolBack.Enabled = _canvas.PolygonPoints > 0;
        Ui.SetButtonState(_toolFinish, _toolFinish.Enabled, false);
        Ui.SetButtonState(_toolBack, false, false);
        _toolText.Text = tool switch
        {
            CanvasTool.Board => Lang.T("Platte ziehen: Ecke eines Moduls ziehen ändert seine Größe, ein Modul ziehen verschiebt es samt Gleisen. Kanten rasten an Nachbarmodulen ein. Klick auf ein Modul: Maße eintippen oder drehen."),
            CanvasTool.Terrain => Lang.F($"Gelände formen: Eckpunkte antippen ({_canvas.PolygonPoints}), dann „Fertig“."),
            CanvasTool.Water => Lang.F($"Gewässer: Eckpunkte antippen ({_canvas.PolygonPoints}), dann „Fertig“."),
            CanvasTool.Flex => Lang.T("Flexgleis ziehen: am offenen Gleisende aufsetzen und ziehen, es rastet an Gleisenden ein."),
            CanvasTool.Building => Lang.T("Gebäude: antippen oder Rechteck aufziehen."),
            CanvasTool.Model => _session.CurrentModel is { } model
                ? Lang.F($"{model.Name}: in den Plan tippen.") + (model.Snap == ModelSnap.Frei ? "" : " " + Lang.T("Rastet am nächsten Gleis ein.")) + (ModelPlacement.FitsSelection(model) ? " " + Lang.T("Tipp: Erst Gleise wählen, dann das Modell – es wird entlang der Auswahl gesetzt.") : "")
                : Lang.T("Bitte zuerst ein Modell wählen."),
            CanvasTool.Tree => Lang.T("Vegetation: jeder Tipp setzt einen Baum."),
            CanvasTool.Feeder => Lang.T("Gleisanschluss: auf ein Gleis tippen. Er hängt an der Zentrale bzw. dem Trafo (gestrichelte Linie); fehlt sie, wird sie angelegt."),
            CanvasTool.Power => Decoders.Find(_session.ModuleModel) is { } mm ? Lang.F($"{mm.Maker} {mm.Id}: an die Stelle im Plan tippen, wo der Baustein liegen soll (auch neben der Platte).")
                : Lang.F($"{PowerSupply.TypeName(_session.PowerType)}: an die Stelle im Plan tippen, wo sie stehen soll (auch neben der Platte)."),
            CanvasTool.Isolation => Lang.T("Trennstelle: auf eine Gleisverbindung (grüner Punkt) tippen."),
            CanvasTool.Signal => Lang.F($"{Signal.TypeName(_session.SignalType)}: auf einen Punkt an einer Gleisverbindung tippen."),
            CanvasTool.Room => Lang.F($"Raum zeichnen: Ecken der Wand-Innenkante antippen ({_canvas.PolygonPoints}), dann „Fertig“."),
            CanvasTool.Wire => Lang.F($"Kabel ({WireLayout.UseName(_session.WireUse)}): Punkte setzen ({_canvas.PolygonPoints}) – an Gleisanschlüssen, Zentrale/Trafo (B und 0), Verteilern, Rückmeldern, Signalen und Weichenantrieben rastet es ein. Doppelklick oder „Fertig“ beendet."),
            CanvasTool.Obstacle => Lang.F($"{SceneryItem.ObstacleName(_session.ObstacleType)}: antippen oder Rechteck aufziehen."),
            CanvasTool.Detector => Lang.F($"{Detector.TypeName(_session.DetectorType)}: auf ein Gleis tippen (Tippen auf einen Rückmelder entfernt ihn)."),
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
        Dialogs.ShowIssues(this, Lang.T("Planprüfung"), issues, Pick, clear: ClearIssues);
    }

    private void ShowElectricCheck()
    {
        var report = Electrics.Analyze(_session.Plan);
        _session.Issues = report.Issues;
        if (!_session.ShowSections) { _session.ShowSections = true; _session.Commit(); }
        _canvas.Invalidate();
        Dialogs.ShowIssues(this, Lang.T("Elektrik prüfen"), report.Issues, Pick,
            Lang.F($"{Electrics.CurrentName(report.Current)}: {report.Sections.Count} Stromabschnitte, {report.ReverseLoops.Count} Kehrschleifen. {(report.ThreeRail ? Lang.T("Mittelleiter: Kehrschleifen ohne Sonderschaltung möglich.") : Lang.T("Zweileiter-System."))}"), ClearIssues);
    }

    /// <summary>Markierungen der letzten Prüfung (Kreise und hervorgehobene Gleise) aus dem Plan entfernen.</summary>
    private void ClearIssues()
    {
        _session.Issues = [];
        _session.ClearFocus();
        _canvas.Invalidate();
        RefreshActions();
    }

    private void Pick(PlanIssue issue)
    {
        var (x0, y0, x1, y1) = _session.FocusIssue(issue);
        _canvas.FitArea(x0, y0, x1, y1);
    }

    /// <summary>Kabel an der gewählten Zentrale, dem Booster oder Trafo beginnen; endet es auf einem Gleis, entsteht dort die Einspeisung bzw. der Masseanschluss.</summary>
    private void StartWireFromSource(bool ground)
    {
        if (_session.SelectedObject is not { } id || _session.WireStart(id, ground) is not { } start)
        {
            _session.Say(Lang.T("Erst eine Zentrale, einen Booster oder Trafo wählen."));
            return;
        }
        _session.WireUse = ground ? WireUse.Rueckleiter : WireUse.Fahrstrom;
        if (_session.Tool != CanvasTool.Wire) SetTool(CanvasTool.Wire);
        _canvas.StartWireAt(start.X, start.Y);
        _session.Say(ground
            ? Lang.T("Masse-Kabel: Punkte antippen und auf einer Schiene enden – dort entsteht der Masseanschluss. „Fertig“ beendet.")
            : Lang.T("Bahnstrom-Kabel: Punkte antippen und auf dem Gleis enden – dort entsteht die Einspeisung. „Fertig“ beendet."));
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
        if (curves.Count == 0) { ShowMessage(Lang.T("Für diese Spur gibt es keine Bogengleise im Katalog. Die Gleiswendel lässt sich hier mit Flexgleis bauen.")); return; }
        var straights = _session.CurrentSystem.Pieces.Where(p => p.Kind == PieceKind.Straight && p.Length >= gauge.TrackWidth * 3).ToList();
        if (straights.Count == 0) straights = gauge.Systems.SelectMany(x => x.Pieces).Where(p => p.Kind == PieceKind.Straight && p.Length >= gauge.TrackWidth * 3).ToList();
        Dialogs.AskHelix(this, gauge, curves, straights, _session.ActiveConnector is not null, spec =>
        {
            if (_session.BuildHelix(spec, _canvas.CenterWorld()) is not { } material) return;
            _canvas.Fit();
            // Die Holzliste gehört wie die Gleis-Stückliste zur Pro-Version
            if (_session.RequirePro(Lang.T("Holzliste der Gleiswendel")))
                Dialogs.ShowHelixMaterial(this, material, () => Export(material.ToCsv(), "text/csv", Lang.F($"{SafeName} – Holzliste Gleiswendel.csv")),
                    cut => ExportSvgFiles(cut.ToSvgFiles(SafeName), Lang.F($"{SafeName} – Zuschnitt Gleiswendel")));
        }, _session.Plan, _session.ActiveConnector?.Z);
    }

    private void AskBenchwork() => Dialogs.AskBenchwork(this, _session, ShowBenchwork);

    private void ShowBenchwork()
    {
        if (_session.Plan.Benchwork is not { } spec) return;
        var b = Benchwork.Build(_session.Plan, spec);
        Dialogs.ShowBenchwork(this, b, AskBenchwork,
            () =>
            {
                if (GetSystemService(ClipboardService) is global::Android.Content.ClipboardManager cm)
                    cm.PrimaryClip = global::Android.Content.ClipData.NewPlainText("TrackMaster", b.ToText());
            },
            () => Export(b.ToCsv(), "text/csv", Lang.F($"{SafeName} – Unterbau.csv")));
    }

    private void AskFrame() => Dialogs.AskFrame(this, _session.Plan.Gauge, spec =>
    {
        var f = FrameBuild.Build(_session.Plan, spec);
        Dialogs.ShowFrame(this, f,
            () => ExportSvgFiles(f.ToSvgFiles(SafeName), Lang.F($"{SafeName} – Spanten")),
            () => Export(f.ToCsv(), "text/csv", Lang.F($"{SafeName} – Spanten.csv")));
    });

    private void AskRampCut()
    {
        var ids = _session.Selection.ToHashSet();
        Dialogs.AskRampCut(this, RampTrasse.Defaults(_session.Plan.Gauge), ids.Count, spec => RampTrasse.Build(_session.Plan, ids, spec),
            cut => Dialogs.ShowRampCut(this, cut, () => ExportSvgFiles(cut.ToSvgFiles(SafeName), Lang.F($"{SafeName} – Trasse"))));
    }

    /// <summary>Speichert Zuschnitte als ZIP mit je einer SVG-Datei pro Teil.</summary>
    private void ExportSvgFiles(IReadOnlyList<SvgFile> files, string name) => Export(SvgFiles.Zip(files), "application/zip", SvgFiles.FileName(name) + ".zip");

    private void ShowWiring()
    {
        var text = Electrics.WiringPlan(_session.Plan);
        Dialogs.ShowText(this, Lang.T("Kabelplan"), text, Lang.T("Speichern…"), () =>
        {
            if (!_session.RequirePro(Lang.T("Kabelplan"))) return;
            Dialogs.Choose(this, Lang.T("Kabelplan speichern"),
                [(Lang.T("Als Text (Kabelplan und Anschlussliste)"), null, false), (Lang.T("Anschlussliste als CSV"), null, false)],
                i =>
                {
                    if (i == 0) Export(text, "text/plain", Lang.F($"{SafeName} – Kabelplan.txt"));
                    else Export(ConnectionList.ToCsv(_session.Plan), "text/csv", Lang.F($"{SafeName} – Anschlussliste.csv"));
                });
        });
    }

    private void ShowControlExport()
    {
        var text = TrainRoutes.ControlText(_session.Plan);
        Dialogs.ShowText(this, Lang.T("Export für Steuerprogramme"), text, Lang.T("Speichern…"), () =>
            Dialogs.Choose(this, Lang.T("Export für Steuerprogramme"),
                [(Lang.T("Als TXT speichern"), null, false), (Lang.T("Als CSV speichern"), null, false), (Lang.T("Als JSON speichern"), null, false)],
                i =>
                {
                    if (i == 0) Export(text, "text/plain", Lang.F($"{SafeName} – Steuerung.txt"));
                    else if (i == 1) Export(TrainRoutes.ControlCsv(_session.Plan), "text/csv", Lang.F($"{SafeName} – Steuerung.csv"));
                    else Export(TrainRoutes.ToJson(_session.Plan), "application/json", Lang.F($"{SafeName} – Steuerung.json"));
                }));
    }

    private void ShowRoutes()
    {
        var routes = TrainRoutes.Find(_session.Plan);
        Dialogs.ShowText(this, Lang.T("Fahrstraßen"), TrainRoutes.ToText(_session.Plan, routes), Lang.T("Als CSV speichern"), () =>
        {
            if (_session.RequirePro(Lang.T("Fahrstraßen speichern"))) Export(TrainRoutes.ToCsv(routes), "text/csv", Lang.F($"{SafeName} – Fahrstraßen.csv"));
        });
    }

    private void Open3D(View3DMode mode)
    {
        View3DActivity.Plan = _session.View;
        var intent = new Intent(this, typeof(View3DActivity));
        intent.PutExtra("mode", (int)mode);
        intent.PutExtra("start", _session.SelectedPieces.FirstOrDefault()?.Id ?? -1);
        StartActivity(intent);
    }

    // ---------- Lizenz ----------

    private void ShowLicense() => Dialogs.ShowLicense(this, _license, () => Pick(RequestLicense),
        () => _ = SyncActivation(interactive: true), () => _ = ReleaseActivation());

    private static string AppVersion => typeof(MainActivity).Assembly.GetName().Version?.ToString(3) ?? "";

    /// <summary>Beim Aktivierungsserver anmelden bzw. nachfragen. Meldet sich nur, wenn gefragt wurde oder Pro wegfällt.</summary>
    private async Task SyncActivation(bool interactive)
    {
        var wasPro = _license.IsPro;
        string? message;
        try { message = await _license.SyncActivationAsync(AppVersion); }
        catch (Exception e) { message = e.Message; }
        RunOnUiThread(() =>
        {
            if (IsFinishing) return;
            BuildActions();
            Refresh();
            _canvas.Invalidate();
            if (message is not null && (interactive || (wasPro && !_license.IsPro))) Dialogs.Message(this, Lang.T("Lizenz"), message);
        });
    }

    private async Task ReleaseActivation()
    {
        string message;
        try { message = await _license.ReleaseActivationAsync(AppVersion); }
        catch (Exception e) { message = e.Message; }
        RunOnUiThread(() =>
        {
            if (IsFinishing) return;
            BuildActions();
            Refresh();
            _canvas.Invalidate();
            Dialogs.Message(this, Lang.T("Lizenz"), message);
        });
    }

    private void InstallLicense(string text)
    {
        var result = _license.Install(text);
        if (result.IsValid)
        {
            Dialogs.Message(this, Lang.T("Lizenz"), Lang.F($"Pro-Lizenz eingespielt: {_license.Summary}"));
            if (_license.ActivationDue) _ = SyncActivation(interactive: true);
            BuildActions();
            Refresh();
            _canvas.Invalidate();
        }
        else
            Dialogs.Message(this, Lang.T("Lizenz"), Lang.F($"Die Lizenzdatei wurde nicht übernommen: {result.StatusText}."));
    }

    // ---------- Dateien ----------

    private void Pick(int request, string type = "*/*")
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType(type);
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

    private void Export(string content, string mime, string fileName) => Export(new System.Text.UTF8Encoding(false).GetBytes(content), mime, fileName);

    private void Export(byte[] data, string mime, string fileName)
    {
        _pendingExport = (data, mime);
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
                case RequestModule:
                    AppendModuleUri(uri);
                    break;
                case RequestSave:
                    WriteText(uri, PlanSerializer.ToJson(_session.Plan));
                    _session.MarkSaved(DisplayName(uri) ?? $"{SafeName}.trackmaster.json");
                    break;
                case RequestLicense:
                    InstallLicense(ReadText(uri));
                    break;
                case RequestBackground:
                    LoadBackground(ReadBytes(uri));
                    break;
                case RequestPrices:
                    if (_session.ImportPrices(ReadText(uri)) > 0) Run("bom");
                    break;
                case RequestExport when _pendingExport is { } export:
                    WriteBytes(uri, export.Data);
                    ShowMessage(Lang.F($"Gespeichert: {DisplayName(uri)}"));
                    _pendingExport = null;
                    break;
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or Java.Lang.Exception)
        {
            Dialogs.Message(this, Lang.T("Datei"), Lang.F($"Die Datei konnte nicht verarbeitet werden: {e.Message}"));
        }
    }

    /// <summary>Gespeicherten Plan als Modul rechts ansetzen.</summary>
    private void AppendModuleUri(Android.Net.Uri uri)
    {
        try
        {
            var other = PlanSerializer.FromJson(ReadText(uri), out _);
            var file = (DisplayName(uri) ?? Lang.T("Modul")).Replace(PlanSerializer.FileExtension, "").Replace(".json", "");
            var title = other.Name is { Length: > 0 } n && n != Lang.T("Neuer Plan") && n != Lang.T("Gleisplan") ? n : file;
            if (_session.AppendModule(other, title)) _canvas.Fit();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or Java.Lang.Exception)
        {
            Dialogs.Message(this, Lang.T("Plan als Modul ansetzen"), Lang.F($"Die Datei ist kein TrackMaster-Plan: {e.Message}"));
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
            Dialogs.Message(this, Lang.T("Öffnen"), Lang.F($"Die Datei ist kein TrackMaster-Plan: {e.Message}"));
        }
    }

    private string ReadText(Android.Net.Uri uri)
    {
        using var stream = ContentResolver!.OpenInputStream(uri) ?? throw new IOException(Lang.T("Datei nicht lesbar."));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private byte[] ReadBytes(Android.Net.Uri uri)
    {
        using var stream = ContentResolver!.OpenInputStream(uri) ?? throw new IOException(Lang.T("Datei nicht lesbar."));
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private void WriteBytes(Android.Net.Uri uri, byte[] data)
    {
        using var stream = ContentResolver!.OpenOutputStream(uri, "wt") ?? throw new IOException(Lang.T("Datei nicht beschreibbar."));
        stream.Write(data);
    }

    private void WriteText(Android.Net.Uri uri, string text)
    {
        using var stream = ContentResolver!.OpenOutputStream(uri, "wt") ?? throw new IOException(Lang.T("Datei nicht beschreibbar."));
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
