using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TrackMaster.Core;

namespace TrackMaster.App;

/// <summary>Gleisbibliothek: Spur, Hersteller, Suche, Kategorien und die Gleisstücke. Klick setzt an, Ziehen platziert frei.</summary>
public sealed class LibraryPanel : DockPanel
{
    private readonly PlanSession _session;
    private readonly Func<(double X, double Y)> _viewCenter;
    private readonly UniformGrid _gauges = new() { Columns = 6, Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock _gaugeMeta = Theme.Secondary("");
    private readonly ComboBox _systems = new() { Height = 30, Margin = new Thickness(0, 3, 0, 8) };
    private readonly Border _warning;
    private readonly TextBox _search = new() { Height = 30, Padding = new Thickness(6, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly WrapPanel _categories = new() { Margin = new Thickness(0, 8, 0, 4) };
    private readonly TextBlock _count = Theme.Secondary("");
    private readonly ListBox _items = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _headerRight;
    private string _category = Lang.T("Alle");
    private string? _shownGauge;
    private Point _pressPoint;
    private string? _pressKey;

    public LibraryPanel(PlanSession session, Func<(double X, double Y)> viewCenter)
    {
        _session = session;
        _viewCenter = viewCenter;
        Background = Theme.PanelBrush;
        LastChildFill = true;

        var header = Theme.PaneHeader(Lang.T("Gleisbibliothek"), out _headerRight);
        SetDock(header, Dock.Top);
        Children.Add(header);

        var top = new StackPanel { Margin = new Thickness(12, 10, 12, 0) };
        top.Children.Add(Theme.Label(Lang.T("Spurweite"), 11, true, Theme.TextSecondaryBrush));
        top.Children.Add(new Border { Height = 4 });
        top.Children.Add(_gauges);
        top.Children.Add(_gaugeMeta);
        top.Children.Add(new Border { Height = 8 });
        var sysLabel = Theme.Label(Lang.T("Hersteller / Gleissystem"), 11, true, Theme.TextSecondaryBrush);
        top.Children.Add(sysLabel);
        System.Windows.Automation.AutomationProperties.SetName(_systems, Lang.T("Hersteller / Gleissystem"));
        top.Children.Add(_systems);
        _warning = new Border
        {
            Background = Theme.WarningBackBrush, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 8),
            Child = Theme.Label(Lang.T("Geometriedaten vorläufig. Vor dem Bau bitte mit dem Herstellerkatalog abgleichen."), 11, false, Theme.WarningTextBrush),
        };
        top.Children.Add(_warning);

        var searchHost = new Grid();
        var placeholder = Theme.Secondary(Lang.T("Artikel-Nr. oder Name suchen"));
        placeholder.Margin = new Thickness(9, 0, 0, 0);
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        placeholder.IsHitTestVisible = false;
        searchHost.Children.Add(_search);
        searchHost.Children.Add(placeholder);
        System.Windows.Automation.AutomationProperties.SetName(_search, Lang.T("Gleise suchen"));
        _search.TextChanged += (_, _) => { placeholder.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; FillItems(); };
        top.Children.Add(searchHost);
        top.Children.Add(_categories);
        var countRow = new DockPanel { Margin = new Thickness(0, 2, 0, 4) };
        var hint = Theme.Secondary(Lang.T("Klicken oder ziehen"));
        DockPanel.SetDock(hint, Dock.Right);
        countRow.Children.Add(hint);
        countRow.Children.Add(_count);
        top.Children.Add(countRow);
        SetDock(top, Dock.Top);
        Children.Add(top);

        _items.Margin = new Thickness(4, 0, 4, 6);
        ScrollViewer.SetHorizontalScrollBarVisibility(_items, ScrollBarVisibility.Disabled);
        Children.Add(_items);

        _systems.SelectionChanged += (_, _) =>
        {
            if (_systems.SelectedItem is ComboBoxItem { Tag: string id })
            {
                _session.SystemByGauge[_session.Plan.GaugeId] = id;
                FillSystemDependent();
            }
        };
        _items.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _pressPoint = e.GetPosition(_items);
            _pressKey = (e.OriginalSource as DependencyObject) is { } src && ItemsControl.ContainerFromElement(_items, src) is ListBoxItem { Tag: string key } ? key : null;
        };
        _items.PreviewMouseMove += (_, e) =>
        {
            if (_pressKey is null || e.LeftButton != MouseButtonState.Pressed) return;
            if ((e.GetPosition(_items) - _pressPoint).Length < 5) return;
            var key = _pressKey;
            _pressKey = null;
            if (key.StartsWith(FlexTag, StringComparison.Ordinal)) return;
            DragDrop.DoDragDrop(_items, new DataObject(DataFormats.StringFormat, key), DragDropEffects.Copy);
        };
        _items.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (_pressKey is { } key) Use(key);
            _pressKey = null;
        };
        _items.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _items.SelectedItem is ListBoxItem { Tag: string key })
            {
                Use(key);
                e.Handled = true;
            }
        };

        session.Changed += () => { if (_shownGauge != _session.Plan.GaugeId) Refresh(); };
        Refresh();
    }

    /// <summary>Kennung der Flexgleis-Einträge: „flex:“ + Flex-System des Artikels.</summary>
    private const string FlexTag = "flex:";

    private void Use(string key)
    {
        if (!key.StartsWith(FlexTag, StringComparison.Ordinal))
        {
            _session.Attach(key, _viewCenter());
            return;
        }
        var gauge = _session.Plan.Gauge;
        var stock = Catalog.Flexes(gauge, _session.CurrentSystem).FirstOrDefault(f => f.SystemId == key[FlexTag.Length..]) ?? Catalog.Flex(gauge, _session.CurrentSystem);
        // Dieses Flexgleis gilt ab jetzt auch für „Flexgleis ziehen“ und „Lücke schließen“
        _session.CurrentFlex = stock;
        if (Window.GetWindow(this) is { } owner && Dialogs.AskFlex(owner, gauge, stock) is { } flex)
            _session.AttachFlex(flex, _viewCenter());
    }

    public void FocusSearch()
    {
        _search.Focus();
        _search.SelectAll();
    }

    private void Refresh()
    {
        var plan = _session.Plan;
        _shownGauge = plan.GaugeId;
        _gauges.Children.Clear();
        foreach (var g in Catalog.Gauges)
        {
            var on = g.Id == plan.GaugeId;
            var b = new ToggleButton
            {
                Content = g.Id, IsChecked = on, Height = 30, Margin = new Thickness(0, 0, 4, 0), FontWeight = FontWeights.SemiBold,
                Background = on ? Theme.AccentBrush : Brushes.White, Foreground = on ? Brushes.White : Theme.TextBrush,
                BorderBrush = on ? Theme.AccentBrush : Theme.ControlBorderBrush, BorderThickness = new Thickness(1),
                Template = Theme.RoundedTemplate(), ToolTip = Lang.F($"Spur {g.Id} · {g.Scale} · {Fmt.Num(g.TrackWidth)} mm"),
            };
            var id = g.Id;
            b.Click += (_, _) => { if (!_session.SetGauge(id)) b.IsChecked = false; };
            _gauges.Children.Add(b);
        }
        var gauge = plan.Gauge;
        _gaugeMeta.Text = Lang.F($"Spur {gauge.Id} · Maßstab {gauge.Scale} · Spurweite {Fmt.Num(gauge.TrackWidth)} mm");
        _systems.Items.Clear();
        var current = _session.CurrentSystem;
        foreach (var s in gauge.Systems)
        {
            var item = new ComboBoxItem { Content = s.Name, Tag = s.Id };
            _systems.Items.Add(item);
            if (s.Id == current.Id) _systems.SelectedItem = item;
        }
        FillSystemDependent();
    }

    private void FillSystemDependent()
    {
        var sys = _session.CurrentSystem;
        _headerRight.Text = sys.Name;
        _warning.Visibility = sys.Verified ? Visibility.Collapsed : Visibility.Visible;
        var cats = new[] { Lang.T("Alle") }.Concat(sys.Pieces.Select(p => p.Category).Distinct()).Append(Lang.T("Flexgleis")).Distinct().ToList();
        if (!cats.Contains(_category)) _category = Lang.T("Alle");
        _categories.Children.Clear();
        foreach (var c in cats)
        {
            var on = c == _category;
            var b = new ToggleButton
            {
                Content = c, IsChecked = on, Height = 24, Padding = new Thickness(9, 0, 9, 0), Margin = new Thickness(0, 0, 4, 4), FontSize = 11.5,
                Background = on ? Theme.AccentLightBrush : Brushes.White, BorderBrush = on ? Theme.AccentBorderBrush : Theme.ControlBorderBrush,
                BorderThickness = new Thickness(1), FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal, Template = Theme.RoundedTemplate(),
            };
            var cat = c;
            b.Click += (_, _) => { _category = cat; FillSystemDependent(); };
            _categories.Children.Add(b);
        }
        FillItems();
    }

    private void FillItems()
    {
        var sys = _session.CurrentSystem;
        var q = _search.Text.Trim();
        var list = sys.Pieces.Where(p => (_category == Lang.T("Alle") || p.Category == _category)
                                         && (q.Length == 0 || $"{p.Number} {p.Name} {p.Spec}".Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
        _items.Items.Clear();
        var gauge = _session.Plan.Gauge;
        var flexes = _category == Lang.T("Alle") || _category == Lang.T("Flexgleis")
            ? Catalog.Flexes(gauge, sys).Where(f => q.Length == 0 || Lang.F($"Flexgleis {f.Number} {f.Name}").Contains(q, StringComparison.OrdinalIgnoreCase)).ToList()
            : [];
        foreach (var stock in flexes)
        {
            var sample = Catalog.Get(Catalog.FlexCurveKey(gauge.Id, Math.Round(gauge.TrackWidth * 30), 30, stock.SystemId));
            var row = new DockPanel { Margin = new Thickness(2) };
            row.Children.Add(new Border
            {
                Width = 52, Height = 38, CornerRadius = new CornerRadius(4), Background = Theme.CanvasBrush, Margin = new Thickness(0, 0, 10, 0),
                Child = new System.Windows.Controls.Image { Source = TrackDrawing.Thumbnail(sample), Stretch = Stretch.Uniform, Margin = new Thickness(4) },
            });
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(Theme.Label(flexes.Count > 1 ? Lang.F($"Flexgleis {stock.Number}…") : Lang.T("Flexgleis…"), 12, true));
            texts.Children.Add(Theme.Secondary(Lang.F($"{(stock.Number == Lang.T("Flexgleis") ? Lang.T("allgemein") : stock.Number)} · Stück {Fmt.Num(stock.Length, 0)} mm")));
            row.Children.Add(texts);
            _items.Items.Add(new ListBoxItem
            {
                Content = row, Tag = FlexTag + stock.SystemId, Padding = new Thickness(4), Cursor = Cursors.Hand,
                ToolTip = Lang.F($"Flexgleis {stock.Name} · Stücke zu {Fmt.Num(stock.Length, 0)} mm\nKlicken: Länge oder Bogen wählen und am aktiven Gleisende ansetzen\nFrei biegen: Werkzeug „Flexgleis ziehen“ (Register Gleise)"),
            });
        }
        foreach (var p in list)
        {
            var row = new DockPanel { Margin = new Thickness(2) };
            var thumb = new Border
            {
                Width = 52, Height = 38, CornerRadius = new CornerRadius(4), Background = Theme.CanvasBrush, Margin = new Thickness(0, 0, 10, 0),
                Child = new System.Windows.Controls.Image { Source = TrackDrawing.Thumbnail(p), Stretch = Stretch.Uniform, Margin = new Thickness(4) },
            };
            row.Children.Add(thumb);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(Theme.Label(p.Name, 12, true));
            texts.Children.Add(Theme.Secondary($"{p.Number} · {p.Spec}"));
            row.Children.Add(texts);
            _items.Items.Add(new ListBoxItem
            {
                Content = row, Tag = p.Key, Padding = new Thickness(4), Cursor = Cursors.Hand,
                ToolTip = Lang.F($"{p.Name} · {p.Spec} · {sys.Name}\nKlicken: am aktiven Gleisende ansetzen\nZiehen: frei auf der Platte platzieren"),
            });
        }
        var total = list.Count + flexes.Count;
        _count.Text = total == 0 ? Lang.T("Keine passenden Gleise") : Lang.F($"{total} Artikel");
    }
}
