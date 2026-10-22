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

    private readonly LibraryPanel _library;
    private readonly PropertiesPanel _properties;
    private readonly TextBlock _title = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _docTab = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _zoomText = new() { Width = 48, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _statusLeft = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _statusMessage = new() { Margin = new Thickness(10, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _cursorText = new() { Margin = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.TextSecondaryBrush };
    private readonly DispatcherTimer _messageTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Dictionary<string, ICommand> _commands = new();
    private readonly List<(RibbonToggleButton Button, Func<bool> IsOn)> _toggles = [];
    private readonly List<(ToggleButton Button, Func<bool> IsOn)> _statusToggles = [];
    private readonly Border _root;
    private readonly Border _menuLabel;
    private readonly Canvas _menuLabelHost = new() { IsHitTestVisible = false };
    private RibbonApplicationMenu _appMenu = null!;

    public MainWindow()
    {
        Title = "TrackMaster Pro";
        Width = 1600; Height = 940; MinWidth = 1024; MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Theme.WindowBrush;
        Foreground = Theme.TextBrush;
        FontFamily = Theme.Font;
        FontSize = 12;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Icon = Icons.Get("App", Theme.Accent);
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
        Session.Message += ShowMessage;
        Canvas.ViewChanged += () => { _zoomText.Text = $"{Fmt.Num(Canvas.Zoom * 100, 0)} %"; UpdateStatus(); };
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

        Add("new", () => { if (ConfirmDiscard()) Session.NewPlan(); });
        Add("open", OpenFile);
        Add("save", () => SaveFile(false));
        Add("saveAs", () => SaveFile(true));
        Add("sample", () => { if (ConfirmDiscard()) { Session.LoadSample(); Canvas.Fit(); } });
        Add("bom", () => Dialogs.ShowBillOfMaterials(this, Session.Plan, ExportCsv));
        Add("csv", ExportCsv);
        Add("svg", ExportSvg);
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
        Add("delete", Session.Delete, HasSelection);
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
    }

    /// <summary>Befehl über seine Kennung ausführen (auch aus dem Eigenschaften-Panel).</summary>
    public void Run(string id)
    {
        if (_commands.TryGetValue(id, out var c) && c.CanExecute(null)) c.Execute(null);
    }

    private ICommand Planned(string name) => new RelayCommand(() => ShowMessage($"„{name}“ ist für eine spätere Version geplant."));

    private void SetTool(CanvasTool tool)
    {
        Session.Tool = tool;
        Canvas.Cursor = tool == CanvasTool.Pan ? Cursors.Hand : null;
        SyncToggles();
    }

    // ---------- Titelleiste ----------

    private UIElement BuildTitleBar()
    {
        var bar = new Grid { Background = Brushes.White };
        var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new System.Windows.Controls.Image { Source = Icons.Get("App", Theme.Accent), Width = 18, Height = 18 });
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

    private RibbonButton PlannedLarge(string label, string icon)
    {
        var b = Large(label, icon, Planned(label), "Geplant für eine spätere Version.");
        b.Opacity = 0.55;
        return b;
    }

    private RibbonButton PlannedSmall(string label, string icon)
    {
        var b = Small(label, icon, Planned(label), "Geplant für eine spätere Version.");
        b.Opacity = 0.55;
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
        _appMenu.Items.Add(Item("Speichern", "Save", "save"));
        _appMenu.Items.Add(Item("Speichern unter…", "SaveAs", "saveAs"));
        _appMenu.Items.Add(new RibbonSeparator());
        _appMenu.Items.Add(Item("Stückliste…", "List", "bom"));
        _appMenu.Items.Add(Item("Stückliste als CSV…", "Csv", "csv"));
        _appMenu.Items.Add(Item("Als SVG exportieren (1:1)…", "Image", "svg"));
        _appMenu.Items.Add(new RibbonSeparator());
        _appMenu.Items.Add(Item("Beispielplan laden", "Template", "sample"));
        _appMenu.FooterPaneContent = new RibbonButton { Label = "Beenden", SmallImageSource = Icons.Get("Exit"), Command = _commands["exit"], HorizontalAlignment = HorizontalAlignment.Right };
        Ribbon.ApplicationMenu = _appMenu;

        Ribbon.Items.Add(Tab("Start",
            Group("Verlauf", Large("Rückgängig", "Undo", "undo", "Letzten Schritt zurücknehmen (Strg+Z)."), Large("Wiederholen", "Redo", "redo", "Zurückgenommenen Schritt wiederholen (Strg+Y).")),
            Group("Werkzeuge",
                Toggle("Auswählen", "Select", true, () => Session.Tool == CanvasTool.Select, _ => SetTool(CanvasTool.Select), "Gleise wählen und verschieben."),
                Toggle("Verschieben", "Hand", true, () => Session.Tool == CanvasTool.Pan, on => SetTool(on ? CanvasTool.Pan : CanvasTool.Select), "Ansicht mit der linken Maustaste verschieben (auch: Leertaste halten).")),
            Group("Bearbeiten",
                Small("15° links drehen", "RotateLeft", "rotL", "Umschalt+R"), Small("15° rechts drehen", "RotateRight", "rotR", "R"), Small("Spiegeln", "Mirror", "flip", "Bögen, Weichen und Kreuzungen spiegeln (F)."),
                Small("Anschluss wechseln", "Swap", "swap", "Gleis mit dem nächsten Anschluss an dieselbe Stelle legen (A)."), Small("Duplizieren", "Copy", "duplicate", "Strg+D"), Small("Löschen", "Delete", "delete", "Entf")),
            Group("Auswahl", Small("Alles auswählen", "SelectAll", "selectAll", "Strg+A"), Small("Auswahl aufheben", "SelectNone", "selectNone", "Esc"), Small("Nächstes Gleisende", "Connector", "nextEnd", "Springt zum nächsten offenen Gleisende (E).")),
            Group("Prüfen", Large("Stückliste", "List", "bom", "Alle Gleise nach Artikel gezählt."), PlannedLarge("Kollisions­prüfung", "Shield"))));

        Ribbon.Items.Add(Tab("Gleise",
            Group("Bibliothek", Large("Gleis suchen", "Search", "search", "Springt in das Suchfeld der Gleisbibliothek.")),
            Group("Verlegen",
                Large("Gerade", "Straight", "qStraight", "Gerade des gewählten Systems am aktiven Gleisende ansetzen."), Large("Bogen", "Curve", "qCurve"),
                Large("Weiche links", "SwitchLeft", "qSwitchL"), Large("Weiche rechts", "SwitchRight", "qSwitchR"),
                Small("Prellbock", "Buffer", "qBuffer"), Small("Kreuzung", "Crossing", "qCrossing"), PlannedSmall("Flexgleis", "Flex")),
            Group("Höhe", PlannedSmall("Steigung", "Slope"), PlannedSmall("Gleiswendel", "Helix"))));

        Ribbon.Items.Add(Tab("Gelände",
            Group("Grundplatte", Large("Anlagen­platte", "Board", "board", "Breite und Tiefe der Anlagenplatte einstellen.")),
            Group("Landschaft (geplant)", PlannedLarge("Gelände formen", "Terrain"), PlannedLarge("Tunnel", "Tunnel"), PlannedSmall("Gewässer", "Water"), PlannedSmall("Gebäude", "Building"), PlannedSmall("Vegetation", "Tree"))));

        Ribbon.Items.Add(Tab("Elektrik",
            Group("Stromversorgung (geplant)", PlannedLarge("Einspeisung", "Bolt"), PlannedSmall("Trennstelle", "Isolate")),
            Group("Digital (geplant)", PlannedLarge("Decoder", "Chip"), PlannedLarge("Signal setzen", "Signal"), PlannedSmall("Kabelplan", "Wire"))));

        Ribbon.Items.Add(Tab("Ansicht",
            Group("Zoom", Large("Ganze Anlage", "Fit", "fit", "Taste 0"), Small("Vergrößern", "ZoomIn", "zoomIn", "+"), Small("Verkleinern", "ZoomOut", "zoomOut", "-")),
            Group("Anzeigen",
                Toggle("Raster", "Grid", false, () => Session.ShowGrid, on => { Session.ShowGrid = on; Canvas.InvalidateVisual(); }),
                Toggle("Gleisenden", "Connector", false, () => Session.ShowEnds, on => { Session.ShowEnds = on; Canvas.InvalidateVisual(); }),
                Toggle("Artikelnummern", "Hash", false, () => Session.ShowLabels, on => { Session.ShowLabels = on; Canvas.InvalidateVisual(); })),
            Group("Hilfe", Large("Tasten­kürzel", "Help", "help", "Alle Maus- und Tastaturbefehle."))));

        Ribbon.Items.Add(Tab("3D",
            Group("3D (geplant)", PlannedLarge("3D-Ansicht", "Cube"), PlannedLarge("Mitfahren", "Train"), PlannedLarge("Tageszeit", "Sun"))));
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
        tabContent.Children.Add(new System.Windows.Controls.Image { Source = Icons.Get("App", Theme.Accent), Width = 14, Height = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
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
        _title.Text = $"{file}{(Session.Dirty ? " *" : "")} – TrackMaster Pro";
        Title = _title.Text;
        _docTab.Text = $"{plan.Name} · Spur {plan.GaugeId}";
        _hint.Text = plan.Pieces.Count == 0
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

    // ---------- Tastatur ----------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (ctrl && e.Key == Key.S) { Run("save"); e.Handled = true; return; }
        if (ctrl && e.Key == Key.O) { Run("open"); e.Handled = true; return; }
        if (ctrl && e.Key == Key.N) { Run("new"); e.Handled = true; return; }
        if (e.Key == Key.F1) { Run("help"); e.Handled = true; return; }
        if (Keyboard.FocusedElement is TextBox or ComboBox or ComboBoxItem) return;

        string? cmd = (ctrl, e.Key) switch
        {
            (true, Key.Z) => shift ? "redo" : "undo",
            (true, Key.Y) => "redo",
            (true, Key.A) => "selectAll",
            (true, Key.D) => "duplicate",
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
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"„{Path.GetFileName(path)}“ lässt sich nicht öffnen.\n\n{ex.Message}", "TrackMaster Pro", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool SaveFile(bool askForName)
    {
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

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (Program.ScreenshotMode) return;
        if (!ConfirmDiscard()) e.Cancel = true;
    }
}
