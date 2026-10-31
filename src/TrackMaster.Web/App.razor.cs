using System.Runtime.Versioning;
using Microsoft.AspNetCore.Components;
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

    protected override void OnInitialized()
    {
        _appearance = Settings.LoadAppearance();
        UseAppearance(_appearance);
        _license = new WebLicense(WebLicense.DeviceId(Settings.DeviceSeed()), () => Js.StorageGet(Settings.LicenseKey), text => Js.StorageSet(Settings.LicenseKey, text));
        _session.IsPro = () => _license.IsPro;
        _session.ProRequired += feature => ShowProRequired(feature);
        _session.Message += ShowToast;
        RestoreAutosave();
        _view = new PlanView(_session);
        _view.PropertiesRequested += () => Run("props");
        _view.PolygonChanged += Refresh;
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

    private (double X, double Y) Center() => _view.CenterWorld();

    private void RegisterCommands()
    {
        Add("new", Lang.T("Neu"), AskNewPlan);
        Add("open", Lang.T("Öffnen"), () => _ = OpenFile());
        Add("save", Lang.T("Speichern"), () => { if (_session.RequireSave()) SavePlan(); });
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
        Add("flex", Lang.T("Flexgleis"), () => AskFlex(_session.CurrentFlex));
        Add("toolFlex", Lang.T("Flexgleis ziehen"), () => SetTool(CanvasTool.Flex));
        Add("flexConnect", Lang.T("Lücke schließen"), () => _session.ConnectEnds(_session.CurrentFlex));
        Add("board", Lang.T("Anlagenplatte"), () => { _session.ClearSelection(); _propsOpen = true; });
        Add("modules", Lang.T("Module"), AskModules);
        Add("legs", Lang.T("Schenkel einzeln"), AskLegs);
        Add("levels", Lang.T("Ebenen"), AskLevels);
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
        Add("toolFeeder", Lang.T("Einspeisung"), () => SetTool(CanvasTool.Feeder));
        Add("toolIsolation", Lang.T("Trennstelle"), () => SetTool(CanvasTool.Isolation));
        Add("toolSignal", Lang.T("Signal setzen"), () =>
        {
            if (_session.Tool == CanvasTool.Signal) SetTool(CanvasTool.Select);
            else AskSignalType(_session.SignalType, t => { _session.SignalType = t; SetTool(CanvasTool.Signal); });
        });
        Add("elecCheck", Lang.T("Elektrik prüfen"), ShowElectricCheck);
        Add("addresses", Lang.T("Decoder und Adressen"), ShowAddresses);
        Add("wiring", Lang.T("Kabelplan"), ShowWiring);
        Add("routes", Lang.T("Fahrstraßen"), ShowRoutes);
        Pro("controlExport", Lang.T("Export für Steuerprogramme"), () => Export(TrainRoutes.ToJson(_session.Plan), "application/json", $"{SafeName} – Steuerung.json"));
        Add("view3d", Lang.T("3D-Ansicht"), () => Open3D(View3DMode.Orbit));
        Add("ride", Lang.T("Mitfahren"), () => Open3D(View3DMode.Ride));
        Add("daytime", Lang.T("Tageszeit"), () => Open3D(View3DMode.Evening));
        Pro("csv", Lang.T("Export als CSV"), () => Export(BillOfMaterials.ToCsv(_session.Plan), "text/csv", Lang.F($"{SafeName} – Stückliste.csv")));
        Pro("print", Lang.T("Drucken"), AskPrint);
        Pro("svg", Lang.T("Export als SVG"), () => Export(SvgExport.Build(_session.Plan), "image/svg+xml", $"{SafeName}.svg"));
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
        "toolFeeder" => _session.Tool == CanvasTool.Feeder,
        "toolIsolation" => _session.Tool == CanvasTool.Isolation,
        "toolSignal" => _session.Tool == CanvasTool.Signal,
        _ => false,
    };

    private static readonly HashSet<string> Toggles =
    [
        "grid", "ends", "labels", "snap", "heights", "scenery", "sections", "toolSelect", "toolPan", "toolFlex", "toolTerrain", "toolWater",
        "toolBoard", "toolBuilding", "toolTree", "toolLabel", "toolDimension", "toolFeeder", "toolIsolation", "toolSignal",
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
        CanvasTool.Tree => Lang.T("Vegetation: jeder Klick setzt einen Baum."),
        CanvasTool.Feeder => Lang.T("Einspeisung: auf ein Gleis klicken."),
        CanvasTool.Isolation => Lang.T("Trennstelle: auf eine Gleisverbindung (grüner Punkt) klicken."),
        CanvasTool.Signal => Lang.F($"{Signal.TypeName(_session.SignalType)}: auf einen Punkt an einer Gleisverbindung klicken."),
        CanvasTool.Label => Lang.T("Text: auf die Stelle klicken und den Text eingeben."),
        CanvasTool.Dimension => Lang.T("Maß: zwei Punkte anklicken; Gleisenden und Plattenecken rasten ein."),
        CanvasTool.Pan => Lang.T("Verschieben: Ansicht mit der linken Maustaste ziehen."),
        CanvasTool.Board => _session.Plan.BoardShape == BoardShape.Module
            ? Lang.T("Platte ziehen: Ecke eines Moduls ziehen ändert seine Größe, ein Modul ziehen verschiebt es samt Gleisen. Kanten rasten an Nachbarmodulen ein.")
            : Lang.T("Platte ziehen: eine Ecke der Platte ziehen ändert ihre Größe. Esc beendet."),
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
            if (key == "Escape") { CloseDialog(); return true; }
            return false;
        }
        if (_open3D)
        {
            if (key == "Escape") { Close3D(); return true; }
            return false;
        }
        var k = key.Length == 1 ? key.ToLowerInvariant() : key;
        if (ctrl && k == "s") { Run("save"); return true; }
        if (ctrl && k == "o") { Run("open"); return true; }
        if (ctrl && k == "p") { Run("print"); return true; }
        if (ctrl && k == "f") { Run("search"); return true; }
        if (key == "F1") { Run("help"); return true; }
        if (key == "F7") { Run("check"); return true; }
        if (key == "Enter" && _view.IsDrawingPolygon) { _view.FinishPolygon(); return true; }
        if (key == "Escape" && _session.Tool is not (CanvasTool.Select or CanvasTool.Pan))
        {
            if (_view.IsDrawingPolygon) _view.CancelPolygon();
            else SetTool(CanvasTool.Select);
            return true;
        }
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
        if (!ctrl && _session.Selection.Count > 0 && key is "ArrowLeft" or "ArrowRight" or "ArrowUp" or "ArrowDown")
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

    private async Task OpenFile()
    {
        var result = await Js.PickFile(".json,.trackmaster.json,application/json");
        if (string.IsNullOrEmpty(result)) return;
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

    private void SavePlan()
    {
        var name = $"{SafeName}{PlanSerializer.FileExtension}";
        Js.Download(name, "application/json", PlanSerializer.ToJson(_session.Plan));
        _session.MarkSaved(name);
    }

    /// <summary>Gespeicherten Plan wählen und als Modul rechts ansetzen.</summary>
    private async Task AppendModuleFile()
    {
        var result = await Js.PickFile(".json,.trackmaster.json,application/json");
        if (string.IsNullOrEmpty(result)) return;
        var cut = result.IndexOf('\0');
        var name = cut > 0 ? result[..cut] : Lang.T("Modul");
        var text = cut >= 0 ? result[(cut + 1)..] : result;
        try
        {
            var other = PlanSerializer.FromJson(text, out _);
            var title = other.Name is { Length: > 0 } n && n != Lang.T("Neuer Plan") && n != Lang.T("Gleisplan") ? n : name.Replace(PlanSerializer.FileExtension, "").Replace(".json", "");
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
        ShowIssues(Lang.T("Planprüfung"), issues, null);
    }

    private void ShowElectricCheck()
    {
        var report = Electrics.Analyze(_session.Plan);
        _session.Issues = report.Issues;
        if (!_session.ShowSections) { _session.ShowSections = true; _view.Rebuild(); _session.Commit(); }
        _view.Invalidate();
        ShowIssues(Lang.T("Elektrik prüfen"), report.Issues,
            Lang.F($"{report.Sections.Count} Stromabschnitte, {report.ReverseLoops.Count} Kehrschleifen. {(report.ThreeRail ? Lang.T("Mittelleiter: Kehrschleifen ohne Sonderschaltung möglich.") : Lang.T("Zweileiter-System."))}"));
    }

    private void PickIssue(PlanIssue issue)
    {
        CloseDialog();
        if (issue.PieceIds.Count > 0) _session.Select(issue.PieceIds, add: false);
        _view.Reveal(issue.X, issue.Y);
    }

    private void AskGradient()
    {
        var sel = _session.SelectedPieces;
        var start = sel.Count > 0 ? sel.Min(p => p.ConnectorHeights().Min()) : 0;
        AskGradient(sel.Count, start, (s, p, r) => _session.ApplyGradient(s, p, r));
    }

    private void ShowWiring()
    {
        var text = Electrics.WiringPlan(_session.Plan);
        ShowText(Lang.T("Kabelplan"), text, Lang.T("Als Text speichern"), () =>
        {
            if (_session.RequirePro(Lang.T("Kabelplan"))) Export(text, "text/plain", Lang.F($"{SafeName} – Kabelplan.txt"));
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

    private void RemoveLicense()
    {
        Js.StorageRemove(Settings.LicenseKey);
        _license = new WebLicense(_license.MachineId, () => null, text => Js.StorageSet(Settings.LicenseKey, text));
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
