using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Controls.Ribbon;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using TrackMaster.Core;
using TrackMaster.Licensing;

using LicenseManager = TrackMaster.Licensing.LicenseManager;

namespace TrackMaster.App;

/// <summary>
/// Hauptfenster: eigene Titelleiste im Windows-11-Stil, Ribbon, Gleisbibliothek, Zeichenfläche, Eigenschaften, Statusleiste.
/// Aufbau wie in GleisMatrix Pro, aber ohne XAML.
/// </summary>
public sealed class MainWindow : Window
{
    public PlanSession Session { get; } = new();
    public PlanCanvas Canvas { get; }
    public Ribbon Ribbon { get; } = new();
    public LicenseManager License { get; }

    private readonly LibraryPanel _library;
    private readonly PropertiesPanel _properties;
    private readonly TextBlock _title = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _docTab = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _zoomText = new() { Width = 48, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _statusLeft = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _statusMessage = new() { Margin = new Thickness(10, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _cursorText = new() { Margin = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.TextSecondaryBrush };
    private readonly Border _editionBadge = new() { CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 1, 7, 1), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    private Window? _issuesWindow;
    private readonly DispatcherTimer _messageTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Dictionary<string, ICommand> _commands = new();
    private readonly List<(RibbonToggleButton Button, Func<bool> IsOn)> _toggles = [];
    private readonly List<(ToggleButton Button, Func<bool> IsOn)> _statusToggles = [];
    private readonly Border _root;
    private readonly Border _menuLabel;
    private readonly Canvas _menuLabelHost = new() { IsHitTestVisible = false };
    private RibbonApplicationMenu _appMenu = null!;
    private RibbonApplicationMenuItem _recentMenu = null!;
    private bool _restarting;

    /// <summary>Automatische Sicherung für Absturz oder Stromausfall (%APPDATA%\TrackMaster Pro\Wiederherstellung).</summary>
    public static readonly Recovery Backup = new(Path.Combine(AppFolders.Data, "Wiederherstellung"));
    private readonly RecentFiles _recent = new(Path.Combine(AppFolders.Data, "zuletzt-geoeffnet.txt"));
    private readonly DispatcherTimer _backupTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool _changedSinceBackup;

    public MainWindow(LicenseManager license)
    {
        License = license;
        Session.IsPro = () => License.IsPro;
        Title = "TrackMaster Pro";
        Width = 1600; Height = 940; MinWidth = 1024; MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Theme.WindowBrush;
        Foreground = Theme.TextBrush;
        FontFamily = Theme.Font;
        FontSize = 12 * Theme.FontScale;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Icon = Brand.Logo;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 34, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });

        Canvas = new PlanCanvas(Session);
        _library = new LibraryPanel(Session, Canvas.CenterWorld);
        _properties = new PropertiesPanel(Session, Run);
        DefineCommands();

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
        grid.Children.Add(BuildTitleBar());
        BuildRibbon();
        Grid.SetRow(Ribbon, 1);
        grid.Children.Add(Ribbon);
        _menuLabel = new Border { Child = new TextBlock { Text = "Datei", Foreground = Brushes.White, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        _menuLabelHost.Children.Add(_menuLabel);
        Grid.SetRow(_menuLabelHost, 1);
        grid.Children.Add(_menuLabelHost);
        var work = BuildWorkArea();
        Grid.SetRow(work, 2);
        grid.Children.Add(work);
        var status = BuildStatusBar();
        Grid.SetRow(status, 3);
        grid.Children.Add(status);
        _root = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0xB9, 0xB9, 0xB9)), BorderThickness = new Thickness(1), Child = grid };
        Content = _root;

        _appMenu.LayoutUpdated += (_, _) => PositionMenuLabel();
        StateChanged += (_, _) => _root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        Session.Changed += OnSessionChanged;
        Session.Changed += () => _changedSinceBackup = true;
        _backupTimer.Tick += (_, _) => { if (_changedSinceBackup) BackupNow(); };
        if (!Program.ScreenshotMode) _backupTimer.Start();
        Session.Message += ShowMessage;
        Session.ProRequired += feature => { if (Dialogs.ShowProRequired(this, feature)) Run("license"); };
        Canvas.ViewChanged += () => { _zoomText.Text = $"{Fmt.Num(Canvas.Zoom * 100, 0)} %"; UpdateStatus(); };
        Canvas.LabelRequested += (x, y) => { if (Dialogs.AskText(this, "Beschriftung", "Text") is { } text) Session.AddLabel(text, x, y); };
        Canvas.CursorMoved += p => _cursorText.Text = p is { } w ? $"X {Fmt.Num(w.X, 0)} · Y {Fmt.Num(w.Y, 0)} mm" : "X – · Y –";
        _messageTimer.Tick += (_, _) => { _messageTimer.Stop(); _statusMessage.Text = "Bereit"; };
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;
        Loaded += (_, _) => Canvas.Focus();
        OnSessionChanged();
    }

    // ---------- Befehle ----------

    private void DefineCommands()
    {
        void Add(string id, Action run, Func<bool>? can = null) => _commands[id] = new RelayCommand(run, can);
        bool HasSelection() => Session.Selection.Count > 0;

        Add("new", () =>
        {
            if (!ConfirmDiscard() || Dialogs.AskNewPlan(this, Session.Plan.GaugeId) is not { } setup) return;
            Session.NewPlan(setup);
            Canvas.Fit();
        });
        Add("open", OpenFile);
        Add("save", () => SaveFile(false));
        Add("saveAs", () => SaveFile(true));
        Add("sample", () => { if (ConfirmDiscard()) { Session.LoadSample(); Canvas.Fit(); } });
        Pro("bom", "Stückliste", () => Dialogs.ShowBillOfMaterials(this, Session.Plan, () => Run("csv")));
        // Pro-Funktionen fragen erst die Lizenz ab
        void Pro(string id, string feature, Action run, Func<bool>? can = null) => Add(id, () => { if (Session.RequirePro(feature)) run(); }, can);
        Pro("csv", "Stückliste als CSV", ExportCsv);
        Pro("svg", "SVG-Export", ExportSvg);
        Pro("print", "Drucken", PrintPlan);
        Add("license", ShowLicense);
        Add("check", () => ShowCheck(false));
        Add("elecCheck", () => ShowCheck(true));
        Add("flex", AddFlex);
        Add("toolFlex", () => SetTool(CanvasTool.Flex));
        Add("flexConnect", () => Session.ConnectEnds(Session.CurrentFlex));
        Add("gradient", AskGradient, HasSelection);
        Add("helix", AskHelix);
        Add("tunnel", () => Session.SetTunnel(!Session.SelectedPieces.All(p => p.Tunnel)), HasSelection);
        Add("toolTerrain", () => SetTool(CanvasTool.Terrain));
        Add("toolWater", () => SetTool(CanvasTool.Water));
        Add("toolBuilding", () => SetTool(CanvasTool.Building));
        Add("toolTree", () => SetTool(CanvasTool.Tree));
        Add("toolLabel", () => SetTool(CanvasTool.Label));
        Add("toolDimension", () => SetTool(CanvasTool.Dimension));
        Add("toolFeeder", () => SetTool(CanvasTool.Feeder));
        Add("toolIsolation", () => SetTool(CanvasTool.Isolation));
        foreach (var t in Enum.GetValues<SignalType>())
        {
            var type = t;
            Add("signal" + type, () => { Session.SignalType = type; SetTool(CanvasTool.Signal); });
        }
        Add("addresses", () => Dialogs.ShowAddresses(this, Session, ShowWiring));
        Add("wiring", ShowWiring);
        Add("routes", ShowRoutes);
        Pro("controlExport", "Export für Steuerprogramme", () => ExportTo("Export für Steuerprogramme", "JSON-Datei (*.json)|*.json", $"{SafeName} – Steuerung.json", TrainRoutes.ToJson(Session.Plan)));
        Add("view3d", () => Open3D(View3DMode.Orbit));
        Add("ride", () => Open3D(View3DMode.Ride));
        Add("daytime", () => Open3D(View3DMode.Evening));
        Add("exit", Close);
        Add("undo", Session.Undo, () => Session.History.CanUndo);
        Add("redo", Session.Redo, () => Session.History.CanRedo);
        Add("toolSelect", () => SetTool(CanvasTool.Select));
        Add("toolPan", () => SetTool(CanvasTool.Pan));
        Add("rotL", () => Session.Rotate(-15), HasSelection);
        Add("rotR", () => Session.Rotate(15), HasSelection);
        Add("flip", Session.Flip, HasSelection);
        Add("swap", Session.SwapAnchor, () => Session.Selection.Count == 1);
        Add("duplicate", Session.Duplicate, HasSelection);
        Add("copy", CopyToClipboard, () => HasSelection() || Session.SelectedObject is not null);
        Add("paste", PasteFromClipboard, () => { try { return Clipboard.ContainsText(); } catch { return false; } });
        Add("delete", Session.Delete, () => HasSelection() || Session.SelectedObject is not null);
        Add("selectAll", Session.SelectAll);
        Add("selectNone", Session.ClearSelection);
        Add("nextEnd", () => { if (Session.NextOpenEnd() is { } p) Canvas.Reveal(p.X, p.Y); });
        Add("qStraight", () => Session.Quick(PieceKind.Straight, null, Canvas.CenterWorld()));
        Add("qCurve", () => Session.Quick(PieceKind.Curve, null, Canvas.CenterWorld()));
        Add("qSwitchL", () => Session.Quick(PieceKind.Switch, false, Canvas.CenterWorld()));
        Add("qSwitchR", () => Session.Quick(PieceKind.Switch, true, Canvas.CenterWorld()));
        Add("qBuffer", () => Session.Quick(PieceKind.Buffer, null, Canvas.CenterWorld()));
        Add("qCrossing", () => Session.Quick(PieceKind.Crossing, null, Canvas.CenterWorld()));
        Add("search", _library.FocusSearch);
        Add("board", _properties.FocusBoard);
        Add("fit", Canvas.Fit);
        Add("zoomIn", () => Canvas.ZoomBy(1.25));
        Add("zoomOut", () => Canvas.ZoomBy(0.8));
        Add("help", () => Dialogs.ShowShortcuts(this));
        Add("rotFree", () => { if (Dialogs.AskAngle(this, "Frei drehen", "Dreht die gewählten Gleise um ihren gemeinsamen Mittelpunkt, um einen beliebigen Winkel.", 7.5, "Drehen") is { } a) Session.Rotate(a); }, HasSelection);
        Add("viewRotL", () => Canvas.RotateViewBy(-15));
        Add("viewRotR", () => Canvas.RotateViewBy(15));
        Add("viewReset", () => Canvas.SetViewRotation(0), () => Canvas.ViewRotation != 0);
        Add("viewAngle", () =>
        {
            if (Dialogs.AskAngle(this, "Ansicht drehen", "Dreht nur die Ansicht – der Plan, die Maße und die Stückliste bleiben unverändert. Auch mit Strg + Mausrad (mit Umschalt in 1°-Schritten).",
                    Canvas.ViewRotation, "Ansicht drehen") is { } a) Canvas.SetViewRotation(a);
        });
        Add("appearance", ChangeAppearance);
        Add("about", () => Dialogs.ShowAbout(this, License));
        Add("update", () => _ = CheckForUpdates(manual: true), () => UpdateCheck.ConfiguredUrl(typeof(MainWindow).Assembly) is not null);
    }

    /// <summary>
    /// Fragt die version.json ab. Beim Start still (nur ein Hinweis in der Statuszeile), über das Menü mit Rückmeldung
    /// und dem Angebot, die Download-Seite zu öffnen.
    /// </summary>
    public async Task CheckForUpdates(bool manual)
    {
        if (UpdateCheck.ConfiguredUrl(typeof(MainWindow).Assembly) is not { } url) return;
        var info = await UpdateCheck.FetchAsync(url, TimeSpan.FromSeconds(manual ? 15 : 5));
        var current = typeof(MainWindow).Assembly.GetName().Version;
        if (info is null)
        {
            if (manual) MessageBox.Show(this, "Die Update-Prüfung hat keine Antwort bekommen. Bitte die Internetverbindung prüfen.", AppFolders.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!UpdateCheck.IsNewer(info, current))
        {
            if (manual) MessageBox.Show(this, $"Version {current?.ToString(3)} ist aktuell.", AppFolders.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!manual) { Session.Say(UpdateCheck.Describe(info) + " Menü › Nach Updates suchen."); return; }
        if (info.Url is null) { MessageBox.Show(this, UpdateCheck.Describe(info), AppFolders.Name, MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (MessageBox.Show(this, UpdateCheck.Describe(info) + "\n\nDownload-Seite jetzt öffnen?", AppFolders.Name, MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(info.Url) { UseShellExecute = true });
    }

    /// <summary>Neue Darstellung wählen; das Fenster startet danach neu und übernimmt den offenen Plan (auch ungespeichert).</summary>
    private void ChangeAppearance()
    {
        if (Dialogs.AskAppearance(this, Appearance.Current) is not { } chosen) return;
        Appearance.Save(chosen);
        try
        {
            var temp = Path.Combine(Path.GetTempPath(), $"trackmaster-neustart-{Environment.ProcessId}.json");
            File.WriteAllText(temp, PlanSerializer.ToJson(Session.Plan));
            var args = new List<string> { "--restore", temp };
            if (Session.FilePath is { } path) { args.Add("--path"); args.Add(path); }
            if (Session.Dirty) args.Add("--dirty");
            var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            foreach (var a in args) start.ArgumentList.Add(a);
            System.Diagnostics.Process.Start(start);
            _restarting = true;
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, $"Die neue Darstellung gilt ab dem nächsten Start.\n\n{ex.Message}", "TrackMaster Pro", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>Plan nach einem Neustart wegen neuer Darstellung übernehmen.</summary>
    public void RestorePlan(string tempFile, string? path, bool dirty)
    {
        try
        {
            Session.Restore(File.ReadAllText(tempFile), path, dirty);
            File.Delete(tempFile);
            Canvas.Fit();
            ShowMessage("Neue Darstellung übernommen.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
    }

    /// <summary>Befehl über seine Kennung ausführen (auch aus dem Eigenschaften-Panel).</summary>
    public void Run(string id)
    {
        if (_commands.TryGetValue(id, out var c) && c.CanExecute(null)) c.Execute(null);
    }

    private void SetTool(CanvasTool tool)
    {
        if (Session.Tool != tool) Canvas.CancelPolygon();
        Session.Tool = tool;
        Canvas.Cursor = tool switch { CanvasTool.Pan => Cursors.Hand, CanvasTool.Select => null, _ => Cursors.Cross };
        SyncToggles();
        OnSessionChanged();
        Canvas.InvalidateVisual();
        Canvas.Focus();
    }

    private static string ToolHint(CanvasTool tool) => tool switch
    {
        CanvasTool.Terrain => "Höhenfläche: Eckpunkte anklicken, Doppelklick oder Eingabetaste schließt die Fläche ab. Esc bricht ab.",
        CanvasTool.Water => "Gewässer: Uferpunkte anklicken, Doppelklick oder Eingabetaste schließt ab. Esc bricht ab.",
        CanvasTool.Building => "Gebäude: Rechteck aufziehen oder einmal klicken für ein Standardgebäude. Esc beendet das Werkzeug.",
        CanvasTool.Tree => "Vegetation: Jeder Klick setzt einen Baum. Esc beendet das Werkzeug.",
        CanvasTool.Feeder => "Einspeisung: Auf ein Gleis klicken; ein Klick auf eine Einspeisung entfernt sie wieder. Den Stromkreis stellt man in den Eigenschaften ein.",
        CanvasTool.Isolation => "Trennstelle: Auf eine Gleisverbindung (grüner Punkt) klicken; ein zweiter Klick entfernt sie wieder.",
        CanvasTool.Flex => "Flexgleis ziehen: An einem offenen Gleisende (oder frei) drücken und ziehen – das Gleis biegt sich mit und rastet an Gleisenden ein. Esc beendet das Werkzeug.",
        CanvasTool.Signal => "Signal: Auf ein Gleisende klicken – das Signal steht rechts davor und gilt für Züge, die auf dieses Ende zufahren. Ein Klick auf ein Signal entfernt es wieder.",
        _ => "",
    };

    // ---------- Titelleiste ----------

    private UIElement BuildTitleBar()
    {
        var bar = new Grid { Background = Brushes.White };
        var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new System.Windows.Controls.Image { Source = Brand.LogoSmall, Width = 20, Height = 20, ToolTip = "Über das Programm" });
        var qat = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        qat.Children.Add(QatButton("Save", "Speichern (Strg+S)", "save"));
        qat.Children.Add(QatButton("Undo", "Rückgängig (Strg+Z)", "undo"));
        qat.Children.Add(QatButton("Redo", "Wiederholen (Strg+Y)", "redo"));
        left.Children.Add(qat);
        bar.Children.Add(left);
        bar.Children.Add(_title);

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(CaptionButton("Minimieren", new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,5 H10"), Width = 10, Height = 10 }, () => WindowState = WindowState.Minimized, false));
        right.Children.Add(CaptionButton("Maximieren", new System.Windows.Shapes.Rectangle { Width = 10, Height = 10, RadiusX = 1.5, RadiusY = 1.5 }, () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized, false));
        right.Children.Add(CaptionButton("Schließen", new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,0 L10,10 M10,0 L0,10"), Width = 10, Height = 10 }, Close, true));
        bar.Children.Add(right);
        return bar;
    }

    private Button QatButton(string icon, string tip, string command)
    {
        var b = new Button
        {
            Width = 28, Height = 26, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1),
            Template = Theme.RoundedTemplate(), ToolTip = tip, Command = _commands[command],
            Content = new System.Windows.Controls.Image { Source = Icons.Get(icon), Width = 16, Height = 16 },
        };
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        b.MouseEnter += (_, _) => b.Background = Theme.HoverBrush;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        return b;
    }

    private Button CaptionButton(string name, System.Windows.Shapes.Shape glyph, Action click, bool close)
    {
        var b = new Button { Width = 46, Height = 34, Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = name, Content = glyph, Foreground = Theme.TextBrush };
        glyph.Stroke = Theme.TextBrush;
        glyph.StrokeThickness = 1;
        b.Template = Theme.RoundedTemplate();
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        b.MouseEnter += (_, _) => { b.Background = close ? Theme.DangerBrush : Theme.HoverBrush; glyph.Stroke = close ? Brushes.White : Theme.TextBrush; };
        b.MouseLeave += (_, _) => { b.Background = Brushes.Transparent; glyph.Stroke = Theme.TextBrush; };
        b.Click += (_, _) => click();
        return b;
    }

    private void PositionMenuLabel()
    {
        if (!_appMenu.IsVisible || _appMenu.ActualWidth <= 0) return;
        var origin = _appMenu.TranslatePoint(new Point(0, 0), _menuLabelHost);
        System.Windows.Controls.Canvas.SetLeft(_menuLabel, origin.X);
        System.Windows.Controls.Canvas.SetTop(_menuLabel, origin.Y);
        _menuLabel.Width = _appMenu.ActualWidth;
        _menuLabel.Height = _appMenu.ActualHeight;
    }

    // ---------- Ribbon ----------

    private RibbonButton Large(string label, string icon, string command, string? description = null) => Large(label, icon, _commands[command], description);

    private static RibbonButton Large(string label, string icon, ICommand command, string? description = null) => new()
    {
        Label = label, LargeImageSource = Icons.Get(icon), SmallImageSource = Icons.Get(icon), Command = command,
        ToolTipTitle = label, ToolTipDescription = description,
        ControlSizeDefinition = new RibbonControlSizeDefinition { ImageSize = RibbonImageSize.Large, IsLabelVisible = true },
    };

    private RibbonButton Small(string label, string icon, string command, string? description = null) => Small(label, icon, _commands[command], description);

    private static RibbonButton Small(string label, string icon, ICommand command, string? description = null) => new()
    {
        Label = label, SmallImageSource = Icons.Get(icon), Command = command, ToolTipTitle = label, ToolTipDescription = description,
        ControlSizeDefinition = new RibbonControlSizeDefinition { ImageSize = RibbonImageSize.Small, IsLabelVisible = true },
    };

    /// <summary>Pro-Funktion: im Tooltip gekennzeichnet; ohne Lizenz fragt der Befehl nach der Lizenz.</summary>
    private RibbonButton ProLarge(string label, string icon, string command, string? description = null)
    {
        var b = Large(label, icon, command, description);
        b.ToolTipFooterTitle = "Pro-Funktion";
        b.ToolTipFooterDescription = License.IsPro ? "In dieser Version freigeschaltet." : "Braucht eine Pro-Lizenz (Datei › Lizenz).";
        return b;
    }

    private RibbonButton ProSmall(string label, string icon, string command, string? description = null)
    {
        var b = Small(label, icon, command, description);
        b.ToolTipFooterTitle = "Pro-Funktion";
        b.ToolTipFooterDescription = License.IsPro ? "In dieser Version freigeschaltet." : "Braucht eine Pro-Lizenz (Datei › Lizenz).";
        return b;
    }

    /// <summary>Werkzeug-Schalter: ein Klick wählt das Werkzeug über seinen Befehl.</summary>
    private RibbonToggleButton ToolToggle(string label, string icon, bool large, CanvasTool tool, string command, string description)
    {
        var b = new RibbonToggleButton
        {
            Label = label, SmallImageSource = Icons.Get(icon), LargeImageSource = large ? Icons.Get(icon) : null,
            ToolTipTitle = label, ToolTipDescription = description,
            ControlSizeDefinition = new RibbonControlSizeDefinition { ImageSize = large ? RibbonImageSize.Large : RibbonImageSize.Small, IsLabelVisible = true },
        };
        b.Click += (_, _) =>
        {
            if (Session.Tool == tool) SetTool(CanvasTool.Select);
            else Run(command);
            SyncToggles();
        };
        _toggles.Add((b, () => Session.Tool == tool));
        return b;
    }

    private RibbonToggleButton Toggle(string label, string icon, bool large, Func<bool> isOn, Action<bool> set, string? description = null)
    {
        var b = new RibbonToggleButton
        {
            Label = label, SmallImageSource = Icons.Get(icon), LargeImageSource = large ? Icons.Get(icon) : null,
            ToolTipTitle = label, ToolTipDescription = description, IsChecked = isOn(),
            ControlSizeDefinition = new RibbonControlSizeDefinition { ImageSize = large ? RibbonImageSize.Large : RibbonImageSize.Small, IsLabelVisible = true },
        };
        b.Click += (_, _) => { set(b.IsChecked == true); SyncToggles(); };
        _toggles.Add((b, isOn));
        return b;
    }

    private static RibbonGroup Group(string header, params UIElement[] items)
    {
        var group = new RibbonGroup { Header = header };
        foreach (var item in items) group.Items.Add(item);
        var image = items.Select(i => i switch { RibbonButton b => b.LargeImageSource ?? b.SmallImageSource, RibbonToggleButton t => t.LargeImageSource ?? t.SmallImageSource, _ => null }).FirstOrDefault(i => i is not null);
        if (image is not null) { group.LargeImageSource = image; group.SmallImageSource = image; }
        return group;
    }

    private static RibbonTab Tab(string header, params RibbonGroup[] groups)
    {
        var tab = new RibbonTab { Header = header };
        foreach (var g in groups) tab.Items.Add(g);
        return tab;
    }

    private void BuildRibbon()
    {
        Ribbon.Background = new SolidColorBrush(Color.FromRgb(0xFB, 0xFB, 0xFB));
        Ribbon.BorderBrush = Theme.BorderStrongBrush;
        Ribbon.MouseOverBackground = Theme.HoverBrush;
        Ribbon.MouseOverBorderBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC));
        Ribbon.PressedBackground = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC));
        Ribbon.PressedBorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8));
        Ribbon.CheckedBackground = Theme.AccentLightBrush;
        Ribbon.CheckedBorderBrush = Theme.AccentBorderBrush;
        Ribbon.FocusedBackground = Theme.HoverBrush;
        Ribbon.FocusedBorderBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC));

        // Datei-Menü (blaue Schaltfläche links)
        _appMenu = new RibbonApplicationMenu { Background = Theme.AccentBrush, BorderBrush = new SolidColorBrush(Theme.AccentDark) };
        RibbonApplicationMenuItem Item(string header, string icon, string command)
            => new() { Header = header, ImageSource = Icons.Get(icon), Command = _commands[command] };
        _appMenu.Items.Add(Item("Neu", "New", "new"));
        _appMenu.Items.Add(Item("Öffnen…", "Open", "open"));
        _recentMenu = new RibbonApplicationMenuItem { Header = "Zuletzt geöffnet", ImageSource = Icons.Get("Open") };
        _appMenu.Items.Add(_recentMenu);
        RefreshRecent();
        _appMenu.Items.Add(Item("Speichern", "Save", "save"));
        _appMenu.Items.Add(Item("Speichern unter…", "SaveAs", "saveAs"));
        _appMenu.Items.Add(new RibbonSeparator());
        _appMenu.Items.Add(Item("Stückliste…", "List", "bom"));
        _appMenu.Items.Add(Item("Stückliste als CSV…", "Csv", "csv"));
        _appMenu.Items.Add(Item("Als SVG exportieren (1:1)…", "Image", "svg"));
        _appMenu.Items.Add(Item("Drucken…", "Print", "print"));
        _appMenu.Items.Add(new RibbonSeparator());
        _appMenu.Items.Add(Item("Beispielplan laden", "Template", "sample"));
        _appMenu.Items.Add(Item("Lizenz…", "Shield", "license"));
        _appMenu.Items.Add(Item("Darstellung…", "Palette", "appearance"));
        if (UpdateCheck.ConfiguredUrl(typeof(MainWindow).Assembly) is not null) _appMenu.Items.Add(Item("Nach Updates suchen…", "Update", "update"));
        _appMenu.Items.Add(Item("Über das Programm…", "Info", "about"));
        _appMenu.FooterPaneContent = new RibbonButton { Label = "Beenden", SmallImageSource = Icons.Get("Exit"), Command = _commands["exit"], HorizontalAlignment = HorizontalAlignment.Right };
        Ribbon.ApplicationMenu = _appMenu;

        Ribbon.Items.Add(Tab("Start",
            Group("Verlauf", Large("Rückgängig", "Undo", "undo", "Letzten Schritt zurücknehmen (Strg+Z)."), Large("Wiederholen", "Redo", "redo", "Zurückgenommenen Schritt wiederholen (Strg+Y).")),
            Group("Werkzeuge",
                Toggle("Auswählen", "Select", true, () => Session.Tool == CanvasTool.Select, _ => SetTool(CanvasTool.Select), "Gleise und Objekte wählen und verschieben (Esc)."),
                Toggle("Verschieben", "Hand", true, () => Session.Tool == CanvasTool.Pan, on => SetTool(on ? CanvasTool.Pan : CanvasTool.Select), "Ansicht mit der linken Maustaste verschieben (auch: Leertaste halten).")),
            Group("Bearbeiten",
                Small("15° links drehen", "RotateLeft", "rotL", "Umschalt+R"), Small("15° rechts drehen", "RotateRight", "rotR", "R"), Small("Frei drehen…", "RotateFree", "rotFree", "Auswahl um einen beliebigen Winkel drehen."), Small("Spiegeln", "Mirror", "flip", "Bögen, Weichen und Kreuzungen spiegeln (F)."),
                Small("Anschluss wechseln", "Swap", "swap", "Gleis mit dem nächsten Anschluss an dieselbe Stelle legen (A)."), Small("Duplizieren", "Copy", "duplicate", "Strg+D"), Small("Löschen", "Delete", "delete", "Entf"),
                Small("Kopieren", "Copy", "copy", "Gewählte Gleise samt Einspeisungen, Trennstellen und Signalen kopieren (Strg+C) – auch in einen anderen Plan."),
                Small("Einfügen", "Paste", "paste", "Kopiertes an der Mausposition einfügen (Strg+V).")),
            Group("Beschriften",
                ToolToggle("Text", "Text", true, CanvasTool.Label, "toolLabel", "Beschriftung setzen: auf die Stelle klicken und den Text eingeben."),
                ToolToggle("Maß", "Ruler", true, CanvasTool.Dimension, "toolDimension", "Maßlinie: zwei Punkte anklicken; Gleisenden und Plattenecken rasten ein.")),
            Group("Auswahl", Small("Alles auswählen", "SelectAll", "selectAll", "Strg+A"), Small("Auswahl aufheben", "SelectNone", "selectNone", "Esc"), Small("Nächstes Gleisende", "Connector", "nextEnd", "Springt zum nächsten offenen Gleisende (E).")),
            Group("Prüfen", ProLarge("Stückliste", "List", "bom", "Alle Gleise nach Artikel gezählt, mit Zubehör."),
                Large("Kollisions­prüfung", "Shield", "check", "Prüft Gleisabstände, Durchfahrtshöhen, Plattenrand, Steigungen und Gelände (F7).")),
            Group("Datei", ProSmall("Drucken", "Print", "print", "Plan auf eine Seite drucken (Strg+P)."), Small("Lizenz", "Shield", "license", "Basis oder Pro, Lizenz einspielen."))));

        Ribbon.Items.Add(Tab("Gleise",
            Group("Bibliothek", Large("Gleis suchen", "Search", "search", "Springt in das Suchfeld der Gleisbibliothek.")),
            Group("Verlegen",
                Large("Gerade", "Straight", "qStraight", "Gerade des gewählten Systems am aktiven Gleisende ansetzen."), Large("Bogen", "Curve", "qCurve"),
                Large("Weiche links", "SwitchLeft", "qSwitchL"), Large("Weiche rechts", "SwitchRight", "qSwitchR"),
                Small("Prellbock", "Buffer", "qBuffer"), Small("Kreuzung", "Crossing", "qCrossing"), Small("Flexgleis…", "Flex", "flex", "Gerade oder Bogen mit frei gewählten Maßen.")),
            Group("Flexgleis",
                Toggle("Flexgleis ziehen", "Flex", true, () => Session.Tool == CanvasTool.Flex, on => SetTool(on ? CanvasTool.Flex : CanvasTool.Select),
                    "Am Gleisende ansetzen und ziehen, das Flexgleis biegt sich mit. Bei verlegten Flexgleisen ein Ende anfassen und ziehen, um sie nachzuformen."),
                Large("Lücke schließen", "Connector", "flexConnect", "Verbindet das aktive Gleisende mit dem gewählten (oder nächsten) Gleisende durch ein passend gebogenes Flexgleis.")),
            Group("Höhe",
                Large("Steigung…", "Slope", "gradient", "Gleichmäßige Steigung über die gewählten Gleise legen."),
                Large("Gleis­wendel…", "Helix", "helix", "Wendel aus Bögen mit mehreren Windungen bauen."),
                Small("Tunnel", "Tunnel", "tunnel", "Gewählte Gleise als Tunnel markieren oder die Markierung aufheben."))));

        Ribbon.Items.Add(Tab("Gelände",
            Group("Grundplatte", Large("Anlagen­platte", "Board", "board", "Breite und Tiefe der Anlagenplatte einstellen.")),
            Group("Landschaft",
                ToolToggle("Gelände formen", "Terrain", true, CanvasTool.Terrain, "toolTerrain", "Höhenfläche (Hügel, Berg, Plateau) als Vieleck zeichnen."),
                Large("Tunnel", "Tunnel", "tunnel", "Gewählte Gleise als Tunnel markieren."),
                ToolToggle("Gewässer", "Water", false, CanvasTool.Water, "toolWater", "See oder Fluss als Vieleck zeichnen."),
                ToolToggle("Gebäude", "Building", false, CanvasTool.Building, "toolBuilding", "Gebäude als Rechteck aufziehen."),
                ToolToggle("Vegetation", "Tree", false, CanvasTool.Tree, "toolTree", "Bäume mit Klicks setzen.")),
            Group("Anzeigen",
                Toggle("Gelände zeigen", "Terrain", false, () => Session.ShowScenery, on => { Session.ShowScenery = on; Canvas.Rebuild(); }))));

        var signalMenu = new RibbonMenuButton
        {
            Label = "Signal setzen", LargeImageSource = Icons.Get("Signal"), SmallImageSource = Icons.Get("Signal"), ToolTipTitle = "Signal setzen",
            ToolTipDescription = "Signalart wählen, dann auf ein Gleisende klicken.",
            ControlSizeDefinition = new RibbonControlSizeDefinition { ImageSize = RibbonImageSize.Large, IsLabelVisible = true },
        };
        foreach (var t in Enum.GetValues<SignalType>())
            signalMenu.Items.Add(new RibbonMenuItem { Header = Signal.TypeName(t), ImageSource = PlanScene.SignalImage(t), ToolTip = SignalFace.For(t).Description, Command = _commands["signal" + t] });
        Ribbon.Items.Add(Tab("Elektrik",
            Group("Stromversorgung",
                ToolToggle("Einspeisung", "Bolt", true, CanvasTool.Feeder, "toolFeeder", "Gleis an einen Stromkreis/Booster anschließen."),
                ToolToggle("Trennstelle", "Isolate", false, CanvasTool.Isolation, "toolIsolation", "Isolierte Schienenverbinder an einer Gleisverbindung setzen."),
                Toggle("Stromkreise zeigen", "Grid", false, () => Session.ShowSections, on => { Session.ShowSections = on; Canvas.Rebuild(); }),
                Small("Elektrik prüfen", "Shield", "elecCheck", "Abschnitte ohne Einspeisung, Kehrschleifen, doppelte Adressen.")),
            Group("Digital",
                Large("Decoder", "Chip", "addresses", "Digitaladressen für Weichen und Signale vergeben."),
                signalMenu,
                Small("Kabelplan", "Wire", "wiring", "Abschnitte, Einspeisungen, Trennstellen und Decoderbelegung als Liste."),
                Small("Fahrstraßen", "Route", "routes", "Alle Wege von Signal zu Signal mit den nötigen Weichenstellungen."),
                Small("Export Steuerung", "Export", "controlExport", "Weichen, Signale, Adressen, Abschnitte und Fahrstraßen als Datei für Steuerprogramme."))));

        Ribbon.Items.Add(Tab("Ansicht",
            Group("Zoom", Large("Ganze Anlage", "Fit", "fit", "Taste 0"), Small("Vergrößern", "ZoomIn", "zoomIn", "+"), Small("Verkleinern", "ZoomOut", "zoomOut", "-")),
            Group("Anzeigen",
                Toggle("Raster", "Grid", false, () => Session.ShowGrid, on => { Session.ShowGrid = on; Canvas.InvalidateVisual(); }),
                Toggle("Gleisenden", "Connector", false, () => Session.ShowEnds, on => { Session.ShowEnds = on; Canvas.InvalidateVisual(); }),
                Toggle("Artikelnummern", "Hash", false, () => Session.ShowLabels, on => { Session.ShowLabels = on; Canvas.InvalidateVisual(); }),
                Toggle("Höhen", "Slope", false, () => Session.ShowHeights, on => { Session.ShowHeights = on; Canvas.InvalidateVisual(); }),
                Toggle("Gelände", "Terrain", false, () => Session.ShowScenery, on => { Session.ShowScenery = on; Canvas.Rebuild(); })),
            Group("Drehen",
                Large("Ansicht drehen…", "ViewRotate", "viewAngle", "Die Ansicht frei drehen – der Plan bleibt unverändert. Auch mit Strg + Mausrad."),
                Small("15° links", "RotateLeft", "viewRotL", "Ansicht 15° gegen den Uhrzeigersinn drehen."),
                Small("15° rechts", "RotateRight", "viewRotR", "Ansicht 15° im Uhrzeigersinn drehen."),
                Small("Gerade ausrichten", "North", "viewReset", "Ansicht wieder auf 0° stellen (oder auf den Kompass klicken).")),
            Group("Darstellung", Large("Design und Schrift", "Palette", "appearance", "Farbschema, Schriftart und Schriftgröße wählen.")),
            Group("Hilfe", Large("Tasten­kürzel", "Help", "help", "Alle Maus- und Tastaturbefehle (F1)."), Large("Über", "Info", "about", "Logo, Version und Ausbaustufe."))));

        Ribbon.Items.Add(Tab("3D",
            Group("3D", Large("3D-Ansicht", "Cube", "view3d", "Die Anlage räumlich ansehen: ziehen dreht, Mausrad zoomt."),
                Large("Mitfahren", "Train", "ride", "Im Führerstand über die Gleise fahren, ab dem gewählten Gleis."),
                Large("Tageszeit", "Sun", "daytime", "3D-Ansicht mit Licht nach Uhrzeit, vom Morgen bis zur Nacht."))));
    }

    private void SyncToggles()
    {
        foreach (var (b, on) in _toggles) b.IsChecked = on();
        foreach (var (b, on) in _statusToggles) StyleStatusToggle(b, on());
    }

    // ---------- Arbeitsbereich ----------

    private UIElement BuildWorkArea()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(276), MinWidth = 220 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 320 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300), MinWidth = 240 });

        grid.Children.Add(_library);
        var leftSplit = new GridSplitter { Width = 1, Background = Theme.BorderStrongBrush, HorizontalAlignment = HorizontalAlignment.Center, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        Grid.SetColumn(leftSplit, 1);
        grid.Children.Add(leftSplit);

        var center = new DockPanel();
        var tabs = new Border { Background = Theme.WindowBrush, BorderBrush = Theme.BorderStrongBrush, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 6, 8, 0) };
        var docTab = new Border
        {
            Background = Brushes.White, BorderBrush = Theme.BorderStrongBrush, BorderThickness = new Thickness(1, 1, 1, 0), CornerRadius = new CornerRadius(6, 6, 0, 0),
            Padding = new Thickness(12, 0, 12, 0), Height = 30, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, -1),
        };
        var tabContent = new StackPanel { Orientation = Orientation.Horizontal };
        tabContent.Children.Add(new System.Windows.Controls.Image { Source = Brand.LogoSmall, Width = 14, Height = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        tabContent.Children.Add(_docTab);
        docTab.Child = tabContent;
        tabs.Child = docTab;
        DockPanel.SetDock(tabs, Dock.Top);
        center.Children.Add(tabs);

        var stage = new Grid();
        stage.Children.Add(Canvas);
        var hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)), BorderBrush = Theme.BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, MaxWidth = 560, IsHitTestVisible = false,
        };
        var hintRow = new DockPanel();
        var info = new System.Windows.Controls.Image { Source = Icons.Get("Info", Theme.Accent), Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top };
        hintRow.Children.Add(info);
        hintRow.Children.Add(_hint);
        hint.Child = hintRow;
        stage.Children.Add(hint);

        var zoom = new Border
        {
            Background = Brushes.White, BorderBrush = Theme.BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(3),
            Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
        };
        var zoomRow = new StackPanel { Orientation = Orientation.Horizontal };
        zoomRow.Children.Add(IconButton("Minus", "Verkleinern", "zoomOut"));
        zoomRow.Children.Add(_zoomText);
        zoomRow.Children.Add(IconButton("Plus", "Vergrößern", "zoomIn"));
        zoomRow.Children.Add(new Border { Width = 1, Height = 18, Background = Theme.BorderBrush, Margin = new Thickness(2, 0, 2, 0) });
        zoomRow.Children.Add(IconButton("Fit", "Ganze Anlage (0)", "fit"));
        zoom.Child = zoomRow;
        stage.Children.Add(zoom);
        center.Children.Add(stage);
        Grid.SetColumn(center, 2);
        grid.Children.Add(center);

        var rightSplit = new GridSplitter { Width = 1, Background = Theme.BorderStrongBrush, HorizontalAlignment = HorizontalAlignment.Center, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        Grid.SetColumn(rightSplit, 3);
        grid.Children.Add(rightSplit);
        Grid.SetColumn(_properties, 4);
        grid.Children.Add(_properties);
        return grid;
    }

    private Button IconButton(string icon, string tip, string command)
    {
        var b = new Button
        {
            Width = 28, Height = 28, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1),
            Template = Theme.RoundedTemplate(), ToolTip = tip, Command = _commands[command],
            Content = new System.Windows.Controls.Image { Source = Icons.Get(icon), Width = 14, Height = 14 },
        };
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        b.MouseEnter += (_, _) => b.Background = Theme.HoverBrush;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        return b;
    }

    // ---------- Statusleiste ----------

    private UIElement BuildStatusBar()
    {
        var dock = new DockPanel();
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(_cursorText);
        right.Children.Add(StatusToggle("Fang", "Gleise rasten beim Loslassen an freien Enden ein.", () => Session.SnapEnabled, on => Session.SnapEnabled = on));
        right.Children.Add(StatusToggle("Raster", "Raster auf der Anlagenplatte zeigen.", () => Session.ShowGrid, on => { Session.ShowGrid = on; Canvas.InvalidateVisual(); }));
        right.Children.Add(new Border { Width = 10 });
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        _statusMessage.Text = "Bereit";
        _editionBadge.Background = License.IsPro ? Theme.AccentBrush : Theme.HoverBrush;
        _editionBadge.Child = Theme.Label(License.EditionName, 11, true, License.IsPro ? Brushes.White : Theme.TextBrush);
        _editionBadge.ToolTip = License.Summary;
        _editionBadge.Margin = new Thickness(8, 0, 4, 0);
        DockPanel.SetDock(_editionBadge, Dock.Left);
        dock.Children.Add(_editionBadge);
        dock.Children.Add(_statusMessage);
        dock.Children.Add(_statusLeft);
        return new Border { Background = Theme.WindowBrush, BorderBrush = Theme.BorderStrongBrush, BorderThickness = new Thickness(0, 1, 0, 0), Child = dock };
    }

    private ToggleButton StatusToggle(string label, string tip, Func<bool> isOn, Action<bool> set)
    {
        var b = new ToggleButton { Content = label, Height = 20, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 6, 0), FontSize = 11, ToolTip = tip, BorderThickness = new Thickness(1), Template = Theme.RoundedTemplate(), VerticalAlignment = VerticalAlignment.Center };
        b.Click += (_, _) => { set(b.IsChecked == true); SyncToggles(); };
        _statusToggles.Add((b, isOn));
        StyleStatusToggle(b, isOn());
        return b;
    }

    private static void StyleStatusToggle(ToggleButton b, bool on)
    {
        b.IsChecked = on;
        b.Background = on ? Theme.AccentLightBrush : Brushes.Transparent;
        b.BorderBrush = on ? Theme.AccentBorderBrush : Theme.ControlBorderBrush;
    }

    private void UpdateStatus()
    {
        var plan = Session.Plan;
        var s = plan.Statistics(Session.Connections);
        var g = plan.Gauge;
        _statusLeft.Children.Clear();
        foreach (var text in new[]
                 {
                     $"Spur {g.Id} · {g.Scale}", $"Gleise: {s.Pieces}", $"Weichen: {s.Switches}",
                     $"Gleislänge: {Fmt.Num(s.TrackLengthMm / 1000, 2)} m", $"Offene Enden: {s.OpenEnds}", $"Zoom {Fmt.Num(Canvas.Zoom * 100, 0)} %",
                 })
            _statusLeft.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.TextSecondaryBrush });
    }

    private void ShowMessage(string text)
    {
        _statusMessage.Text = text;
        _messageTimer.Stop();
        _messageTimer.Start();
    }

    // ---------- Aktualisieren ----------

    private void OnSessionChanged()
    {
        var plan = Session.Plan;
        var file = Session.FilePath is { } path ? Path.GetFileName(path) : plan.Name;
        _title.Text = $"{file}{(Session.Dirty ? " *" : "")} – TrackMaster Pro{(License.IsPro ? "" : " Basis")}";
        Title = _title.Text;
        _docTab.Text = $"{plan.Name} · Spur {plan.GaugeId}";
        _hint.Text = Session.Tool is not (CanvasTool.Select or CanvasTool.Pan)
            ? ToolHint(Session.Tool)
            : plan.Pieces.Count == 0
            ? "Leerer Plan: Klicke ein Gleis in der Bibliothek an oder ziehe es auf die Platte."
            : Session.ActiveConnector is not null
                ? "Aktives Gleisende (blau): Ein Klick in der Bibliothek setzt das nächste Gleis dort an. A wechselt den Anschluss, F spiegelt."
                : Session.Selection.Count > 0
                    ? "Ziehen verschiebt, R dreht, F spiegelt, Entf löscht. Orangefarbene Punkte sind offene Gleisenden."
                    : "Klicke ein orangefarbenes Gleisende an, um dort weiterzubauen. F1 zeigt alle Tastenkürzel.";
        UpdateStatus();
        SyncToggles();
        CommandManager.InvalidateRequerySuggested();
    }

    // ---------- Zwischenablage ----------

    private void CopyToClipboard()
    {
        if (Session.CopySelection() is not { } json) return;
        try { Clipboard.SetText(json); }
        catch (System.Runtime.InteropServices.COMException) { Session.Say("Die Zwischenablage ist gerade belegt – bitte noch einmal kopieren."); }
    }

    private void PasteFromClipboard()
    {
        string text;
        try { text = Clipboard.GetText(); }
        catch (System.Runtime.InteropServices.COMException) { return; }
        (double X, double Y)? at = null;
        if (Canvas.IsMouseOver) { var w = Canvas.ToWorld(Mouse.GetPosition(Canvas)); at = (w.X, w.Y); }
        Session.Paste(text, at);
    }

    // ---------- Tastatur ----------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (ctrl && e.Key == Key.S) { Run("save"); e.Handled = true; return; }
        if (ctrl && e.Key == Key.O) { Run("open"); e.Handled = true; return; }
        if (ctrl && e.Key == Key.N) { Run("new"); e.Handled = true; return; }
        if (ctrl && e.Key == Key.P) { Run("print"); e.Handled = true; return; }
        if (e.Key == Key.F1) { Run("help"); e.Handled = true; return; }
        if (e.Key == Key.F7) { Run("check"); e.Handled = true; return; }
        if (Keyboard.FocusedElement is TextBox or ComboBox or ComboBoxItem) return;
        if (e.Key == Key.Enter && Canvas.IsDrawingPolygon) { Canvas.FinishPolygon(); e.Handled = true; return; }
        if (e.Key == Key.Escape && Session.Tool is not (CanvasTool.Select or CanvasTool.Pan))
        {
            if (Canvas.IsDrawingPolygon) Canvas.CancelPolygon();
            else SetTool(CanvasTool.Select);
            e.Handled = true;
            return;
        }

        string? cmd = (ctrl, e.Key) switch
        {
            (true, Key.Z) => shift ? "redo" : "undo",
            (true, Key.Y) => "redo",
            (true, Key.A) => "selectAll",
            (true, Key.D) => "duplicate",
            (true, Key.C) => "copy",
            (true, Key.V) => "paste",
            (false, Key.Delete) or (false, Key.Back) => "delete",
            (false, Key.R) => shift ? "rotL" : "rotR",
            (false, Key.F) => "flip",
            (false, Key.A) => "swap",
            (false, Key.E) => "nextEnd",
            (false, Key.Escape) => "selectNone",
            (false, Key.Add) or (false, Key.OemPlus) => "zoomIn",
            (false, Key.Subtract) or (false, Key.OemMinus) => "zoomOut",
            (false, Key.D0) or (false, Key.NumPad0) => "fit",
            _ => null,
        };
        if (cmd is not null)
        {
            Run(cmd);
            e.Handled = true;
            return;
        }
        if (!ctrl && Session.Selection.Count > 0 && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var step = shift ? 10 : 1;
            var dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
            var dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
            Session.Nudge(dx, dy, record: !e.IsRepeat);
            e.Handled = true;
        }
    }

    // ---------- Dateien ----------

    private const string FileFilter = "TrackMaster-Plan (*.trackmaster.json)|*.trackmaster.json|JSON-Dateien (*.json)|*.json|Alle Dateien (*.*)|*.*";

    private string SafeName => string.Join("", Session.Plan.Name.Split(Path.GetInvalidFileNameChars())).Trim() is { Length: > 0 } n ? n : "Gleisplan";

    private bool ConfirmDiscard()
    {
        if (!Session.Dirty) return true;
        if (!Session.IsPro())
            return MessageBox.Show(this, $"„{Session.Plan.Name}“ wurde geändert. Speichern ist in der Basis-Version nicht enthalten (Datei › Lizenz).\n\nÄnderungen verwerfen?",
                "TrackMaster Pro", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
        var answer = MessageBox.Show(this, $"Änderungen an „{Session.Plan.Name}“ speichern?", "TrackMaster Pro", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) return false;
        if (answer == MessageBoxResult.Yes) return SaveFile(false);
        return true;
    }

    private void OpenFile()
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog { Filter = FileFilter, Title = "Gleisplan öffnen" };
        if (dlg.ShowDialog(this) != true) return;
        OpenPath(dlg.FileName);
    }

    public void OpenPath(string path)
    {
        try
        {
            Session.Open(path);
            Canvas.Fit();
            _recent.Add(path);
            RefreshRecent();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"„{Path.GetFileName(path)}“ lässt sich nicht öffnen.\n\n{ex.Message}", "TrackMaster Pro", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool SaveFile(bool askForName)
    {
        if (!Session.RequireSave()) return false;
        var path = Session.FilePath;
        if (askForName || path is null)
        {
            var dlg = new SaveFileDialog { Filter = FileFilter, Title = "Gleisplan speichern", FileName = SafeName + PlanSerializer.FileExtension, AddExtension = false };
            if (dlg.ShowDialog(this) != true) return false;
            path = dlg.FileName;
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) path += PlanSerializer.FileExtension;
        }
        try
        {
            Session.Save(path);
            Backup.Clear();
            _changedSinceBackup = false;
            _recent.Add(path);
            RefreshRecent();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Speichern nicht möglich.\n\n{ex.Message}", "TrackMaster Pro", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void ExportTo(string title, string filter, string fileName, string content)
    {
        var dlg = new SaveFileDialog { Filter = filter, Title = title, FileName = fileName };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, content, new System.Text.UTF8Encoding(false));
            ShowMessage($"Gespeichert: {Path.GetFileName(dlg.FileName)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Speichern nicht möglich.\n\n{ex.Message}", "TrackMaster Pro", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportCsv() => ExportTo("Stückliste speichern", "CSV-Datei (*.csv)|*.csv", $"{SafeName} – Stückliste.csv", BillOfMaterials.ToCsv(Session.Plan));

    private void ExportSvg() => ExportTo("Gleisplan als SVG speichern", "SVG-Grafik (*.svg)|*.svg", $"{SafeName}.svg", SvgExport.Build(Session.Plan));

    // ---------- Prüfen, Höhe, Elektrik, 3D, Drucken, Lizenz ----------

    private void ShowCheck(bool electrics)
    {
        _issuesWindow?.Close();
        IReadOnlyList<PlanIssue> Analyze()
        {
            var issues = electrics ? Electrics.Analyze(Session.Plan).Issues : PlanCheck.Run(Session.Plan);
            Session.Issues = issues;
            Canvas.InvalidateVisual();
            return issues;
        }
        _issuesWindow = Dialogs.ShowIssues(this, electrics ? "Elektrik prüfen" : "Kollisions- und Planprüfung", Analyze, issue =>
        {
            if (issue.PieceIds.Count > 0) Session.Select(issue.PieceIds, add: false);
            Canvas.Reveal(issue.X, issue.Y);
            Activate();
        }, electrics && Electrics.IsThreeRail(Session.Plan) ? "Mittelleiter-Gleis (Märklin): Kehrschleifen brauchen hier keine Sonderschaltung." : null);
        _issuesWindow.Closed += (_, _) => { Session.Issues = []; Canvas.InvalidateVisual(); _issuesWindow = null; };
    }

    private void AddFlex()
    {
        var gauge = Session.Plan.Gauge;
        if (Dialogs.AskFlex(this, gauge, Session.CurrentFlex) is { } key) Session.AttachFlex(key, Canvas.CenterWorld());
    }

    private void AskGradient()
    {
        var sel = Session.SelectedPieces;
        var start = sel.Count > 0 ? sel.Min(p => p.ConnectorHeights().Min()) : 0;
        if (Dialogs.AskGradient(this, sel.Count, start) is { } g) Session.ApplyGradient(g.StartHeight, g.Percent, g.Reverse);
    }

    /// <summary>Öffnet für Bildschirmfotos (Handbuch) einen Dialog: helix, holzliste, bom, license, check, new, wiring.</summary>
    public void OpenForScreenshot(string name)
    {
        if (name == "druckart") { Dialogs.AskPrintMode(this, 60, "Seiten 200 × 287 mm"); return; }
        if (name == "bauvorlage") { ShowTilePreview(); return; }
        if (name != "holzliste") { Run(name == "helix" ? "helix" : name); return; }
        var curves = Session.CurrentSystem.Pieces.Where(p => p.Kind == PieceKind.Curve && 360 % p.Angle < 0.01).OrderBy(p => p.Radius).ToList();
        if (curves.Count == 0) return;
        var inner = curves[Math.Min(1, curves.Count - 1)];
        var gauge = Session.Plan.Gauge;
        var spacing = Helix.DefaultSpacing(gauge);
        var spec = new HelixSpec(inner.Key, 3, Math.Round(gauge.TrackWidth * 6.5), true, HelixShape.Rund,
            Tracks: 2, Spacing: spacing, OuterCurveKey: Helix.OuterCurve(curves, inner, spacing, HelixShape.Rund)?.Key);
        Dialogs.ShowHelixMaterial(this, Helix.Material(spec, gauge), _ => { });
    }

    /// <summary>Für das Handbuch: eine Seite der Bauvorlage (A4) als Bild in einem Fenster.</summary>
    private void ShowTilePreview()
    {
        const double mm = 96 / 25.4;
        double pageW = 210 * mm, pageH = 297 * mm;
        var tiles = PrintTiles.Layout(Session.Plan.Bounds(), 200, 273, 10);
        var tile = tiles.OrderBy(t => Session.Plan.Pieces.Count(p => p.X > t.X0 && p.X < t.X1 && p.Y > t.Y0 && p.Y < t.Y1)).Last();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)pageW, (int)pageH, 96, 96, PixelFormats.Pbgra32);
        var page = new DrawingVisual();
        using (var dc = page.RenderOpen()) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageW, pageH));
        bitmap.Render(page);
        bitmap.Render(PlanPrint.RenderTile(Session.Plan, tile, tiles.Max(t => t.Row) + 1, tiles.Max(t => t.Column) + 1, pageW, pageH, License.IsPro, 10));
        new Window { Title = "Bauvorlage", SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, Owner = this,
                     Content = new Image { Source = bitmap, Width = pageW, Height = pageH } }.Show();
    }

    private void AskHelix()
    {
        var gauge = Session.Plan.Gauge;
        var curves = Session.CurrentSystem.Pieces.Where(p => p.Kind == PieceKind.Curve && 360 % p.Angle < 0.01).ToList();
        if (curves.Count == 0) curves = gauge.Systems.SelectMany(x => x.Pieces).Where(p => p.Kind == PieceKind.Curve && 360 % p.Angle < 0.01).ToList();
        if (curves.Count == 0)
        {
            MessageBox.Show(this, "Für diese Spur gibt es keine Bogengleise im Katalog. Die Gleiswendel lässt sich hier mit Flexgleis bauen.", "TrackMaster Pro", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var straights = Session.CurrentSystem.Pieces.Where(p => p.Kind == PieceKind.Straight && p.Length >= gauge.TrackWidth * 3).ToList();
        if (straights.Count == 0) straights = gauge.Systems.SelectMany(x => x.Pieces).Where(p => p.Kind == PieceKind.Straight && p.Length >= gauge.TrackWidth * 3).ToList();
        if (Dialogs.AskHelix(this, gauge, curves, straights, Session.ActiveConnector is not null) is { } h
            && Session.BuildHelix(h, Canvas.CenterWorld()) is { } material)
        {
            Canvas.Fit();
            // Die Holzliste gehört wie die Gleis-Stückliste zur Pro-Version
            if (Session.RequirePro("Holzliste der Gleiswendel"))
                Dialogs.ShowHelixMaterial(this, material, csv => ExportTo("Holzliste speichern", "CSV-Datei (*.csv)|*.csv", $"{SafeName} – Holzliste Gleiswendel.csv", csv));
        }
    }

    private void ShowWiring()
    {
        var text = Electrics.WiringPlan(Session.Plan);
        Dialogs.ShowText(this, "Kabelplan", text, "Als Textdatei speichern…",
            () => { if (Session.RequirePro("Kabelplan speichern")) ExportTo("Kabelplan speichern", "Textdatei (*.txt)|*.txt", $"{SafeName} – Kabelplan.txt", text); });
    }

    private void ShowRoutes()
    {
        var routes = TrainRoutes.Find(Session.Plan);
        Dialogs.ShowText(this, "Fahrstraßen", TrainRoutes.ToText(Session.Plan, routes), "Als CSV speichern…",
            () => { if (Session.RequirePro("Fahrstraßen speichern")) ExportTo("Fahrstraßen speichern", "CSV-Datei (*.csv)|*.csv", $"{SafeName} – Fahrstraßen.csv", TrainRoutes.ToCsv(routes)); });
    }

    private void Open3D(View3DMode mode)
    {
        var window = new View3DWindow(Session.Plan, Session.SelectedPieces.FirstOrDefault()?.Id, mode) { Owner = this };
        window.Show();
    }

    private void PrintPlan()
    {
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true) return;
        // Bauvorlage: bedruckbarer Bereich abzüglich Kopf- und Fußzeile (je 12 mm) und 5 mm Rand seitlich
        const double mm = 96 / 25.4, overlap = 10;
        double pageW = dlg.PrintableAreaWidth, pageH = dlg.PrintableAreaHeight;
        var tiles = PrintTiles.Layout(Session.Plan.Bounds(), pageW / mm - 10, pageH / mm - 24, overlap);
        var paper = $"Seiten {Fmt.Num(pageW / mm, 0)} × {Fmt.Num(pageH / mm, 0)} mm";
        if (Dialogs.AskPrintMode(this, tiles.Count, paper) is not { } full) return;
        if (full)
            dlg.PrintDocument(new TilePaginator(Session.Plan, tiles, new Size(pageW, pageH), License.IsPro, overlap), $"TrackMaster Pro – {Session.Plan.Name} (Bauvorlage 1:1)");
        else
            dlg.PrintVisual(PlanPrint.Render(Session.Plan, pageW, pageH, License.IsPro), $"TrackMaster Pro – {Session.Plan.Name}");
        ShowMessage(full ? $"Bauvorlage mit {tiles.Count} Seiten an den Drucker gesendet." : "An den Drucker gesendet.");
    }

    private void ShowLicense()
    {
        Dialogs.ShowLicense(this, License, () =>
        {
            var dlg = new OpenFileDialog { Title = "Lizenzdatei wählen", Filter = "TrackMaster-Lizenz (*.lic)|*.lic|Alle Dateien (*.*)|*.*" };
            if (dlg.ShowDialog(this) != true) return null;
            try
            {
                var result = License.Install(File.ReadAllText(dlg.FileName));
                if (!result.IsValid) return $"Die Lizenz wurde nicht übernommen: {result.StatusText}.";
                ApplyEdition();
                return $"Pro-Lizenz für {result.License!.Licensee} eingespielt – alle Funktionen sind frei.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return $"Die Datei lässt sich nicht lesen: {ex.Message}";
            }
        });
    }

    /// <summary>Nach dem Einspielen einer Lizenz: Statusleiste, Titel und Tooltips aktualisieren.</summary>
    private void ApplyEdition()
    {
        _editionBadge.Background = License.IsPro ? Theme.AccentBrush : Theme.HoverBrush;
        _editionBadge.Child = Theme.Label(License.EditionName, 11, true, License.IsPro ? Brushes.White : Theme.TextBrush);
        _editionBadge.ToolTip = License.Summary;
        OnSessionChanged();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (Program.ScreenshotMode || _restarting) return;
        if (!ConfirmDiscard()) { e.Cancel = true; return; }
        _backupTimer.Stop();
        Backup.Clear();
    }

    /// <summary>Sichert ungespeicherte Änderungen (alle 30 Sekunden und nach einem Programmfehler).</summary>
    public void BackupNow()
    {
        _changedSinceBackup = false;
        if (Program.ScreenshotMode || !Session.Dirty) return;
        try { Backup.Write(Session.Plan, Session.FilePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Beim Start: Sicherung aus einer abgebrochenen Sitzung anbieten.</summary>
    public bool OfferRecovery()
    {
        if (Backup.TryLoad() is not { } snap) return false;
        var answer = MessageBox.Show(this,
            $"TrackMaster Pro wurde zuletzt nicht regulär beendet.\n\nVom Plan „{snap.Name}“ gibt es eine automatische Sicherung vom {snap.Saved:dd.MM.yyyy} um {snap.Saved:HH:mm} Uhr " +
            "mit Änderungen, die nicht gespeichert wurden.\n\nSoll die Sicherung wiederhergestellt werden?",
            "Plan wiederherstellen", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
        {
            Session.Restore(snap.Json, snap.Path, dirty: true);
            Canvas.Fit();
            ShowMessage("Sicherung wiederhergestellt. Bitte speichern Sie den Plan.");
            return true;
        }
        Backup.Clear();
        return false;
    }

    private void RefreshRecent()
    {
        _recentMenu.Items.Clear();
        var items = _recent.Items;
        foreach (var path in items)
        {
            var item = new RibbonApplicationMenuItem { Header = Path.GetFileName(path).Replace(PlanSerializer.FileExtension, ""), ToolTip = path };
            item.Click += (_, _) => { if (ConfirmDiscard()) OpenPath(path); };
            _recentMenu.Items.Add(item);
        }
        if (items.Count == 0) _recentMenu.Items.Add(new RibbonApplicationMenuItem { Header = "(noch keine)", IsEnabled = false });
    }
}
