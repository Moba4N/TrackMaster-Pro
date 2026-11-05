using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using TrackMaster.Core;

namespace TrackMaster.App;

/// <summary>
/// Modell-Editor (Pro): Gebäude und andere Ausstattung aus Körpern zusammensetzen. Drei Ansichten (oben, vorne, seitlich)
/// zum Wählen und Verschieben, Ecken von Vieleck und Freiform ziehen, 3D-Vorschau, Eigenschaften des gewählten Körpers.
/// </summary>
public sealed class ModelEditorWindow : Window
{
    private readonly ModelEditor _ed;
    private readonly Func<ModelDefinition, bool> _save;
    private readonly double _mmPerMeter;
    private readonly string _gaugeName;
    private readonly List<EditorViewControl> _views = [];
    private readonly Model3DGroup _preview = new();
    private readonly Scene3D _scene = new();
    private readonly PerspectiveCamera _camera = new() { FieldOfView = 40, NearPlaneDistance = 0.1, FarPlaneDistance = 2000 };
    private double _yaw = -35, _pitch = 25, _distance = 30;
    private Point? _orbitStart;
    private (double Yaw, double Pitch) _orbitOrigin;

    private readonly TextBox _name = new();
    private readonly ComboBox _category = new() { IsEditable = true };
    private readonly ComboBox _snap = new();
    private readonly ComboBox _mainMaterial = new();
    private readonly TextBox _description = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 60, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ListBox _bodies = new() { SelectionMode = SelectionMode.Extended, BorderBrush = Theme.ControlBorderBrush };
    private readonly StackPanel _props = new() { Margin = new Thickness(12) };
    private readonly TextBlock _status = new() { Foreground = Theme.TextSecondaryBrush, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _size = new() { Foreground = Theme.TextSecondaryBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
    private readonly ToggleButton _polygonMode = new();
    private readonly ComboBox _grid = new() { Width = 90, Height = 28, Margin = new Thickness(0, 0, 6, 6) };
    private readonly Dictionary<string, Button> _actions = [];
    private bool _syncing;
    private bool _fromPanel;

    /// <summary>Wurde mindestens einmal gespeichert?</summary>
    public bool Saved { get; private set; }

    /// <param name="model">Zu bearbeitendes eigenes Modell (eine Kopie wird bearbeitet).</param>
    /// <param name="save">Speichert das Modell in der Bibliothek (und im Plan); false bei Fehler.</param>
    public ModelEditorWindow(ModelDefinition model, Gauge gauge, Func<ModelDefinition, bool> save)
    {
        _ed = new ModelEditor(model.Clone());
        _save = save;
        _mmPerMeter = ModelLibrary.MmPerMeter(gauge);
        _gaugeName = gauge.Id;
        Width = 1360; Height = 860; MinWidth = 960; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = Theme.Font;
        FontSize = 12 * Theme.FontScale;
        Background = Theme.WindowBrush;
        ShowInTaskbar = false;

        var root = new DockPanel();
        var toolbar = BuildToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        var footer = BuildFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var left = BuildModelPanel();
        DockPanel.SetDock(left, Dock.Left);
        root.Children.Add(left);
        var right = new Border
        {
            Width = 270, Background = Theme.PanelBrush, BorderBrush = Theme.BorderBrush, BorderThickness = new Thickness(1, 0, 0, 0),
            Child = new DockPanel { Children = { Header(Lang.T("Gewählter Körper")), new ScrollViewer { Content = _props, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } } },
        };
        DockPanel.SetDock((UIElement)((DockPanel)right.Child).Children[0], Dock.Top);
        DockPanel.SetDock(right, Dock.Right);
        root.Children.Add(right);
        root.Children.Add(BuildViews());
        Content = root;

        _ed.Changed += OnChanged;
        PreviewKeyDown += OnKey;
        Closing += (_, e) =>
        {
            if (!_ed.Dirty) return;
            var answer = MessageBox.Show(this, Lang.F($"Änderungen an „{_ed.Model.Name}“ speichern?"), Lang.T("Modell-Editor"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !Save())) e.Cancel = true;
        };
        Loaded += (_, _) => { foreach (var v in _views) v.Fit(); };
        LoadModelFields();
        OnChanged();
    }

    private ModelDefinition Model => _ed.Model;

    /// <summary>Körper (und Ecke) wählen, z. B. für Bildschirmfotos.</summary>
    internal void SelectBody(int index, int vertex = -1)
    {
        _ed.Selection.Clear();
        if (index >= 0 && index < Model.Bodies.Count) _ed.Selection.Add(index);
        _ed.SelectedVertex = vertex;
        OnChanged();
    }

    // ---------- Aufbau ----------

    private static Border Header(string title) => new()
    {
        Height = 32, Padding = new Thickness(12, 0, 12, 0), Background = Theme.PaneHeaderBrush, BorderBrush = Theme.BorderBrush, BorderThickness = new Thickness(0, 0, 0, 1),
        Child = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center },
    };

    private Button Action(string key, string text, string? icon, Action run, string? tip = null)
    {
        var b = Theme.Button(text, icon);
        b.ToolTip = tip;
        b.Click += (_, _) => { run(); Keyboard.Focus(this); };
        _actions[key] = b;
        return b;
    }

    private UIElement BuildToolbar()
    {
        var shapes = new WrapPanel { Margin = new Thickness(12, 8, 12, 0) };
        shapes.Children.Add(new TextBlock { Text = Lang.T("Hinzufügen:"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6), Foreground = Theme.TextSecondaryBrush });
        foreach (var (shape, icon) in new (BodyShape, string)[]
                 {
                     (BodyShape.Quader, "Cube"), (BodyShape.Zylinder, "Column"), (BodyShape.Keil, "Slope"), (BodyShape.Satteldach, "Building"),
                     (BodyShape.Walmdach, "Building"), (BodyShape.Pultdach, "Building"), (BodyShape.Gewoelbe, "Tunnel"), (BodyShape.Bogenwand, "Tunnel"),
                     (BodyShape.Fenster, "Window"), (BodyShape.Tuer, "Door"), (BodyShape.Tor, "Door"), (BodyShape.Treppe, "Ramp"),
                     (BodyShape.Vieleck, "Room"), (BodyShape.Freiform, "Cube"),
                 })
        {
            var s = shape;
            var b = Theme.Button(ModelBody.ShapeName(s), icon);
            b.ToolTip = s switch
            {
                BodyShape.Fenster or BodyShape.Tuer or BodyShape.Tor => Lang.T("Kommt an die Vorderseite des gewählten Körpers."),
                BodyShape.Satteldach or BodyShape.Walmdach or BodyShape.Pultdach => Lang.T("Kommt oben auf den gewählten Körper."),
                BodyShape.Vieleck => Lang.T("Vieleck mit fünf Ecken; die Ecken lassen sich ziehen. Eigener Grundriss: „Grundriss zeichnen“."),
                BodyShape.Freiform => Lang.T("Freie Form, bei der sich jede Ecke in allen drei Richtungen ziehen lässt."),
                _ => null,
            };
            b.Click += (_, _) => { _ed.Add(s); Keyboard.Focus(this); };
            shapes.Children.Add(b);
        }
        _polygonMode.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { new Image { Source = Icons.Get("Room"), Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) }, new TextBlock { Text = Lang.T("Grundriss zeichnen"), VerticalAlignment = VerticalAlignment.Center } },
        };
        _polygonMode.Height = 30;
        _polygonMode.Padding = new Thickness(10, 0, 10, 0);
        _polygonMode.Margin = new Thickness(0, 0, 6, 6);
        _polygonMode.ToolTip = Lang.T("In der Draufsicht Ecke für Ecke klicken; Doppelklick, Eingabetaste oder rechte Maustaste schließt den Grundriss, Esc bricht ab.");
        _polygonMode.Checked += (_, _) => Say(Lang.T("Grundriss zeichnen: in der Draufsicht die Ecken anklicken, Doppelklick schließt."));
        _polygonMode.Unchecked += (_, _) => { foreach (var v in _views) v.CancelPolygon(); };
        shapes.Children.Add(_polygonMode);

        var edit = new WrapPanel { Margin = new Thickness(12, 0, 12, 2) };
        edit.Children.Add(Action("undo", Lang.T("Rückgängig"), "Undo", _ed.Undo, Lang.T("Strg+Z")));
        edit.Children.Add(Action("redo", Lang.T("Wiederholen"), "Redo", _ed.Redo, Lang.T("Strg+Y")));
        edit.Children.Add(Action("dup", Lang.T("Duplizieren"), "Copy", _ed.Duplicate, Lang.T("Strg+D")));
        edit.Children.Add(Action("del", Lang.T("Löschen"), "Delete", _ed.Delete, Lang.T("Entf")));
        edit.Children.Add(Action("mirrorX", Lang.T("Spiegeln links/rechts"), "Mirror", () => _ed.Mirror(true), Lang.T("Spiegelt die gewählten Körper (ohne Auswahl alle) an der Mittelachse.")));
        edit.Children.Add(Action("mirrorY", Lang.T("Spiegeln vorn/hinten"), "Mirror", () => _ed.Mirror(false)));
        edit.Children.Add(Action("group", Lang.T("Gruppieren"), "Layers", () => _ed.Group(), Lang.T("Gruppierte Körper werden gemeinsam verschoben.")));
        edit.Children.Add(Action("ungroup", Lang.T("Gruppe lösen"), "Layers", () => _ed.Group(true)));
        edit.Children.Add(Action("freeform", Lang.T("In Freiform umwandeln"), "Cube", _ed.ToFreeform, Lang.T("Gleiche Gestalt, danach lässt sich jede Ecke frei ziehen.")));
        edit.Children.Add(Action("vtxAdd", Lang.T("Ecke einfügen"), "Plus", _ed.InsertVertex, Lang.T("Neue Ecke in der Mitte der Kante hinter der gewählten Ecke.")));
        edit.Children.Add(Action("vtxDel", Lang.T("Ecke entfernen"), "Minus", _ed.RemoveVertex));
        edit.Children.Add(new TextBlock { Text = Lang.T("Raster:"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 6), Foreground = Theme.TextSecondaryBrush });
        foreach (var (label, step) in new[] { (Lang.T("aus"), 0.0), ("1 cm", 0.01), ("5 cm", 0.05), ("10 cm", 0.1), ("25 cm", 0.25), ("50 cm", 0.5), ("1 m", 1.0) })
            _grid.Items.Add(new ComboBoxItem { Content = label, Tag = step });
        _grid.SelectedIndex = 3;
        _grid.SelectionChanged += (_, _) => { _ed.Grid = (double)((ComboBoxItem)_grid.SelectedItem).Tag; Redraw(); };
        edit.Children.Add(_grid);
        edit.Children.Add(Action("fit", Lang.T("Einpassen"), "Fit", () => { foreach (var v in _views) v.Fit(); }));

        return new Border
        {
            Background = Theme.PanelBrush, BorderBrush = Theme.BorderBrush, BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel { Children = { shapes, edit } },
        };
    }

    private UIElement BuildFooter()
    {
        var dock = new DockPanel { Margin = new Thickness(12, 8, 12, 2) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var save = Theme.Button(Lang.T("Speichern"), "Save", accent: true);
        save.Click += (_, _) => Save();
        var saveClose = Theme.Button(Lang.T("Speichern und schließen"));
        saveClose.Click += (_, _) => { if (Save()) Close(); };
        var close = Theme.Button(Lang.T("Schließen"));
        close.Click += (_, _) => Close();
        buttons.Children.Add(save);
        buttons.Children.Add(saveClose);
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Right);
        dock.Children.Add(buttons);
        DockPanel.SetDock(_size, Dock.Left);
        dock.Children.Add(_size);
        dock.Children.Add(_status);
        return new Border { Background = Theme.PanelBrush, BorderBrush = Theme.BorderBrush, BorderThickness = new Thickness(0, 1, 0, 0), Child = dock };
    }

    private UIElement BuildModelPanel()
    {
        var p = new StackPanel { Margin = new Thickness(12) };
        p.Children.Add(Theme.Field(Lang.T("Name"), _name));
        _name.TextChanged += (_, _) =>
        {
            if (_syncing) return;
            Model.Name = _name.Text;
            _ed.Dirty = true;
            UpdateTitle();
        };
        foreach (var c in ModelLibrary.Categories()) _category.Items.Add(c);
        _category.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) =>
        {
            if (_syncing) return;
            Model.Category = _category.Text.Trim();
            _ed.Dirty = true;
        }));
        p.Children.Add(Theme.Field(Lang.T("Kategorie (Menü)"), _category));
        foreach (var s in Enum.GetValues<ModelSnap>()) _snap.Items.Add(new ComboBoxItem { Content = ModelBody.SnapName(s), Tag = s });
        _snap.SelectionChanged += (_, _) =>
        {
            if (_syncing || _snap.SelectedItem is not ComboBoxItem { Tag: ModelSnap s }) return;
            Model.Snap = s;
            _ed.Dirty = true;
        };
        p.Children.Add(Theme.Field(Lang.T("Am Gleis einrasten"), _snap));
        _mainMaterial.Items.Add(new ComboBoxItem { Content = Lang.T("(erster Körper)"), Tag = null });
        foreach (var m in Enum.GetValues<ModelMaterial>()) _mainMaterial.Items.Add(new ComboBoxItem { Content = ModelBody.MaterialName(m), Tag = m });
        _mainMaterial.SelectionChanged += (_, _) =>
        {
            if (_syncing || _mainMaterial.SelectedItem is not ComboBoxItem item) return;
            Model.MainMaterial = item.Tag as ModelMaterial?;
            _ed.Dirty = true;
        };
        var mm = Theme.Field(Lang.T("Hauptmaterial (Farbwahl im Plan)"), _mainMaterial);
        mm.ToolTip = Lang.T("Körper aus diesem Material bekommen im Plan die gewählte Hauptfarbe.");
        p.Children.Add(mm);
        var desc = new StackPanel { Margin = new Thickness(0, 0, 6, 8) };
        desc.Children.Add(new TextBlock { Text = Lang.T("Beschreibung"), FontSize = 11 * Theme.FontScale, Foreground = Theme.TextSecondaryBrush, Margin = new Thickness(0, 0, 0, 3) });
        desc.Children.Add(_description);
        _description.BorderBrush = Theme.ControlBorderBrush;
        _description.TextChanged += (_, _) =>
        {
            if (_syncing) return;
            Model.Description = _description.Text;
            _ed.Dirty = true;
        };
        p.Children.Add(desc);

        var bodiesHeader = Header(Lang.T("Körper"));
        _bodies.Margin = new Thickness(12, 8, 12, 12);
        _bodies.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            _ed.Selection.Clear();
            foreach (var item in _bodies.SelectedItems.OfType<ListBoxItem>()) _ed.Selection.Add((int)item.Tag);
            _ed.SelectedVertex = -1;
            OnChanged();
        };
        var dock = new DockPanel();
        DockPanel.SetDock(p, Dock.Top);
        dock.Children.Add(Header(Lang.T("Modell")));
        DockPanel.SetDock(dock.Children[0], Dock.Top);
        dock.Children.Add(p);
        DockPanel.SetDock(bodiesHeader, Dock.Top);
        dock.Children.Add(bodiesHeader);
        dock.Children.Add(_bodies);
        return new Border { Width = 260, Background = Theme.PanelBrush, BorderBrush = Theme.BorderBrush, BorderThickness = new Thickness(0, 0, 1, 0), Child = dock };
    }

    private UIElement BuildViews()
    {
        var grid = new Grid { Margin = new Thickness(6) };
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var titles = new Dictionary<EditorView, string>
        {
            [EditorView.Draufsicht] = Lang.T("Draufsicht"),
            [EditorView.Vorderansicht] = Lang.T("Vorderansicht (Gleisseite unten in der Draufsicht)"),
            [EditorView.Seitenansicht] = Lang.T("Seitenansicht von rechts"),
        };
        var cells = new[] { (EditorView.Draufsicht, 0, 0), (EditorView.Vorderansicht, 1, 0), (EditorView.Seitenansicht, 1, 1) };
        foreach (var (view, row, col) in cells)
        {
            var v = new EditorViewControl(_ed, view, this);
            _views.Add(v);
            var frame = Frame(titles[view], v);
            Grid.SetRow(frame, row); Grid.SetColumn(frame, col);
            grid.Children.Add(frame);
        }

        var viewport = new Viewport3D { ClipToBounds = true, Camera = _camera };
        var all = new Model3DGroup();
        all.Children.Add(new AmbientLight(Color.FromRgb(0x70, 0x70, 0x70)));
        all.Children.Add(new DirectionalLight(Color.FromRgb(0xC8, 0xC8, 0xC0), new Vector3D(-0.5, -1, -0.7)));
        all.Children.Add(new DirectionalLight(Color.FromRgb(0x40, 0x40, 0x48), new Vector3D(0.6, -0.3, 0.8)));
        all.Children.Add(_preview);
        viewport.Children.Add(new ModelVisual3D { Content = all });
        var host = new Border { Background = new LinearGradientBrush(Color.FromRgb(0xCF, 0xE2, 0xF2), Color.FromRgb(0xF4, 0xF6, 0xF8), 90), Child = viewport, Cursor = Cursors.SizeAll };
        host.MouseLeftButtonDown += (_, e) => { _orbitStart = e.GetPosition(host); _orbitOrigin = (_yaw, _pitch); host.CaptureMouse(); };
        host.MouseMove += (_, e) =>
        {
            if (_orbitStart is not { } s || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(host);
            _yaw = _orbitOrigin.Yaw - (p.X - s.X) * 0.4;
            _pitch = Math.Clamp(_orbitOrigin.Pitch + (p.Y - s.Y) * 0.3, -10, 89);
            PlaceCamera();
        };
        host.MouseLeftButtonUp += (_, _) => { _orbitStart = null; host.ReleaseMouseCapture(); };
        host.MouseWheel += (_, e) => { _distance = Math.Clamp(_distance * (e.Delta > 0 ? 0.88 : 1.14), 2, 800); PlaceCamera(); };
        var preview = Frame(Lang.T("3D-Vorschau (ziehen dreht, Mausrad zoomt)"), host);
        Grid.SetRow(preview, 0); Grid.SetColumn(preview, 1);
        grid.Children.Add(preview);
        return grid;
    }

    private static Border Frame(string title, UIElement content)
    {
        var label = new TextBlock { Text = title, Margin = new Thickness(8, 4, 8, 4), Foreground = Theme.TextSecondaryBrush, FontSize = 11 * Theme.FontScale };
        var dock = new DockPanel();
        DockPanel.SetDock(label, Dock.Top);
        dock.Children.Add(label);
        dock.Children.Add(content);
        return new Border { Margin = new Thickness(6), Background = Theme.PanelBrush, BorderBrush = Theme.BorderStrongBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = dock, ClipToBounds = true };
    }

    // ---------- Aktualisieren ----------

    private void LoadModelFields()
    {
        _syncing = true;
        _name.Text = Model.Name;
        _category.Text = Model.Category;
        _snap.SelectedIndex = (int)Model.Snap;
        _mainMaterial.SelectedIndex = Model.MainMaterial is { } m ? (int)m + 1 : 0;
        _description.Text = Model.Description;
        _syncing = false;
        UpdateTitle();
    }

    private void UpdateTitle() => Title = Lang.F($"Modell-Editor – {(string.IsNullOrWhiteSpace(Model.Name) ? Lang.T("Modell") : Model.Name)}{(_ed.Dirty ? " *" : "")}");

    /// <summary>Nach jeder Änderung: Ansichten, Vorschau, Körperliste, Eigenschaften und Schaltflächen.</summary>
    private void OnChanged()
    {
        Redraw();
        RebuildPreview();
        SyncBodyList();
        if (!_fromPanel) RebuildProps();
        UpdateActions();
        UpdateTitle();
        var (w, d, h) = Model.Size;
        _size.Text = Lang.F($"Vorbild {Fmt.Num(w, 1)} × {Fmt.Num(d, 1)} × {Fmt.Num(h, 1)} m · {_gaugeName}: {Fmt.Num(w * _mmPerMeter, 0)} × {Fmt.Num(d * _mmPerMeter, 0)} × {Fmt.Num(h * _mmPerMeter, 0)} mm");
    }

    /// <summary>Nur neu zeichnen (beim Ziehen), Vorschau mit.</summary>
    internal void Redraw(bool preview = false)
    {
        foreach (var v in _views) v.InvalidateVisual();
        if (preview) RebuildPreview();
    }

    internal bool PolygonMode => _polygonMode.IsChecked == true;

    internal void PolygonDone(IReadOnlyList<(double X, double Y)> points)
    {
        _polygonMode.IsChecked = false;
        var height = Selected is { } host && !host.IsDetail ? Math.Max(1, host.H) : 3;
        if (_ed.AddPolygon(points, height) is null) Say(Lang.T("Für einen Grundriss braucht es mindestens drei Ecken, die eine Fläche umschließen."));
        else Say(Lang.T("Grundriss angelegt. Ecken lassen sich in der Draufsicht ziehen, ihre Höhe in der Vorder- oder Seitenansicht."));
    }

    internal void Say(string text) => _status.Text = text;

    private ModelBody? Selected => _ed.Selected;

    private void SyncBodyList()
    {
        _syncing = true;
        _bodies.Items.Clear();
        for (var i = 0; i < Model.Bodies.Count; i++)
        {
            var b = Model.Bodies[i];
            var text = $"{i + 1}. {ModelBody.ShapeName(b.Shape)} · {ModelBody.MaterialName(b.Material)}" + (b.Group > 0 ? Lang.F($" · Gruppe {b.Group}") : "");
            var item = new ListBoxItem { Content = text, Tag = i };
            _bodies.Items.Add(item);
            if (_ed.Selection.Contains(i)) item.IsSelected = true;
        }
        if (_ed.SelectedIndex >= 0 && _ed.SelectedIndex < _bodies.Items.Count) _bodies.ScrollIntoView(_bodies.Items[_ed.SelectedIndex]);
        _syncing = false;
    }

    private void UpdateActions()
    {
        var sel = _ed.Selection.Count > 0;
        var poly = Selected is { Shape: BodyShape.Vieleck or BodyShape.Freiform };
        _actions["undo"].IsEnabled = _ed.CanUndo;
        _actions["redo"].IsEnabled = _ed.CanRedo;
        _actions["dup"].IsEnabled = sel;
        _actions["del"].IsEnabled = sel;
        _actions["group"].IsEnabled = _ed.Selection.Count > 1;
        _actions["ungroup"].IsEnabled = sel && _ed.SelectedWithGroups().Any(b => b.Group > 0);
        _actions["freeform"].IsEnabled = Selected is { } s && s.Shape != BodyShape.Freiform;
        _actions["vtxAdd"].IsEnabled = poly;
        _actions["vtxDel"].IsEnabled = poly && _ed.SelectedVertex >= 0;
    }

    private void RebuildPreview()
    {
        var meshes = new Dictionary<(Rgb Color, SurfaceKind Kind), SceneMesh>();
        void Add(Rgb color, SurfaceKind kind, ModelTriangle t)
        {
            if (!meshes.TryGetValue((color, kind), out var m)) meshes[(color, kind)] = m = new SceneMesh();
            static Vec3 P((double X, double Y, double Z) p) => new(p.X, p.Z, p.Y);
            m.Triangle(P(t.A), P(t.B), P(t.C));
        }
        var accent = new Rgb(Theme.Accent.R, Theme.Accent.G, Theme.Accent.B);
        for (var i = 0; i < Model.Bodies.Count; i++)
        {
            var b = Model.Bodies[i];
            var (color, kind) = ModelMesh.Surface(Model, b, null);
            if (_ed.Selection.Contains(i)) { color = Mix(color, accent, 0.35); kind = SurfaceKind.Matte; }
            foreach (var t in ModelMesh.Body(b)) Add(color, kind, t);
        }
        // Boden zur Orientierung
        var (x0, y0, x1, y1, _) = Model.Bounds();
        var pad = Math.Max(2, Math.Max(x1 - x0, y1 - y0) * 0.3);
        var ground = new Rgb(0xB8, 0xC4, 0xA8);
        Add(ground, SurfaceKind.Matte, new ModelTriangle((x0 - pad, y0 - pad, -0.01), (x1 + pad, y0 - pad, -0.01), (x1 + pad, y1 + pad, -0.01)));
        Add(ground, SurfaceKind.Matte, new ModelTriangle((x0 - pad, y0 - pad, -0.01), (x1 + pad, y1 + pad, -0.01), (x0 - pad, y1 + pad, -0.01)));
        _scene.Fill(_preview, meshes);
        PlaceCamera();
    }

    internal static Rgb Mix(Rgb a, Rgb b, double t) => new((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private bool _cameraSet;

    private void PlaceCamera()
    {
        var (x0, y0, x1, y1, z1) = Model.Bounds();
        var target = new Point3D((x0 + x1) / 2, z1 / 3, (y0 + y1) / 2);
        if (!_cameraSet)
        {
            _distance = Math.Clamp(Math.Max(Math.Max(x1 - x0, y1 - y0), z1) * 2.4, 6, 400);
            _cameraSet = true;
        }
        double yaw = _yaw * Math.PI / 180, pitch = _pitch * Math.PI / 180;
        var dir = new Vector3D(Math.Cos(pitch) * Math.Sin(yaw), Math.Sin(pitch), Math.Cos(pitch) * Math.Cos(yaw));
        _camera.Position = target + dir * _distance;
        _camera.LookDirection = -dir;
        _camera.UpDirection = new Vector3D(0, 1, 0);
    }

    // ---------- Eigenschaften des gewählten Körpers ----------

    private void RebuildProps()
    {
        _props.Children.Clear();
        if (Selected is not { } b)
        {
            _props.Children.Add(Theme.Secondary(Lang.T("Einen Körper in einer Ansicht anklicken oder oben einen hinzufügen. Mit Strg oder Umschalt mehrere wählen.")));
            _props.Children.Add(new TextBlock { Height = 12 });
            _props.Children.Add(Theme.Secondary(Lang.T("Ziehen verschiebt den Körper, Mausrad zoomt, rechte Maustaste verschiebt die Ansicht. Pfeiltasten verschieben um ein Rasterfeld.")));
            return;
        }
        var title = Theme.Label(ModelBody.ShapeName(b.Shape) + (_ed.Selection.Count > 1 ? Lang.F($" (und {_ed.Selection.Count - 1} weitere)") : ""), 13, true);
        title.Margin = new Thickness(0, 0, 0, 8);
        _props.Children.Add(title);

        var pos = Row();
        pos.Children.Add(Num(Lang.T("X (m)"), b.X, v => b.X = v));
        pos.Children.Add(Num(Lang.T("Y (m)"), b.Y, v => b.Y = v));
        pos.Children.Add(Num(Lang.T("über Boden (m)"), b.Z, v => b.Z = v));
        _props.Children.Add(pos);
        if (b.Shape is not (BodyShape.Vieleck or BodyShape.Freiform))
        {
            var size = Row();
            size.Children.Add(Num(Lang.T("Länge (m)"), b.W, v => b.W = Math.Max(0.01, v)));
            size.Children.Add(Num(Lang.T("Tiefe (m)"), b.D, v => b.D = Math.Max(0.01, v)));
            size.Children.Add(Num(Lang.T("Höhe (m)"), b.H, v => b.H = Math.Max(0.01, v)));
            _props.Children.Add(size);
        }
        else if (b.Shape == BodyShape.Vieleck)
            _props.Children.Add(Num(Lang.T("Höhe (m)"), b.H, v => { b.H = Math.Max(0.01, v); b.Heights.Clear(); }, Lang.T("Setzt alle Ecken auf diese Höhe.")));
        var rot = Row();
        rot.Children.Add(Num(Lang.T("Drehung (°)"), b.Rotation, v => b.Rotation = Angles.Normalize(v)));
        if (b.Shape is BodyShape.Quader or BodyShape.Zylinder or BodyShape.Vieleck)
            rot.Children.Add(Num(Lang.T("Oberseite (×)"), b.Top, v => b.Top = Math.Clamp(v, 0, 4), Lang.T("Größe der Oberseite: 1 = gerade, 0 = Spitze (Pyramide, Kegel), 0,5 = verjüngt.")));
        _props.Children.Add(rot);
        if (b.Shape is BodyShape.Gewoelbe or BodyShape.Bogenwand)
        {
            var open = Row();
            open.Children.Add(Num(Lang.T("Öffnung breit (m)"), b.OpeningW, v => b.OpeningW = Math.Max(0, v)));
            open.Children.Add(Num(Lang.T("Öffnung hoch (m)"), b.OpeningH, v => b.OpeningH = Math.Max(0, v)));
            _props.Children.Add(open);
        }

        var material = new ComboBox();
        foreach (var m in Enum.GetValues<ModelMaterial>()) material.Items.Add(new ComboBoxItem { Content = ModelBody.MaterialName(m), Tag = m });
        material.SelectedIndex = (int)b.Material;
        material.SelectionChanged += (_, _) =>
        {
            if (material.SelectedItem is ComboBoxItem { Tag: ModelMaterial m }) FromPanel(() => { foreach (var x in SelectedBodies()) x.Material = m; });
        };
        _props.Children.Add(Theme.Field(Lang.T("Material"), material));

        var color = new ComboBox();
        color.Items.Add(new ComboBoxItem { Content = Lang.T("Farbe des Materials"), Tag = null });
        foreach (var (name, hex) in ModelLibrary.MainColors.Where(c => c.Hex is not null)) color.Items.Add(new ComboBoxItem { Content = name, Tag = hex, Background = Brush(hex!) });
        if (b.Color is { } own && ModelLibrary.MainColors.All(c => !string.Equals(c.Hex, own, StringComparison.OrdinalIgnoreCase)))
            color.Items.Add(new ComboBoxItem { Content = own, Tag = own, Background = Brush(own) });
        color.SelectedItem = color.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, b.Color, StringComparison.OrdinalIgnoreCase)) ?? color.Items[0];
        color.SelectionChanged += (_, _) =>
        {
            if (color.SelectedItem is ComboBoxItem item) FromPanel(() => { foreach (var x in SelectedBodies()) x.Color = item.Tag as string; });
        };
        _props.Children.Add(Theme.Field(Lang.T("Farbe"), color));

        if (b.Shape is BodyShape.Vieleck or BodyShape.Freiform)
        {
            var count = b.Shape == BodyShape.Vieleck ? b.Points.Count : b.Vertices.Count;
            var head = Theme.Label(Lang.F($"Ecken: {count}"), 12, true);
            head.Margin = new Thickness(0, 6, 0, 4);
            _props.Children.Add(head);
            if (_ed.SelectedVertex >= 0 && _ed.SelectedVertex < count)
            {
                var i = _ed.SelectedVertex;
                var row = Row();
                if (b.Shape == BodyShape.Vieleck)
                {
                    row.Children.Add(Num(Lang.T("Ecke x"), b.Points[i].X, v => b.Points[i] = (v, b.Points[i].Y)));
                    row.Children.Add(Num(Lang.T("Ecke y"), b.Points[i].Y, v => b.Points[i] = (b.Points[i].X, v)));
                    row.Children.Add(Num(Lang.T("Höhe"), i < b.Heights.Count ? b.Heights[i] : b.H, v =>
                    {
                        while (b.Heights.Count < b.Points.Count) b.Heights.Add(b.H);
                        b.Heights[i] = Math.Max(0.01, v);
                    }));
                }
                else
                {
                    row.Children.Add(Num(Lang.T("Ecke x"), b.Vertices[i].X, v => b.Vertices[i] = (v, b.Vertices[i].Y, b.Vertices[i].Z)));
                    row.Children.Add(Num(Lang.T("Ecke y"), b.Vertices[i].Y, v => b.Vertices[i] = (b.Vertices[i].X, v, b.Vertices[i].Z)));
                    row.Children.Add(Num(Lang.T("Ecke z"), b.Vertices[i].Z, v => b.Vertices[i] = (b.Vertices[i].X, b.Vertices[i].Y, v)));
                }
                _props.Children.Add(Theme.Secondary(Lang.F($"Ecke {i + 1} (in eigenen Achsen des Körpers):")));
                _props.Children.Add(row);
            }
            else _props.Children.Add(Theme.Secondary(Lang.T("Eine Ecke (Quadrat) in einer Ansicht anklicken, um sie zu ziehen oder ihre Werte einzugeben.")));
        }
        if (b.Group > 0) _props.Children.Add(Theme.Secondary(Lang.F($"Gehört zu Gruppe {b.Group}; die Gruppe wird gemeinsam verschoben.")));
    }

    private IEnumerable<ModelBody> SelectedBodies() => _ed.Selection.Where(i => i < Model.Bodies.Count).Select(i => Model.Bodies[i]);

    private static SolidColorBrush Brush(string hex)
    {
        var c = ModelBody.ParseColor(hex) ?? new Rgb(0xFF, 0xFF, 0xFF);
        var b = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    private static UniformGrid Row() => new() { Rows = 1, Columns = 3 };

    /// <summary>Zahlenfeld: übernimmt bei Eingabetaste oder beim Verlassen; ein Rückgängig-Schritt.</summary>
    private StackPanel Num(string label, double value, Action<double> apply, string? tip = null)
    {
        var box = new TextBox { Text = Fmt.Num(value, 3), ToolTip = tip };
        var field = Theme.Field(label, box);
        void Commit()
        {
            if (Fmt.Parse(box.Text) is not { } v) { box.Text = Fmt.Num(value, 3); return; }
            if (Math.Abs(v - value) < 1e-9) return;
            value = v;
            FromPanel(() => apply(v));
            box.Text = Fmt.Num(v, 3);
        }
        box.LostKeyboardFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        return field;
    }

    /// <summary>Änderung aus dem Eigenschaftenfeld: ein Rückgängig-Schritt, Felder bleiben stehen (Fokus bleibt).</summary>
    private void FromPanel(Action change)
    {
        _fromPanel = true;
        try { _ed.Edit(change); }
        finally { _fromPanel = false; }
    }

    // ---------- Tastatur, Speichern ----------

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox || e.OriginalSource is ComboBox { IsEditable: true }) return;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var step = _ed.Grid > 0 ? _ed.Grid : 0.1;
        void Move(double dx, double dy)
        {
            if (_ed.Selection.Count == 0) return;
            _ed.Edit(() => _ed.MoveSelection(dx, dy, 0));
        }
        switch (e.Key)
        {
            case Key.Z when ctrl: _ed.Undo(); break;
            case Key.Y when ctrl: _ed.Redo(); break;
            case Key.D when ctrl: _ed.Duplicate(); break;
            case Key.S when ctrl: Save(); break;
            case Key.Delete when _ed.SelectedVertex >= 0: _ed.RemoveVertex(); break;
            case Key.Delete: _ed.Delete(); break;
            case Key.Left: Move(-step, 0); break;
            case Key.Right: Move(step, 0); break;
            case Key.Up: Move(0, -step); break;
            case Key.Down: Move(0, step); break;
            case Key.Escape when PolygonMode: _polygonMode.IsChecked = false; break;
            case Key.Enter when PolygonMode: foreach (var v in _views) v.FinishPolygon(); break;
            default: return;
        }
        e.Handled = true;
    }

    private bool Save()
    {
        Model.Name = Model.Name.Trim();
        if (_ed.Problems() is { Count: > 0 } problems)
        {
            MessageBox.Show(this, string.Join("\n", problems), Lang.T("Modell-Editor"), MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (!_save(Model.Clone())) return false;
        _ed.Dirty = false;
        Saved = true;
        UpdateTitle();
        Say(Lang.F($"„{Model.Name}“ gespeichert. Es steht jetzt unter Ausstattung › Eigene Modelle."));
        return true;
    }
}

/// <summary>Eine Rissansicht im Modell-Editor: Gitter, Körper (hinten zuerst), Auswahl, Ecken; Ziehen, Zoomen, Grundriss zeichnen.</summary>
internal sealed class EditorViewControl : FrameworkElement
{
    private readonly ModelEditor _ed;
    private readonly EditorView _view;
    private readonly ModelEditorWindow _window;
    private double _scale = 30;           // Pixel je Meter
    private Vector _offset;               // Bildschirmlage des Ursprungs
    private bool _fitted;

    private enum Drag { None, Body, Vertex, Pan }
    private Drag _drag;
    private Point _dragStart;
    private (double DX, double DY, double DZ) _applied;
    private bool _recorded;
    private Vector _panOrigin;
    private readonly List<(double X, double Y)> _polygon = [];
    private Point? _mouse;

    public EditorViewControl(ModelEditor ed, EditorView view, ModelEditorWindow window)
    {
        _ed = ed;
        _view = view;
        _window = window;
        Focusable = true;
        ClipToBounds = true;
        SizeChanged += (_, _) => { if (!_fitted) Fit(); else InvalidateVisual(); };
    }

    private Point ToScreen(double u, double v) => new(u * _scale + _offset.X, v * _scale + _offset.Y);
    private (double U, double V) ToModel(Point p) => ((p.X - _offset.X) / _scale, (p.Y - _offset.Y) / _scale);

    public void Fit()
    {
        if (ActualWidth < 10 || ActualHeight < 10) return;
        var pts = _ed.Model.Bodies.SelectMany(ModelMesh.Body).SelectMany(t => new[] { t.A, t.B, t.C }).Select(p => ModelEditor.Project(_view, p)).ToList();
        if (pts.Count == 0) pts = [(-4, -4, 0), (4, 4, 0)];
        double u0 = pts.Min(p => p.U), u1 = pts.Max(p => p.U), v0 = pts.Min(p => p.V), v1 = pts.Max(p => p.V);
        // Boden mit ins Bild
        if (_view != EditorView.Draufsicht) v1 = Math.Max(v1, 0.5);
        var w = Math.Max(u1 - u0, 2);
        var h = Math.Max(v1 - v0, 2);
        _scale = Math.Clamp(Math.Min(ActualWidth / (w * 1.25), ActualHeight / (h * 1.25)), 2, 2000);
        _offset = new Vector(ActualWidth / 2 - (u0 + u1) / 2 * _scale, ActualHeight / 2 - (v0 + v1) / 2 * _scale);
        _fitted = true;
        InvalidateVisual();
    }

    public void CancelPolygon()
    {
        _polygon.Clear();
        InvalidateVisual();
    }

    public void FinishPolygon()
    {
        if (_view != EditorView.Draufsicht || _polygon.Count == 0) return;
        var pts = _polygon.ToList();
        _polygon.Clear();
        _window.PolygonDone(pts);
    }

    // ---------- Zeichnen ----------

    private static readonly Pen MinorPen = Freeze(new Pen(Theme.GridMinorBrush, 1));
    private static readonly Pen MajorPen = Freeze(new Pen(Theme.GridMajorBrush, 1));
    private static readonly Pen AxisPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0x60, 0x60, 0x60)), 1) { DashStyle = DashStyles.Dash });
    private static readonly Pen GroundPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x7A, 0x8A, 0x60)), 2));
    private static readonly Pen SelectPen = Freeze(new Pen(Theme.AccentBrush, 1.5) { DashStyle = DashStyles.Dash });
    private static readonly Pen AccentPen = Freeze(new Pen(Theme.AccentBrush, 2));

    private static Pen Freeze(Pen p) { p.Freeze(); return p; }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Theme.CanvasBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));
        DrawGrid(dc);

        // Dreiecke aller Körper, hinten zuerst; Helligkeit nach Neigung zum Betrachter
        var accent = new Rgb(Theme.Accent.R, Theme.Accent.G, Theme.Accent.B);
        var tris = new List<(double Depth, Point A, Point B, Point C, Color Fill)>();
        for (var i = 0; i < _ed.Model.Bodies.Count; i++)
        {
            var body = _ed.Model.Bodies[i];
            var (rgb, _) = ModelMesh.Surface(_ed.Model, body, null);
            if (_ed.Selection.Contains(i)) rgb = ModelEditorWindow.Mix(rgb, accent, 0.3);
            foreach (var t in ModelMesh.Body(body))
            {
                var (a, b, c) = (ModelEditor.Project(_view, t.A), ModelEditor.Project(_view, t.B), ModelEditor.Project(_view, t.C));
                var area = (b.U - a.U) * (c.V - a.V) - (c.U - a.U) * (b.V - a.V);
                if (Math.Abs(area) < 1e-10) continue;
                var shade = Shade(t);
                var col = Color.FromRgb((byte)(rgb.R * shade), (byte)(rgb.G * shade), (byte)(rgb.B * shade));
                tris.Add(((a.Depth + b.Depth + c.Depth) / 3 + (body.IsDetail ? 0.02 : 0), ToScreen(a.U, a.V), ToScreen(b.U, b.V), ToScreen(c.U, c.V), col));
            }
        }
        var brushes = new Dictionary<Color, (SolidColorBrush, Pen)>();
        foreach (var t in tris.OrderBy(t => t.Depth))
        {
            if (!brushes.TryGetValue(t.Fill, out var bp))
            {
                var br = new SolidColorBrush(t.Fill); br.Freeze();
                // gleichfarbiger Rand schließt Haarfugen zwischen Dreiecken
                var pen = new Pen(br, 0.6); pen.Freeze();
                brushes[t.Fill] = bp = (br, pen);
            }
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(t.A, true, true);
                ctx.LineTo(t.B, true, false);
                ctx.LineTo(t.C, true, false);
            }
            g.Freeze();
            dc.DrawGeometry(bp.Item1, bp.Item2, g);
        }

        // Auswahl: umgebendes Rechteck
        foreach (var i in _ed.Selection.Where(i => i < _ed.Model.Bodies.Count))
        {
            var pts = ModelMesh.Body(_ed.Model.Bodies[i]).SelectMany(t => new[] { t.A, t.B, t.C }).Select(p => ModelEditor.Project(_view, p)).ToList();
            if (pts.Count == 0) continue;
            var r = new Rect(ToScreen(pts.Min(p => p.U), pts.Min(p => p.V)), ToScreen(pts.Max(p => p.U), pts.Max(p => p.V)));
            r.Inflate(3, 3);
            dc.DrawRectangle(null, SelectPen, r);
        }

        // Ecken des gewählten Vielecks bzw. der Freiform
        if (_ed.Selected is { Shape: BodyShape.Vieleck or BodyShape.Freiform } sel)
        {
            var verts = _ed.Vertices(sel);
            for (var i = 0; i < verts.Count; i++)
            {
                var p = ModelEditor.Project(_view, verts[i]);
                var s = ToScreen(p.U, p.V);
                var on = i == _ed.SelectedVertex;
                dc.DrawRectangle(on ? Theme.AccentBrush : Brushes.White, AccentPen, new Rect(s.X - 4, s.Y - 4, 8, 8));
            }
        }

        // Grundriss in Arbeit
        if (_polygon.Count > 0)
        {
            var pts = _polygon.Select(p => ToScreen(p.X, p.Y)).ToList();
            if (_mouse is { } m) pts.Add(SnapScreen(m));
            for (var i = 1; i < pts.Count; i++) dc.DrawLine(AccentPen, pts[i - 1], pts[i]);
            foreach (var p in pts) dc.DrawEllipse(Brushes.White, AccentPen, p, 3.5, 3.5);
        }

        // Maßstab unten links
        var meters = NiceStep(80 / _scale);
        var len = meters * _scale;
        var y = ActualHeight - 14;
        dc.DrawLine(new Pen(Theme.TextSecondaryBrush, 2), new Point(12, y), new Point(12 + len, y));
        var label = new FormattedText(meters >= 1 ? $"{Fmt.Num(meters, 0)} m" : $"{Fmt.Num(meters * 100, 0)} cm", Lang.IsEnglish ? Fmt.En : Fmt.De, FlowDirection.LeftToRight,
            new Typeface(Theme.Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 10 * Theme.FontScale, Theme.TextSecondaryBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(label, new Point(16 + len, y - label.Height / 2));
    }

    private Point SnapScreen(Point p)
    {
        var (u, v) = ToModel(p);
        return ToScreen(_ed.Snap(u), _ed.Snap(v));
    }

    private double Shade(ModelTriangle t)
    {
        var (a, b, c) = (t.A, t.B, t.C);
        double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z, vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
        double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
        var n = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (n < 1e-12) return 1;
        var facing = Math.Abs(_view switch { EditorView.Draufsicht => nz, EditorView.Vorderansicht => ny, _ => nx }) / n;
        return 0.62 + 0.38 * facing;
    }

    private static double NiceStep(double raw)
    {
        foreach (var s in new[] { 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 50, 100 })
            if (s >= raw) return s;
        return 200;
    }

    private void DrawGrid(DrawingContext dc)
    {
        var minor = NiceStep(12 / _scale);
        var major = minor * (minor is 0.02 or 0.2 or 2 or 20 ? 5 : minor is 0.05 or 0.5 or 5 or 50 ? 2 : 10);
        var (u0, v0) = ToModel(new Point(0, 0));
        var (u1, v1) = ToModel(new Point(ActualWidth, ActualHeight));
        for (var u = Math.Floor(u0 / minor) * minor; u <= u1; u += minor)
        {
            var x = Math.Round(ToScreen(u, 0).X) + 0.5;
            dc.DrawLine(Math.Abs(u / major - Math.Round(u / major)) < 1e-6 ? MajorPen : MinorPen, new Point(x, 0), new Point(x, ActualHeight));
        }
        for (var v = Math.Floor(v0 / minor) * minor; v <= v1; v += minor)
        {
            var y = Math.Round(ToScreen(0, v).Y) + 0.5;
            dc.DrawLine(Math.Abs(v / major - Math.Round(v / major)) < 1e-6 ? MajorPen : MinorPen, new Point(0, y), new Point(ActualWidth, y));
        }
        var o = ToScreen(0, 0);
        dc.DrawLine(AxisPen, new Point(o.X, 0), new Point(o.X, ActualHeight));
        if (_view == EditorView.Draufsicht) dc.DrawLine(AxisPen, new Point(0, o.Y), new Point(ActualWidth, o.Y));
        else dc.DrawLine(GroundPen, new Point(0, o.Y), new Point(ActualWidth, o.Y));
    }

    // ---------- Maus ----------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);
        var (u, v) = ToModel(p);
        if (_window.PolygonMode)
        {
            if (_view != EditorView.Draufsicht) { _window.Say(Lang.T("Den Grundriss bitte in der Draufsicht zeichnen.")); return; }
            if (e.ClickCount >= 2) { FinishPolygon(); return; }
            _polygon.Add((_ed.Snap(u), _ed.Snap(v)));
            InvalidateVisual();
            return;
        }
        _dragStart = p;
        _applied = (0, 0, 0);
        _recorded = false;
        if (_ed.Selected is { Shape: BodyShape.Vieleck or BodyShape.Freiform } && _ed.PickVertex(_view, u, v, 7 / _scale) >= 0)
        {
            _drag = Drag.Vertex;
            _window.Say(Lang.F($"Ecke {_ed.SelectedVertex + 1}: ziehen verschiebt sie."));
        }
        else
        {
            var add = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            var hit = _ed.Pick(_view, u, v, add);
            _drag = hit is { } h && _ed.Selection.Contains(h) ? Drag.Body : Drag.None;
        }
        if (_drag != Drag.None) CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (_window.PolygonMode && _polygon.Count > 0) { FinishPolygon(); e.Handled = true; return; }
        _drag = Drag.Pan;
        _dragStart = e.GetPosition(this);
        _panOrigin = _offset;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        _mouse = p;
        if (_window.PolygonMode && _polygon.Count > 0) InvalidateVisual();
        if (_drag == Drag.Pan) { _offset = _panOrigin + (p - _dragStart); InvalidateVisual(); return; }
        if (_drag is not (Drag.Body or Drag.Vertex) || e.LeftButton != MouseButtonState.Pressed) return;
        var d = p - _dragStart;
        if (!_recorded && d.Length < 3) return;
        var (dx, dy, dz) = ModelEditor.Unproject(_view, d.X / _scale, d.Y / _scale);
        (dx, dy, dz) = (_ed.Snap(dx), _ed.Snap(dy), _ed.Snap(dz));
        var inc = (dx - _applied.DX, dy - _applied.DY, dz - _applied.DZ);
        if (Math.Abs(inc.Item1) + Math.Abs(inc.Item2) + Math.Abs(inc.Item3) < 1e-12) return;
        if (!_recorded) { _ed.Record(); _recorded = true; }
        if (_drag == Drag.Body) _ed.MoveSelection(inc.Item1, inc.Item2, inc.Item3);
        else _ed.MoveVertex(_ed.SelectedVertex, inc.Item1, inc.Item2, inc.Item3);
        _applied = (dx, dy, dz);
        _window.Redraw(preview: true);
        _window.Say(Lang.F($"Verschoben um {Fmt.Num(dx, 2)} / {Fmt.Num(dy, 2)} / {Fmt.Num(dz, 2)} m"));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _mouse = null;
        if (_polygon.Count > 0) InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_drag == Drag.None) return;
        ReleaseMouseCapture();
        var moved = _recorded;
        _drag = Drag.None;
        if (moved) _ed.Commit();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var p = e.GetPosition(this);
        var (u, v) = ToModel(p);
        _scale = Math.Clamp(_scale * (e.Delta > 0 ? 1.15 : 1 / 1.15), 2, 4000);
        _offset = new Vector(p.X - u * _scale, p.Y - v * _scale);
        InvalidateVisual();
        e.Handled = true;
    }
}
