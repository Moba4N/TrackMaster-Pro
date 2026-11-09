using System.Runtime.Versioning;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using TrackMaster.Core;
using TrackMaster.Licensing;
using TrackMaster.Web.Interop;
using TrackMaster.Web.Plan;
using TrackMaster.Web.Shell;
using Signal = TrackMaster.Core.Signal;

namespace TrackMaster.Web;

/// <summary>
/// Hauptseite der Webversion: Titelzeile, Menüband wie unter Windows, Gleisbibliothek, Zeichenfläche, Eigenschaften und Statuszeile.
/// Der Plan wird laufend im Browser gesichert und beim nächsten Besuch wiederhergestellt.
/// </summary>
[SupportedOSPlatform("browser")]
public partial class App : ComponentBase, IAppHost
{
    private readonly PlanSession _session = new();
    private PlanView _view = null!;
    private WebLicense _license = null!;
    private AppearanceSettings _appearance = new();
    private LibraryPanel? _library;
    private readonly Dictionary<string, Command> _commands = [];
    private Tab[] _ribbon = [];
    private string _tab = "";
    private int _version, _appearanceVersion;
    private bool _renderPending, _attached;
    private string? _toast;
    private int _toastId;
    private string? _clipboard;
    private string? _fatal;

    // Schmale Fenster: Bibliothek und Eigenschaften als Schublade
    private bool _libraryOpen, _propsOpen;

    public PlanView View => _view;

    private sealed record Command(string Name, Action Execute, bool Pro, Func<bool>? CanExecute);

    /// <summary>Schaltfläche im Menüband. <paramref name="On"/> zeigt einen Schalter oder ein Werkzeug als aktiv.</summary>
    private sealed record Item(string Label, string Icon, string Command, bool Large = false, string? Tip = null, Func<bool>? On = null);
    private sealed record Group(string Name, Item[] Items);
    private sealed record Tab(string Name, Group[] Groups);

    /// <summary>Startseite der Webseite, in die der Planer eingebunden ist (aus index.html); leer = kein Link.</summary>
    private string _homeUrl = "";

    protected override void OnInitialized()
    {
        try { _homeUrl = Js.HomeUrl(); } catch { _homeUrl = ""; }
        _appearance = Settings.LoadAppearance();
        UseAppearance(_appearance);
        _license = CreateLicense(WebLicense.DeviceId(Settings.DeviceSeed()));
        _session.IsPro = () => _license.IsPro;
        _session.ProRequired += feature => ShowProRequired(feature);
        _session.Message += ShowToast;
        _session.BoardPartClicked += (module, leg) => { AskBoardPart(module, leg); StateHasChanged(); };
        RestoreAutosave();
        _view = new PlanView(_session);
        _view.SpaceTapped += () => { if (_session.Tool is CanvasTool.Select or CanvasTool.Pan) { Run("repeat"); Refresh(); } };
        _view.PropertiesRequested += () => Run("props");
        _view.ContextRequested += (x, y) => { _ctx = (x, y); Refresh(); };
        _view.JointHeightRequested += c => { AskJointHeight(c); Refresh(); };
        _view.PolygonChanged += Refresh;
        _view.PowerPlaced += () => SetTool(CanvasTool.Select);
        _view.LabelRequested += (x, y) => AskText(Lang.T("Beschriftung"), "Text", "", text => _session.AddLabel(text, x, y));
        _view.ViewChanged += UpdateZoomText;
        _session.Changed += OnSessionChanged;
        RegisterCommands();
        _ribbon = BuildRibbon();
        _tab = _ribbon[0].Name;
        JsEntry.Host = this;
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (!_attached)
        {
            _attached = true;
            Js.AttachPlan(PlanView.ElementId);
            Js.Focus(PlanView.ElementId);
            if (_license.ActivationDue) _ = SyncActivation(interactive: false);
        }
        if (_open3D && !_glReady) Init3D();
    }

    // ---------- Sicherung im Browser ----------

    private const string AutosaveInfoKey = Settings.AutosaveKey + ".info";
    private CancellationTokenSource? _autosave;
    private bool _storageWarned;

    private void RestoreAutosave()
    {
        try
        {
            if (Js.StorageGet(Settings.AutosaveKey) is not { Length: > 0 } json) return;
            var info = (Js.StorageGet(AutosaveInfoKey) ?? "").Split('|', 2);
            var dirty = info.Length > 0 && info[0] == "1";
            var name = info.Length > 1 && info[1].Length > 0 ? info[1] : null;
            _session.Restore(json, name, dirty);
        }
        catch (Exception)
        {
            // Defekte Sicherung: mit der Beispielanlage weitermachen
        }
    }

    private void ScheduleAutosave()
    {
        _autosave?.Cancel();
        var cts = _autosave = new CancellationTokenSource();
        _ = Task.Delay(1500, cts.Token).ContinueWith(t => { if (!t.IsCanceled) SaveNow(); }, TaskScheduler.Default);
    }

    public void SaveNow()
    {
        _autosave?.Cancel();
        try
        {
            var ok = Js.StorageSet(Settings.AutosaveKey, PlanSerializer.ToJson(_session.Plan));
            Js.StorageSet(AutosaveInfoKey, $"{(_session.Dirty ? "1" : "0")}|{_session.FilePath}");
            if (!ok && !_storageWarned)
            {
                _storageWarned = true;
                ShowToast(Lang.T("Der Plan konnte nicht im Browser gesichert werden (Speicher voll oder gesperrt). Bitte über Datei › Speichern sichern."));
            }
        }
        catch (Exception)
        {
            // Sicherung ist nur ein Netz; Fehler hier sollen die Arbeit nicht stören
        }
    }

    // ---------- Änderungen ----------

    private void OnSessionChanged()
    {
        _version++;
        ScheduleAutosave();
        Refresh();
    }

    /// <summary>Oberfläche neu aufbauen, höchstens einmal je Bild (beim Ziehen kommen viele Änderungen).</summary>
    private void Refresh()
    {
        if (_renderPending) return;
        _renderPending = true;
        _ = InvokeAsync(async () =>
        {
            await Task.Delay(16);
            _renderPending = false;
            StateHasChanged();
        });
    }

    private string _zoomText = "";

    private void UpdateZoomText()
    {
        var text = $"{Fmt.Num(_view.Zoom * 100, 0)} %" + (Math.Abs(_view.ViewRotation) > 0.05 ? $" · {Fmt.Num(_view.ViewRotation, 0)}°" : "");
        if (text == _zoomText) return;
        _zoomText = text;
        Refresh();
    }

    public void Report(Exception e)
    {
        Console.Error.WriteLine(e);
        ShowToast(Lang.F($"Fehler: {e.Message}"));
    }

    private void ShowToast(string text)
    {
        _toast = text;
        var id = ++_toastId;
        Refresh();
        _ = InvokeAsync(async () =>
        {
            await Task.Delay(Math.Clamp(text.Length * 60, 2500, 7000));
            if (id != _toastId) return;
            _toast = null;
            StateHasChanged();
        });
    }

    // ---------- Befehle ----------

    private void Add(string id, string name, Action execute, Func<bool>? canExecute = null) => _commands[id] = new Command(name, execute, false, canExecute);
    private void Pro(string id, string name, Action execute, Func<bool>? canExecute = null) => _commands[id] = new Command(name, execute, true, canExecute);

    private void Run(string id)
    {
        if (!_commands.TryGetValue(id, out var cmd)) return;
        if (cmd.CanExecute is { } can && !can()) return;
        if (cmd.Pro && !_session.RequirePro(cmd.Name)) return;
        try { cmd.Execute(); }
        catch (Exception e) { Report(e); }
        Refresh();
    }

    private bool CanRun(string id) => _commands.TryGetValue(id, out var cmd) && (cmd.CanExecute?.Invoke() ?? true);
    private bool IsProOnly(string id) => _commands.TryGetValue(id, out var cmd) && cmd.Pro && !_license.IsPro;

    private static string ItemTip(Item item, bool locked)
        => (item.Tip is null ? item.Label : $"{item.Label}\n{item.Tip}") + (locked ? Lang.T(" (Pro)") : "");

    private bool HasSelection() => _session.Selection.Count > 0;
    private bool HasConnectionPiece() => _session.ConnectionPiece is not null;
    private bool IsModulePlan() => _session.Plan.BoardShape == BoardShape.Module && _session.Plan.Modules.Count > 1;

    /// <summary>Offenes Kontextmenü (Rechtsklick im Plan) an dieser Stelle der Zeichenfläche.</summary>
    private (float X, float Y)? _ctx;

    /// <summary>
    /// „Kabel von hier verlegen“ an der gewählten Zentrale, dem Booster bzw. Trafo: Werkzeug „Kabel verlegen“ beginnt an der
    /// Bahnstromklemme (bzw. der Masseklemme). Wo das Kabel auf einem Gleis endet, entsteht die Einspeisung.
    /// </summary>
    private void StartWireFromSource(bool ground)
    {
        if (_session.SelectedObject is not { } id || _session.WireStart(id, ground) is not { } start)
        {
            _session.Say(Lang.T("Erst eine Zentrale, einen Booster oder Trafo wählen."));
            return;
        }
        _session.WireUse = ground ? WireUse.Rueckleiter : WireUse.Fahrstrom;
        SetTool(CanvasTool.Wire);
        _view.StartWireAt(start.X, start.Y);
        _session.Say(ground
            ? Lang.T("Masse-Kabel: Punkte anklicken und auf einer Schiene enden – dort entsteht der Masseanschluss. Doppelklick beendet.")
            : Lang.T("Bahnstrom-Kabel: Punkte anklicken und auf dem Gleis enden – dort entsteht die Einspeisung. Doppelklick beendet."));
        Refresh();
    }

    private void CtxRun(string id)
    {
        _ctx = null;
        Run(id);
    }

    private (double X, double Y) Center() => _view.CenterWorld();

    private void RegisterCommands()
    {
        Add("new", Lang.T("Neu"), AskNewPlan);
        Add("open", Lang.T("Öffnen"), () => _ = OpenFile());
        Add("save", Lang.T("Speichern"), () => { if (_session.RequireSave()) _ = SavePlan(); });
        Add("saveAs", Lang.T("Speichern unter"), () => { if (_session.RequireSave()) _ = SavePlanAs(); });
        Add("sample", Lang.T("Beispielanlage"), () => { _session.LoadSample(); _view.Fit(); });
        Add("license", Lang.T("Lizenz"), ShowLicense);
        Add("help", Lang.T("Bedienung"), ShowHelp);
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
        Add("paste", Lang.T("Einfügen"), () => _ = PasteFromClipboard());
        Add("delete", Lang.T("Löschen"), _session.Delete, () => HasSelection() || _session.SelectedObject is not null);
        Add("selectAll", Lang.T("Alles auswählen"), _session.SelectAll);
        Add("selectNone", Lang.T("Auswahl aufheben"), _session.ClearSelection);
        Add("nextEnd", Lang.T("Nächstes Gleisende"), () => { if (_session.NextOpenEnd() is { } p) _view.Reveal(p.X, p.Y); });
        Add("repeat", Lang.T("Gleis wiederholen"), () => _session.RepeatLast(Center()));
        Add("props", Lang.T("Eigenschaften"), () => _propsOpen = true);
        Pro("bom", Lang.T("Stückliste"), ShowBillOfMaterials);
        Add("check", Lang.T("Planprüfung"), ShowCheck);
        Add("fit", Lang.T("Einpassen"), () => _view.Fit());
        Add("zoomIn", Lang.T("Vergrößern"), () => _view.ZoomBy(1.3f));
        Add("zoomOut", Lang.T("Verkleinern"), () => _view.ZoomBy(1 / 1.3f));
        Add("viewRotL", Lang.T("Ansicht nach links drehen"), () => _view.RotateViewBy(-15));
        Add("viewRotR", Lang.T("Ansicht nach rechts drehen"), () => _view.RotateViewBy(15));
        Add("viewReset", Lang.T("Ansicht gerade ausrichten"), () => _view.SetViewRotation(0));
        Add("viewAngle", Lang.T("Ansicht drehen"), AskViewAngle);
        Add("appearance", Lang.T("Darstellung"), AskAppearance);
        Add("language", "Sprache / Language", AskLanguage);
        Add("about", Lang.T("Über das Programm"), ShowAbout);
        Add("search", Lang.T("Gleis suchen"), () => { _libraryOpen = true; _library?.FocusSearch(); });
        Add("straight", Lang.T("Gerade"), () => _session.Quick(PieceKind.Straight, null, Center()));
        Add("curve", Lang.T("Bogen"), () => _session.Quick(PieceKind.Curve, null, Center()));
        Add("switchL", Lang.T("Weiche links"), () => _session.Quick(PieceKind.Switch, false, Center()));
        Add("switchR", Lang.T("Weiche rechts"), () => _session.Quick(PieceKind.Switch, true, Center()));
        Add("buffer", Lang.T("Prellbock"), () => _session.Quick(PieceKind.Buffer, null, Center()));
        Add("crossing", Lang.T("Kreuzung"), () => _session.Quick(PieceKind.Crossing, null, Center()));
        Add("turntable", Lang.T("Drehscheibe"), () => _session.Quick(PieceKind.Turntable, null, Center()));
        Add("flex", Lang.T("Flexgleis"), () => AskFlex(_session.CurrentFlex));
        Add("toolFlex", Lang.T("Flexgleis ziehen"), () => SetTool(CanvasTool.Flex));
        Add("flexConnect", Lang.T("Lücke schließen"), () => _session.ConnectEnds(_session.CurrentFlex));
        Add("board", Lang.T("Anlagenplatte"), () => { _session.ClearSelection(); _propsOpen = true; });
        Add("modules", Lang.T("Module"), AskModules);
        Add("cutJoints", Lang.T("An Modulkante trennen"), _session.CutAtModuleJoints, IsModulePlan);
        Add("legs", Lang.T("Schenkel einzeln"), AskLegs);
        Add("levels", Lang.T("Ebenen"), AskLevels);
        Add("boardRotate", Lang.T("Anlage drehen"), () => _session.RotateBoard());
        Add("benchwork", Lang.T("Unterbau"), () => { if (_session.Plan.Benchwork is null) AskBenchwork(); else ShowBenchwork(); });
        Add("toolBoard", Lang.T("Platte ziehen"), () => SetTool(CanvasTool.Board));
        Add("grid", Lang.T("Raster"), () => Toggle(() => _session.ShowGrid = !_session.ShowGrid));
        Add("ends", Lang.T("Gleisenden"), () => Toggle(() => _session.ShowEnds = !_session.ShowEnds));
        Add("labels", Lang.T("Beschriftung"), () => Toggle(() => _session.ShowLabels = !_session.ShowLabels));
        Add("snap", Lang.T("Einrasten"), () => Toggle(() => _session.SnapEnabled = !_session.SnapEnabled));
        Add("toolSelect", Lang.T("Auswählen"), () => SetTool(CanvasTool.Select));
        Add("toolPan", Lang.T("Verschieben"), () => SetTool(CanvasTool.Pan));
        Add("heights", Lang.T("Höhen zeigen"), () => Toggle(() => _session.ShowHeights = !_session.ShowHeights));
        Add("scenery", Lang.T("Gelände zeigen"), () => Toggle(() => _session.ShowScenery = !_session.ShowScenery));
        Add("sections", Lang.T("Stromkreise zeigen"), () => Toggle(() => _session.ShowSections = !_session.ShowSections));
        Add("gradient", Lang.T("Steigung"), AskGradient, HasSelection);
        Add("helix", Lang.T("Gleiswendel"), AskHelix);
        Pro("frame", Lang.T("Spantenbau"), AskFrame);
        Pro("rampCut", Lang.T("Trasse aus Auswahl"), AskRampCut, HasSelection);
        Add("tunnel", "Tunnel", () => _session.SetTunnel(!_session.SelectedPieces.All(p => p.Tunnel)), HasSelection);
        Add("toolTerrain", Lang.T("Gelände formen"), () => SetTool(CanvasTool.Terrain));
        Add("toolWater", Lang.T("Gewässer"), () => SetTool(CanvasTool.Water));
        Add("toolBuilding", Lang.T("Gebäude"), () => SetTool(CanvasTool.Building));
        Add("toolTree", Lang.T("Vegetation"), () => SetTool(CanvasTool.Tree));
        Add("toolLabel", "Text", () => SetTool(CanvasTool.Label));
        Add("toolDimension", Lang.T("Maß"), () => SetTool(CanvasTool.Dimension));
        Add("toolBeam", Lang.T("Leiste ziehen"), () => SetTool(CanvasTool.Beam));
        Add("addPlate", Lang.T("Platte auflegen"), () => { SetTool(CanvasTool.Select); _session.AddPlate(); });
        Add("toolPower", Lang.T("Trafo / Zentrale"), () =>
        {
            if (_session.Tool == CanvasTool.Power && _session.PowerType != PowerSourceType.Verteiler) SetTool(CanvasTool.Select);
            else AskPowerType(t => { _session.PowerType = t; SetTool(CanvasTool.Power); });
        });
        Add("toolDistributor", Lang.T("Verteiler"), () =>
        {
            if (_session.Tool == CanvasTool.Power && _session.PowerType == PowerSourceType.Verteiler) SetTool(CanvasTool.Select);
            else AskDistributorColor(hex => { _session.DistributorColor = hex; _session.PowerType = PowerSourceType.Verteiler; SetTool(CanvasTool.Power); });
        });
        Add("wireFromSource", Lang.T("Kabel von hier verlegen"), () => StartWireFromSource(false));
        Add("groundFromSource", Lang.T("Masse-Kabel von hier verlegen"), () => StartWireFromSource(true));
        Add("toolIsolation", Lang.T("Trennstelle"), () => SetTool(CanvasTool.Isolation));
        Add("toolSignal", Lang.T("Signal setzen"), () =>
        {
            if (_session.Tool == CanvasTool.Signal) SetTool(CanvasTool.Select);
            else AskSignalType(_session.SignalType, t => { _session.SignalType = t; SetTool(CanvasTool.Signal); });
        });
        Add("elecCheck", Lang.T("Elektrik prüfen"), ShowElectricCheck);
        Add("current", Lang.T("Betrieb und Stromart"), AskCurrent);
        Add("elecLabels", Lang.T("Nur Elektrik beschriften"), () => Toggle(() => _session.ElectricLabelsOnly = !_session.ElectricLabelsOnly));
        Add("toolWire", Lang.T("Kabel verlegen"), () =>
        {
            if (_session.Tool == CanvasTool.Wire) SetTool(CanvasTool.Select);
            else AskWireUse(u => { _session.WireUse = u; SetTool(CanvasTool.Wire); });
        });
        Add("connIsolate", Lang.T("Gleis trennen"), () => { if (_session.ConnectionPiece is { } p) _session.ToggleAllIsolations(p.Id); }, HasConnectionPiece);
        Add("connDetector", Lang.T("Rückmelder am Gleis"), () => { if (_session.ConnectionPiece is { } p) _session.ToggleDetectorOn(p.Id); }, HasConnectionPiece);
        Add("addresses", Lang.T("Decoder und Adressen"), ShowAddresses);
        Add("wiring", Lang.T("Kabelplan"), ShowWiring);
        Add("wires", Lang.T("Kabel zeigen"), () => { _session.ShowWires = !_session.ShowWires; _session.Say(_session.ShowWires ? Lang.T("Kabel eingeblendet.") : Lang.T("Kabel ausgeblendet – über das Kontextmenü oder „Kabel zeigen“ wieder einblenden.")); _view.Invalidate(); });
        Add("numberElectrics", Lang.T("Elektrik nummerieren"), () => _session.NumberElectrics());
        Add("routes", Lang.T("Fahrstraßen"), ShowRoutes);
        Pro("controlExport", Lang.T("Export für Steuerprogramme"), ShowControlExport);
        Add("view3d", Lang.T("3D-Ansicht"), () => Open3D(View3DMode.Orbit));
        Add("ride", Lang.T("Mitfahren"), () => Open3D(View3DMode.Ride));
        Add("daytime", Lang.T("Tageszeit"), () => Open3D(View3DMode.Evening));
        Pro("csv", Lang.T("Export als CSV"), () => Export(BillOfMaterials.ToCsv(_session.Plan), "text/csv", Lang.F($"{SafeName} – Stückliste.csv")));
        Pro("print", Lang.T("Drucken"), AskPrint);
        Pro("svg", Lang.T("Export als SVG"), () => Export(SvgExport.Build(_session.Plan), "image/svg+xml", $"{SafeName}.svg"));
        Pro("dxf", Lang.T("Export als DXF"), () => Export(DxfExport.Build(_session.Plan), "application/dxf", $"{SafeName}.dxf"));
        Pro("png", Lang.T("Export als PNG"), () => _ = ExportPng());
        // Paket 1 und 2: Raum, Hintergrundbild, Anordnen, Parallelgleis, Höhenprofil, Rückmelder
        Add("lock", Lang.T("Sperren"), _session.ToggleLock, HasSelection);
        Add("group", Lang.T("Gruppieren"), _session.GroupSelection, () => _session.Selection.Count > 1);
        Add("ungroup", Lang.T("Gruppe auflösen"), _session.Ungroup, HasSelection);
        Add("alignL", Lang.T("Links ausrichten"), () => _session.Align(AlignMode.Left), () => _session.Selection.Count > 1);
        Add("alignR", Lang.T("Rechts ausrichten"), () => _session.Align(AlignMode.Right), () => _session.Selection.Count > 1);
        Add("alignT", Lang.T("Oben ausrichten"), () => _session.Align(AlignMode.Top), () => _session.Selection.Count > 1);
        Add("alignB", Lang.T("Unten ausrichten"), () => _session.Align(AlignMode.Bottom), () => _session.Selection.Count > 1);
        Add("alignCX", Lang.T("Mittig waagrecht"), () => _session.Align(AlignMode.CenterX), () => _session.Selection.Count > 1);
        Add("alignCY", Lang.T("Mittig senkrecht"), () => _session.Align(AlignMode.CenterY), () => _session.Selection.Count > 1);
        Add("parallel", Lang.T("Parallelgleis"), AskParallel, HasSelection);
        Add("suggest", Lang.T("Planvorschlag"), AskSuggest);
        Add("profile", Lang.T("Höhenprofil"), ShowProfile);
        Add("roomRect", Lang.T("Raum"), AskRoom);
        Add("toolRoom", Lang.T("Raum zeichnen"), () => SetTool(CanvasTool.Room));
        Add("toolDoor", Lang.T("Tür"), () => SetObstacleTool(ObstacleType.Tuer));
        Add("toolWindow", Lang.T("Fenster"), () => SetObstacleTool(ObstacleType.Fenster));
        Add("toolColumn", Lang.T("Säule"), () => SetObstacleTool(ObstacleType.Saeule));
        Add("toolHeater", Lang.T("Heizkörper"), () => SetObstacleTool(ObstacleType.Heizkoerper));
        Add("toolFurniture", Lang.T("Möbel"), () => SetObstacleTool(ObstacleType.Moebel));
        Add("bgLoad", Lang.T("Hintergrundbild laden"), () => _ = LoadBackground());
        Add("bgEdit", Lang.T("Hintergrundbild"), AskBackground, () => _session.Plan.Background is not null);
        Add("bgScale", Lang.T("Bild nach Maß skalieren"), AskScaleBackground, () => _session.Plan.Background is not null);
        Add("bgToggle", Lang.T("Hintergrundbild zeigen"), () => _session.EditBackground(b => b.Visible = !b.Visible), () => _session.Plan.Background is not null);
        Add("modelsTunnel", Lang.T("Tunnel und Brücken"), () => AskModelCategory(BuiltinModels.CatTunnel));
        Add("modelsStation", Lang.T("Bahnhof"), () => AskModelCategory(BuiltinModels.CatStation));
        Add("modelsRailway", Lang.T("Bahnanlagen"), () => AskModelCategory(BuiltinModels.CatRailway));
        Add("modelsHouses", Lang.T("Häuser"), () => AskModelCategory(BuiltinModels.CatHouses));
        AddEquipmentProCommands();
        Add("toolDetector", Lang.T("Rückmelder"), () =>
        {
            if (_session.Tool == CanvasTool.Detector) SetTool(CanvasTool.Select);
            else AskDetectorType(t => { _session.DetectorType = t; SetTool(CanvasTool.Detector); });
        });
    }

    private void AskModelCategory(string category)
    {
        if (_session.Tool == CanvasTool.Model && _session.CurrentModel?.Category == category) { SetTool(CanvasTool.Select); return; }
        AskModel(category, BuiltinModels.All.Where(m => m.Category == category).ToList());
    }

    /// <summary>Modell für das Werkzeug „Modell einfügen“ wählen.</summary>
    private void SetModelTool(string id)
    {
        // Gleise gewählt und Brücke, Stützmauer oder Bahnsteig: gleich entlang der Auswahl setzen
        if (_session.Tool != CanvasTool.Model && _session.PlaceModelOnSelection(id) is not null) { _session.ModelId = id; _view.Invalidate(); Refresh(); return; }
        _session.ModelId = id;
        if (_session.Tool != CanvasTool.Model) SetTool(CanvasTool.Model);
        else { _view.Invalidate(); Refresh(); }
    }

    private bool ModelToolIn(string category) => _session.Tool == CanvasTool.Model && _session.CurrentModel is { } m && m.Category == category && m.IsBuiltin;

    private void SetObstacleTool(ObstacleType type)
    {
        if (_session.Tool == CanvasTool.Obstacle && _session.ObstacleType == type) { SetTool(CanvasTool.Select); return; }
        _session.ObstacleType = type;
        SetTool(CanvasTool.Select);
        SetTool(CanvasTool.Obstacle);
    }

    /// <summary>Plan als PNG: alles im Blick, längste Seite etwa 4000 Pixel.</summary>
    private async Task ExportPng()
    {
        var plan = _session.Plan;
        var (x0, y0, x1, y1) = plan.Bounds();
        const double margin = 40;
        double w = x1 - x0 + 2 * margin, h = y1 - y0 + 2 * margin;
        var scale = (float)Math.Min(4, 4000 / Math.Max(w, h));
        int pw = (int)Math.Ceiling(w * scale), ph = (int)Math.Ceiling(h * scale);
        var c = new Graphics.Canvas(pw, ph);
        c.Save();
        c.Scale(scale, scale);
        c.Translate((float)(margin - x0), (float)(margin - y0));
        new PlanPainter(plan, new HashSet<int>(), false).Draw(c, scale, _session.ShowScenery, new HashSet<int>(), null);
        c.Restore();
        var name = $"{SafeName}.png";
        if (await Js.DownloadPng(name, Graphics.SvgWriter.Image(c), pw, ph)) ShowToast(Lang.F($"Gespeichert: {name} ({pw} × {ph} Pixel)"));
        else ShowToast(Lang.T("Das Bild ließ sich nicht erzeugen."));
    }

    private async Task LoadBackground()
    {
        var result = await Js.PickImage(2400);
        if (string.IsNullOrEmpty(result)) return;
        var parts = result.Split('\0');
        if (parts.Length < 5 || !int.TryParse(parts[2], out var pw) || !int.TryParse(parts[3], out var ph) || pw <= 0 || ph <= 0)
        {
            Message(Lang.T("Hintergrundbild"), Lang.T("Das Bild ließ sich nicht lesen (PNG, JPEG, GIF oder WebP)."));
            return;
        }
        var old = _session.Plan.Background;
        _session.SetBackground(new PlanBackground
        {
            Data = parts[4], Mime = parts[1], PixelWidth = pw, PixelHeight = ph,
            X = old?.X ?? 0, Y = old?.Y ?? 0, Width = old?.Width ?? _session.Plan.BoardWidth, Opacity = old?.Opacity ?? 0.5,
        });
        _view.Fit();
        Refresh();
    }

    private void Toggle(Action change)
    {
        change();
        _view.Rebuild();
        _session.Commit();
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
        "connDetector" => _session.ConnectionPiece is { } dp && _session.DetectorOn(dp.Id) is not null,
        "connIsolate" => _session.ConnectionPiece is { } ip && _session.PieceEnds(ip.Id).Where(e => e.Connected).ToList() is { Count: > 0 } ends && ends.All(e => e.Isolated),
        "toolSelect" => _session.Tool == CanvasTool.Select,
        "toolPan" => _session.Tool == CanvasTool.Pan,
        "toolBoard" => _session.Tool == CanvasTool.Board,
        "toolFlex" => _session.Tool == CanvasTool.Flex,
        "toolTerrain" => _session.Tool == CanvasTool.Terrain,
        "toolWater" => _session.Tool == CanvasTool.Water,
        "toolBuilding" => _session.Tool == CanvasTool.Building,
        "toolTree" => _session.Tool == CanvasTool.Tree,
        "toolLabel" => _session.Tool == CanvasTool.Label,
        "toolDimension" => _session.Tool == CanvasTool.Dimension,
        "toolBeam" => _session.Tool == CanvasTool.Beam,
        "wires" => _session.ShowWires,
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
        "modelsTunnel" => ModelToolIn(BuiltinModels.CatTunnel),
        "modelsStation" => ModelToolIn(BuiltinModels.CatStation),
        "modelsRailway" => ModelToolIn(BuiltinModels.CatRailway),
        "modelsHouses" => ModelToolIn(BuiltinModels.CatHouses),
        "modelsOwn" => _session.Tool == CanvasTool.Model && _session.CurrentModel is { IsBuiltin: false },
        _ => false,
    };

    private static readonly HashSet<string> Toggles =
    [
        "grid", "ends", "labels", "snap", "heights", "scenery", "sections", "toolSelect", "toolPan", "toolFlex", "toolTerrain", "toolWater",
        "toolBoard", "toolBuilding", "toolTree", "toolLabel", "toolDimension", "toolBeam", "toolPower", "toolDistributor", "toolIsolation", "wires", "toolSignal",
        "toolRoom", "toolDetector", "toolDoor", "toolWindow", "toolColumn", "toolHeater", "toolFurniture", "bgToggle",
        "modelsTunnel", "modelsStation", "modelsRailway", "modelsHouses", "modelsOwn", "elecLabels", "toolWire", "connDetector", "connIsolate",
    ];

    private void SetTool(CanvasTool tool)
    {
        if (_view.IsDrawingPolygon && tool != _session.Tool) _view.CancelPolygon();
        _session.Tool = _session.Tool == tool && tool != CanvasTool.Select ? CanvasTool.Select : tool;
        if (_session.Tool is not (CanvasTool.Select or CanvasTool.Pan)) _session.ClearSelection();
        _view.Invalidate();
        Refresh();
    }

    private string ToolText => _session.Tool switch
    {
        CanvasTool.Terrain => Lang.F($"Gelände formen: Eckpunkte setzen ({_view.PolygonPoints}), dann „Fertig“ oder Doppelklick."),
        CanvasTool.Water => Lang.F($"Gewässer: Eckpunkte setzen ({_view.PolygonPoints}), dann „Fertig“ oder Doppelklick."),
        CanvasTool.Flex => Lang.T("Flexgleis ziehen: am offenen Gleisende ansetzen und ziehen, es rastet an Gleisenden ein."),
        CanvasTool.Building => Lang.T("Gebäude: klicken oder Rechteck aufziehen."),
        CanvasTool.Model => _session.CurrentModel is { } model
            ? Lang.F($"{model.Name}: in den Plan klicken.") + (model.Snap == ModelSnap.Frei ? "" : " " + Lang.T("Rastet am nächsten Gleis ein.")) + (ModelPlacement.FitsSelection(model) ? " " + Lang.T("Tipp: Erst Gleise wählen, dann das Modell – es wird entlang der Auswahl gesetzt.") : "")
            : Lang.T("Bitte zuerst ein Modell wählen."),
        CanvasTool.Tree => Lang.T("Vegetation: jeder Klick setzt einen Baum."),
        CanvasTool.Feeder => Lang.T("Gleisanschluss: auf ein Gleis klicken. Er hängt an der Zentrale bzw. dem Trafo (gestrichelte Linie); fehlt sie, wird sie angelegt."),
        CanvasTool.Power => Decoders.Find(_session.ModuleModel) is { } mm ? Lang.F($"{mm.Maker} {mm.Id}: an die Stelle im Plan klicken, wo der Baustein liegen soll (auch neben der Platte).")
            : Lang.F($"{PowerSupply.TypeName(_session.PowerType)}: an die Stelle im Plan klicken, wo sie stehen soll (auch neben der Platte)."),
        CanvasTool.Wire => Lang.F($"Kabel ({WireLayout.UseName(_session.WireUse)}): Punkte setzen ({_view.PolygonPoints}) – rastet an Zentrale/Trafo (B und 0), Verteilern, Rückmeldern, Signalen und Weichenantrieben ein; auf einem Gleis entsteht eine Einspeisung. Doppelklick oder „Fertig“ beendet."),
        CanvasTool.Room => Lang.F($"Raum zeichnen: Ecken der Wand-Innenkante setzen ({_view.PolygonPoints}), dann „Fertig“ oder Doppelklick."),
        CanvasTool.Obstacle => Lang.F($"{SceneryItem.ObstacleName(_session.ObstacleType)}: klicken oder Rechteck aufziehen."),
        CanvasTool.Detector => Lang.F($"{Detector.TypeName(_session.DetectorType)}: auf ein Gleis klicken (Klick auf einen Rückmelder entfernt ihn)."),
        CanvasTool.Isolation => Lang.T("Trennstelle: auf eine Gleisverbindung (grüner Punkt) klicken."),
        CanvasTool.Signal => Lang.F($"{Signal.TypeName(_session.SignalType)} ({Signal.SystemShort(_session.SignalSystem)}): auf einen Punkt an einer Gleisverbindung klicken."),
        CanvasTool.Label => Lang.T("Text: auf die Stelle klicken und den Text eingeben."),
        CanvasTool.Dimension => Lang.T("Maß: zwei Punkte anklicken; Gleisenden und Plattenecken rasten ein."),
        CanvasTool.Beam => Lang.T("Leiste: Anfang und Ende anklicken. Plattenkanten, Ecken und andere Leisten rasten ein, von einer Kante aus auch rechtwinklig auf eine Leiste."),
        CanvasTool.Pan => Lang.T("Verschieben: Ansicht mit der linken Maustaste ziehen."),
        CanvasTool.Board => _session.Plan.BoardShape == BoardShape.Module
            ? Lang.T("Platte ziehen: Ecke eines Moduls ziehen ändert seine Größe, ein Modul ziehen verschiebt es samt Gleisen. Kanten rasten an Nachbarmodulen ein. Klick auf ein Modul: Maße eintippen oder drehen.")
            : Lang.T("Platte ziehen: eine Ecke der Platte ziehen ändert ihre Größe, Klick auf die Platte: Maße eintippen oder drehen. Esc beendet."),
        _ => "",
    };

    private string SafeName
    {
        get
        {
            var n = string.Concat(_session.Plan.Name.Select(ch => ch is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' || char.IsControl(ch) ? '_' : ch)).Trim();
            return n.Length == 0 ? Lang.T("Gleisplan") : n;
        }
    }

    // ---------- Tastatur ----------

    public bool KeyDown(string key, bool ctrl, bool shift, bool alt)
    {
        if (_dialog is not null)
        {
            if (key == "Escape") { DismissDialog(); return true; }
            return false;
        }
        if (_editModel is not null) return EditorKeyDown(key, ctrl);
        if (_open3D)
        {
            if (key == "Escape") { Close3D(); return true; }
            // Richtung an der nächsten Weiche: Pfeiltasten, W schaltet weiter
            if (key is "ArrowLeft" or "ArrowUp" or "ArrowRight" && (_riding || _trainOn))
            {
                ChooseDirection(key == "ArrowLeft" ? -1 : key == "ArrowRight" ? 1 : 0);
                StateHasChanged();
                return true;
            }
            if (key is "w" or "W" && (_riding || _trainOn)) { ToggleNextSwitch(); StateHasChanged(); return true; }
            return false;
        }
        var k = key.Length == 1 ? key.ToLowerInvariant() : key;
        if (ctrl && shift && k == "s") { Run("saveAs"); return true; }
        if (ctrl && k == "s") { Run("save"); return true; }
        if (ctrl && k == "o") { Run("open"); return true; }
        if (ctrl && k == "p") { Run("print"); return true; }
        if (ctrl && k == "f") { Run("search"); return true; }
        if (key == "F1") { Run("help"); return true; }
        if (key == "F7") { Run("check"); return true; }
        if (key == "Enter" && _view.IsDrawingPolygon) { _view.FinishPolygon(); return true; }
        if (key == "Enter" && !ctrl && _session.Tool is CanvasTool.Select or CanvasTool.Pan) { Run("repeat"); return true; }
        if (key == "Escape" && _session.Tool is not (CanvasTool.Select or CanvasTool.Pan))
        {
            if (_view.IsDrawingPolygon) _view.CancelPolygon();
            else SetTool(CanvasTool.Select);
            return true;
        }
        // Esc entfernt zuerst die Markierungen der letzten Prüfung
        if (key == "Escape" && !ctrl && _session.Issues.Count > 0) { CloseIssueNav(); return true; }
        string? cmd = (ctrl, k) switch
        {
            (true, "z") => shift ? "redo" : "undo",
            (true, "y") => "redo",
            (true, "a") => "selectAll",
            (true, "d") => "duplicate",
            (true, "c") => "copy",
            (true, "v") => "paste",
            (false, "Delete") or (false, "Backspace") => "delete",
            (false, "r") => shift ? "rotL" : "rotR",
            (false, "f") => "flip",
            (false, "a") => "swap",
            (false, "e") => "nextEnd",
            (false, "Escape") => "selectNone",
            (false, "+") => "zoomIn",
            (false, "-") => "zoomOut",
            (false, "0") => "fit",
            _ => null,
        };
        if (cmd is not null)
        {
            Run(cmd);
            return true;
        }
        if (!ctrl && _session.CanNudge && key is "ArrowLeft" or "ArrowRight" or "ArrowUp" or "ArrowDown")
        {
            var step = shift ? 10 : 1;
            var dx = key == "ArrowLeft" ? -step : key == "ArrowRight" ? step : 0;
            var dy = key == "ArrowUp" ? -step : key == "ArrowDown" ? step : 0;
            _session.Nudge(dx, dy, record: true);
            return true;
        }
        return false;
    }

    // ---------- Zwischenablage ----------

    private void CopyToClipboard()
    {
        if (_session.CopySelection() is not { } json) return;
        _clipboard = json;
        _ = Js.CopyText(json);
    }

    private async Task PasteFromClipboard()
    {
        string? text = null;
        try { text = await Js.ReadClipboard(); }
        catch (Exception) { }
        if (string.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith('{')) text = _clipboard;
        _session.Paste(text, _view.Cursor ?? Center());
        Refresh();
    }

    // ---------- Dateien ----------

    /// <summary>Endung, unter der die Webversion Pläne speichert. Inhalt wie *.trackmaster.json, daher öffnet sie auch diese.</summary>
    private const string WebFileExtension = ".mpr";
    private const string PlanFileTypes = ".mpr,.json,.trackmaster.json,application/json";

    private async Task OpenFile()
    {
        var result = await Js.PickFile(PlanFileTypes);
        if (string.IsNullOrEmpty(result)) return;
        _saveAsName = null;
        Js.ForgetSaveHandle();
        var cut = result.IndexOf('\0');
        var name = cut > 0 ? result[..cut] : null;
        var text = cut >= 0 ? result[(cut + 1)..] : result;
        try
        {
            _session.OpenJson(text, name);
            _view.Fit();
        }
        catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException or FormatException or NotSupportedException or ArgumentException or KeyNotFoundException)
        {
            Message(Lang.T("Öffnen"), Lang.F($"Die Datei ist kein TrackMaster-Plan: {e.Message}"));
        }
        Refresh();
    }

    /// <summary>Name der Datei, die „Speichern unter“ gewählt hat; „Speichern“ schreibt dann wieder hinein.</summary>
    private string? _saveAsName;

    private async Task SavePlan()
    {
        var json = PlanSerializer.ToJson(_session.Plan);
        if (_saveAsName is { } chosen && _session.FilePath == chosen && await Js.SaveToHandle(json))
        {
            _session.MarkSaved(chosen);
            Refresh();
            return;
        }
        var name = $"{SafeName}{WebFileExtension}";
        Js.Download(name, "application/json", json);
        _session.MarkSaved(name);
        Refresh();
    }

    private async Task SavePlanAs()
    {
        var current = _session.FilePath is { Length: > 0 } f ? System.IO.Path.GetFileName(f) : null;
        var suggested = current is not null && current.EndsWith(WebFileExtension, StringComparison.OrdinalIgnoreCase) ? current : $"{SafeName}{WebFileExtension}";
        var name = await Js.SaveAs(suggested, WebFileExtension, Lang.T("TrackMaster-Plan"), Lang.T("Dateiname für den Plan:"), PlanSerializer.ToJson(_session.Plan));
        if (string.IsNullOrEmpty(name)) return;
        _saveAsName = name;
        _session.MarkSaved(name);
        Refresh();
    }

    /// <summary>Gespeicherten Plan wählen und als Modul rechts ansetzen.</summary>
    private async Task AppendModuleFile()
    {
        var result = await Js.PickFile(PlanFileTypes);
        if (string.IsNullOrEmpty(result)) return;
        var cut = result.IndexOf('\0');
        var name = cut > 0 ? result[..cut] : Lang.T("Modul");
        var text = cut >= 0 ? result[(cut + 1)..] : result;
        try
        {
            var other = PlanSerializer.FromJson(text, out _);
            var title = other.Name is { Length: > 0 } n && n != Lang.T("Neuer Plan") && n != Lang.T("Gleisplan") ? n : name.Replace(PlanSerializer.FileExtension, "").Replace(".json", "").Replace(WebFileExtension, "");
            if (_session.AppendModule(other, title)) _view.Fit();
        }
        catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException or FormatException or NotSupportedException or ArgumentException or KeyNotFoundException)
        {
            Message(Lang.T("Plan als Modul ansetzen"), Lang.F($"Die Datei ist kein TrackMaster-Plan: {e.Message}"));
        }
        Refresh();
    }

    private void Export(string content, string mime, string fileName)
    {
        Js.Download(fileName, mime, content);
        ShowToast(Lang.F($"Gespeichert: {fileName}"));
    }

    /// <summary>Speichert Zuschnitte als ZIP mit je einer SVG-Datei pro Teil.</summary>
    private void ExportSvgFiles(IReadOnlyList<SvgFile> files, string name)
    {
        var fileName = SvgFiles.FileName(name) + ".zip";
        Js.DownloadBytes(fileName, "application/zip", SvgFiles.Zip(files));
        ShowToast(Lang.F($"Gespeichert: {fileName} ({files.Count} SVG-Dateien)"));
    }

    // ---------- Prüfen ----------

    private void ShowCheck()
    {
        var issues = PlanCheck.Run(_session.Plan);
        _session.Issues = issues;
        _view.Invalidate();
        _issueNav = (Lang.T("Planprüfung"), issues, null);
        ShowIssues(Lang.T("Planprüfung"), issues, null);
    }

    private void ShowElectricCheck()
    {
        var report = Electrics.Analyze(_session.Plan);
        _session.Issues = report.Issues;
        if (!_session.ShowSections) { _session.ShowSections = true; _view.Rebuild(); _session.Commit(); }
        _view.Invalidate();
        var footer = Lang.F($"{Electrics.CurrentName(report.Current)}: {report.Sections.Count} Stromabschnitte, {report.ReverseLoops.Count} Kehrschleifen. {(report.ThreeRail ? Lang.T("Mittelleiter: Kehrschleifen ohne Sonderschaltung möglich.") : Lang.T("Zweileiter-System."))}");
        _issueNav = (Lang.T("Elektrik prüfen"), report.Issues, footer);
        ShowIssues(Lang.T("Elektrik prüfen"), report.Issues, footer);
    }

    /// <summary>Befunde der letzten Prüfung zum Durchblättern im Plan (Leiste unten), null = keine.</summary>
    private (string Title, IReadOnlyList<PlanIssue> Issues, string? Footer)? _issueNav;

    private void PickIssue(PlanIssue issue)
    {
        CloseDialog();
        var (x0, y0, x1, y1) = _session.FocusIssue(issue);
        _view.Rebuild();
        _view.FitArea(x0, y0, x1, y1);
        Refresh();
    }

    private void StepIssue(int delta)
    {
        if (_issueNav is not { } nav || nav.Issues.Count == 0) return;
        var i = _session.FocusedIssue is { } f ? IndexOf(nav.Issues, f) : -1;
        var next = ((i < 0 ? (delta > 0 ? -1 : 0) : i) + delta + nav.Issues.Count) % nav.Issues.Count;
        PickIssue(nav.Issues[next]);
    }

    private static int IndexOf(IReadOnlyList<PlanIssue> list, PlanIssue item)
    {
        for (var i = 0; i < list.Count; i++) if (ReferenceEquals(list[i], item)) return i;
        return -1;
    }

    private void CloseIssueNav()
    {
        _issueNav = null;
        _session.Issues = [];
        _session.ClearFocus();
        _view.Invalidate();
        Refresh();
    }

    private void AskGradient()
    {
        var sel = _session.SelectedPieces;
        var start = sel.Count > 0 ? sel.Min(p => p.ConnectorHeights().Min()) : 0;
        AskGradient(sel.Count, start, _session.GradientLength,
            (s, p, r) => _session.ApplyGradient(s, p, r), (s, e, r) => _session.ApplyGradientHeights(s, e, r));
    }

    private void ShowWiring()
    {
        var text = Electrics.WiringPlan(_session.Plan);
        ShowText(Lang.T("Kabelplan"), text, Lang.T("Als Text speichern"), () =>
        {
            if (_session.RequirePro(Lang.T("Kabelplan"))) Export(text, "text/plain", Lang.F($"{SafeName} – Kabelplan.txt"));
        }, Lang.T("Anschlussliste als CSV"), () =>
        {
            if (_session.RequirePro(Lang.T("Kabelplan"))) Export(ConnectionList.ToCsv(_session.Plan), "text/csv", Lang.F($"{SafeName} – Anschlussliste.csv"));
        });
    }

    private void ShowRoutes()
    {
        var routes = TrainRoutes.Find(_session.Plan);
        ShowText(Lang.T("Fahrstraßen"), TrainRoutes.ToText(_session.Plan, routes), Lang.T("Als CSV speichern"), () =>
        {
            if (_session.RequirePro(Lang.T("Fahrstraßen speichern"))) Export(TrainRoutes.ToCsv(routes), "text/csv", Lang.F($"{SafeName} – Fahrstraßen.csv"));
        });
    }

    private void AskHelix()
    {
        var gauge = _session.Plan.Gauge;
        var curves = _session.CurrentSystem.Pieces.Where(p => p.Kind == PieceKind.Curve && 360 % p.Angle < 0.01).ToList();
        if (curves.Count == 0) curves = gauge.Systems.SelectMany(x => x.Pieces).Where(p => p.Kind == PieceKind.Curve && 360 % p.Angle < 0.01).ToList();
        if (curves.Count == 0) { ShowToast(Lang.T("Für diese Spur gibt es keine Bogengleise im Katalog. Die Gleiswendel lässt sich hier mit Flexgleis bauen.")); return; }
        var straights = _session.CurrentSystem.Pieces.Where(p => p.Kind == PieceKind.Straight && p.Length >= gauge.TrackWidth * 3).ToList();
        if (straights.Count == 0) straights = gauge.Systems.SelectMany(x => x.Pieces).Where(p => p.Kind == PieceKind.Straight && p.Length >= gauge.TrackWidth * 3).ToList();
        AskHelix(gauge, curves, straights, _session.ActiveConnector is not null, spec =>
        {
            if (_session.BuildHelix(spec, Center()) is not { } material) return;
            _view.Fit();
            // Die Holzliste gehört wie die Gleis-Stückliste zur Pro-Version
            if (_session.RequirePro(Lang.T("Holzliste der Gleiswendel")))
                ShowHelixMaterial(material, () => Export(material.ToCsv(), "text/csv", Lang.F($"{SafeName} – Holzliste Gleiswendel.csv")));
        });
    }

    private void AskViewAngle() => AskAngle(Lang.T("Ansicht drehen"),
        Lang.T("Dreht nur die Ansicht – der Plan selbst bleibt unverändert. Auch mit Strg + Mausrad oder zwei Fingern; ein Klick auf den Kompass richtet sie wieder gerade aus."),
        Math.Round(_view.ViewRotation, 1), Lang.T("Drehen"), a => _view.SetViewRotation(a));

    private void AskFreeRotation() => AskAngle(Lang.T("Auswahl frei drehen"),
        Lang.T("Dreht die gewählten Gleise um ihren gemeinsamen Mittelpunkt, um einen beliebigen Winkel."),
        7.5, Lang.T("Drehen"), a => _session.Rotate(a));

    // ---------- Lizenz ----------

    private async Task ChooseLicenseFile()
    {
        var result = await Js.PickFile(".lic,application/json,text/plain");
        if (string.IsNullOrEmpty(result)) return;
        var cut = result.IndexOf('\0');
        var text = cut >= 0 ? result[(cut + 1)..] : result;
        InstallLicense(text);
        StateHasChanged();
    }

    /// <summary>Lizenztext aus dem Eingabefeld übernehmen; das Fenster bleibt offen, wenn er nicht passt.</summary>
    private bool PasteLicense()
    {
        // Aus E-Mails oder Textprogrammen kommen Anführungszeichen manchmal typografisch zurück
        var text = D("lic").Trim().Replace('“', '"').Replace('”', '"').Replace('„', '"').Replace('\u00A0', ' ');
        if (text.Length == 0)
            return Fail([Lang.T("Bitte zuerst den Inhalt der Lizenzdatei in das Feld einfügen.")]);
        var check = _license.Verify(text);
        if (!check.IsValid)
            return Fail([LicenseProblem(check)]);
        CloseAllDialogs();
        InstallLicense(text);
        return false;
    }

    private void InstallLicense(string text)
    {
        var check = _license.Install(text);
        if (check.IsValid)
        {
            Message(Lang.T("Lizenz"), Lang.F($"Pro-Lizenz eingespielt: {_license.Summary}"));
            _view.Rebuild();
            if (_license.ActivationDue) _ = SyncActivation(interactive: true);
        }
        else
            Message(Lang.T("Lizenz"), LicenseProblem(check));
    }

    /// <summary>Was an der Lizenz nicht stimmt, mit dem wahrscheinlichsten Grund.</summary>
    private static string LicenseProblem(LicenseCheckResult check) => check.Status switch
    {
        LicenseStatus.InvalidSignature => Lang.T("Die Lizenz wurde nicht übernommen: Die Signatur passt nicht zu TrackMaster Pro. Meist ist es eine Lizenz für ein anderes Programm, oder sie wurde mit einem anderen Schlüssel erstellt oder beim Kopieren verändert."),
        LicenseStatus.Malformed => Lang.T("Die Lizenz wurde nicht übernommen: Das ist kein vollständiger Lizenztext. Bitte den ganzen Inhalt der Datei „trackmaster.lic“ einfügen, von { bis }."),
        _ => Lang.F($"Die Lizenz wurde nicht übernommen: {check.StatusText}."),
    };

    private const string ActivationKey = Settings.LicenseKey + ".activation";

    private static string AppVersion => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "";

    private static WebLicense CreateLicense(string deviceId)
    {
        string url;
        try { url = Js.ActivationUrl(); } catch { url = ""; }
        string name;
        try { name = Js.BrowserName(); } catch { name = "Browser"; }
        var activation = url.Length == 0 ? null : new LicenseActivation(url, deviceId, "Web", name,
            () => Js.StorageGet(ActivationKey), json => { if (json is null) Js.StorageRemove(ActivationKey); else Js.StorageSet(ActivationKey, json); });
        return new WebLicense(deviceId, () => Js.StorageGet(Settings.LicenseKey), text => Js.StorageSet(Settings.LicenseKey, text), activation: activation);
    }

    /// <summary>Beim Aktivierungsserver anmelden bzw. nachfragen; zeigt eine Meldung, wenn sich etwas Wichtiges ändert.</summary>
    private async Task SyncActivation(bool interactive)
    {
        var wasPro = _license.IsPro;
        string? message;
        try { message = await _license.SyncActivationAsync(AppVersion); }
        catch (Exception e) { message = e.Message; }
        if (interactive && message is not null) ShowToast(message);
        else if (wasPro && !_license.IsPro && message is not null) Message(Lang.T("Lizenz"), message);
        _view.Rebuild();
        StateHasChanged();
    }

    private async Task ReleaseActivation()
    {
        string message;
        try { message = await _license.ReleaseActivationAsync(AppVersion); }
        catch (Exception e) { message = e.Message; }
        CloseAllDialogs();
        Message(Lang.T("Lizenz"), message);
        _view.Rebuild();
        StateHasChanged();
    }

    private void RemoveLicense()
    {
        Js.StorageRemove(Settings.LicenseKey);
        Js.StorageRemove(ActivationKey);
        _license = CreateLicense(_license.MachineId);
        CloseDialog();
        ShowToast(Lang.T("Die Lizenz wurde aus diesem Browser entfernt. Es läuft wieder die Basisversion."));
    }

    // ---------- Darstellung und Sprache ----------

    private void UseAppearance(AppearanceSettings a)
    {
        Ui.D = a.Palette;
        Ui.FontScale = a.Scale;
        var font = Settings.CssFont(a.FontChoice);
        Graphics.SvgWriter.FontFamily = font;
        Js.SetFont(font);
    }

    private void ApplyAppearance(AppearanceSettings a)
    {
        _appearance = a;
        Settings.SaveAppearance(a);
        UseAppearance(a);
        _appearanceVersion++;
        _view?.Rebuild();
        StateHasChanged();
    }

    private string RootStyle
    {
        get
        {
            var d = _appearance.Palette;
            static string Css(Rgb c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            return $"--accent:{Css(d.Accent)};--accent-dark:{Css(d.AccentDark)};--accent-light:{Css(d.AccentLight)};--accent-border:{Css(d.AccentBorder)};" +
                   $"--tip-back:{Css(d.TipBack)};--canvas:{Css(d.Canvas)};--font:{Settings.CssFont(_appearance.FontChoice)};--scale:{_appearance.Scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }
    }

    private static string Version
    {
        get
        {
            var v = typeof(App).Assembly.GetName().Version;
            return $"{v?.Major}.{v?.Minor}.{v?.Build}";
        }
    }
}

public enum View3DMode { Orbit, Ride, Evening }
