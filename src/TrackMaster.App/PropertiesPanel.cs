using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TrackMaster.Core;

namespace TrackMaster.App;

/// <summary>Eigenschaften des gewählten Gleises bzw. des Plans, wenn nichts gewählt ist.</summary>
public sealed class PropertiesPanel : DockPanel
{
    private readonly PlanSession _session;
    private readonly Action<string> _run;
    private readonly StackPanel _body = new() { Margin = new Thickness(12, 10, 6, 12) };
    private readonly TextBlock _headerRight;
    private bool _pending;

    /// <param name="run">Führt einen Befehl des Hauptfensters aus (z. B. „Stückliste“).</param>
    public PropertiesPanel(PlanSession session, Action<string> run)
    {
        _session = session;
        _run = run;
        Background = Theme.PanelBrush;
        var header = Theme.PaneHeader(Lang.T("Eigenschaften"), out _headerRight);
        SetDock(header, Dock.Top);
        Children.Add(header);
        Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        session.Changed += Refresh;
        Refresh();
    }

    /// <summary>Setzt den Fokus auf die Breite der Anlagenplatte.</summary>
    public void FocusBoard()
    {
        _session.ClearSelection();
        if (_boardWidth is not null) { _boardWidth.Focus(); _boardWidth.SelectAll(); }
    }

    private TextBox? _boardWidth;

    private void Refresh()
    {
        // Nicht neu aufbauen, während jemand tippt; nach dem Verlassen des Felds nachholen
        if (_body.IsKeyboardFocusWithin && Keyboard.FocusedElement is TextBox)
        {
            _pending = true;
            return;
        }
        _pending = false;
        _body.Children.Clear();
        _boardWidth = null;
        var sel = _session.SelectedPieces;
        if (_session.SelectedObject is { } obj && _session.FindObject(obj) is { } o) ObjectView(o);
        else if (sel.Count == 0) PlanView();
        else if (sel.Count == 1) PieceView(sel[0]);
        else MultiView(sel);
    }

    private void Section(string title) => _body.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Theme.TextSecondaryBrush, Margin = new Thickness(0, 6, 0, 6) });

    private void Rows(params (string Key, string Value, System.Windows.Media.Brush? Brush)[] rows)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 6, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
            var k = Theme.Label(rows[i].Key, 12, false, Theme.TextSecondaryBrush);
            var v = Theme.Label(rows[i].Value, 12, rows[i].Brush is not null, rows[i].Brush);
            v.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetRow(k, i); Grid.SetRow(v, i); Grid.SetColumn(v, 1);
            grid.Children.Add(k); grid.Children.Add(v);
        }
        _body.Children.Add(grid);
    }

    private TextBox NumberBox(double value, Action<double> apply, int decimals = 1)
    {
        var box = new TextBox { Text = Fmt.Num(value, decimals) };
        void Commit()
        {
            var v = Fmt.Parse(box.Text);
            if (v is null) { _session.Say(Lang.T("Bitte eine Zahl eingeben.")); box.Text = Fmt.Num(value, decimals); return; }
            if (Math.Abs(v.Value - value) < 1e-9) return;
            _session.Edit(() => apply(v.Value));
        }
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); Keyboard.ClearFocus(); e.Handled = true; } };
        box.LostKeyboardFocus += (_, _) => { Commit(); if (_pending) Dispatcher.BeginInvoke(Refresh); };
        return box;
    }

    private Grid Columns(int n, params UIElement[] items)
    {
        var grid = new Grid();
        for (var i = 0; i < n; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < items.Length; i++) { Grid.SetColumn(items[i], i); grid.Children.Add(items[i]); }
        return grid;
    }

    private WrapPanel Buttons(params Button[] buttons)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var b in buttons) panel.Children.Add(b);
        return panel;
    }

    private Button Cmd(string text, string icon, string command, bool danger = false)
    {
        var b = Theme.Button(text, icon, danger: danger);
        b.Click += (_, _) => _run(command);
        return b;
    }

    private void PlanView()
    {
        var plan = _session.Plan;
        var stats = plan.Statistics(_session.Connections);
        var gauge = plan.Gauge;
        _headerRight.Text = Lang.T("Plan");

        var name = new TextBox { Text = plan.Name };
        void CommitName()
        {
            var n = name.Text.Trim();
            if (n.Length == 0 || n == plan.Name) { name.Text = plan.Name; return; }
            _session.Edit(() => plan.Name = n);
        }
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitName(); Keyboard.ClearFocus(); } };
        name.LostKeyboardFocus += (_, _) => { CommitName(); if (_pending) Dispatcher.BeginInvoke(Refresh); };
        _body.Children.Add(Theme.Field(Lang.T("Planname"), name));

        Section(Lang.T("Anlagenplatte"));
        _boardWidth = NumberBox(plan.BoardWidth, v => plan.BoardWidth = Math.Clamp(v, 50, 50000), 0);
        var height = NumberBox(plan.BoardHeight, v => plan.BoardHeight = Math.Clamp(v, 50, 50000), 0);
        var shape = new ComboBox();
        foreach (var f in BoardGeometry.All) shape.Items.Add(new ComboBoxItem { Content = BoardGeometry.Name(f), Tag = f, IsSelected = f == plan.BoardShape });
        shape.SelectionChanged += (_, _) =>
        {
            if (shape.SelectedItem is not ComboBoxItem { Tag: BoardShape f } || f == plan.BoardShape) return;
            _session.Edit(() =>
            {
                // Modulanlage: die bisherige Platte wird zu Modulen; zurück zu einer festen Form ohne Module
                if (f == BoardShape.Module) { ModuleLayout.ConvertToModules(plan); return; }
                plan.Modules.Clear();
                plan.BoardLegs.Clear();
                plan.BoardShape = f;
                plan.BoardDepth = BoardGeometry.Depth(f, plan.BoardWidth, plan.BoardHeight, plan.BoardDepth, plan.BoardTurn);
            });
        };
        _body.Children.Add(Theme.Field(Lang.T("Form"), shape));
        if (plan.BoardShape == BoardShape.Module)
        {
            _body.Children.Add(Theme.Label(BoardGeometry.Describe(plan), 12, false, Theme.TextSecondaryBrush));
            _body.Children.Add(Buttons(Cmd(Lang.T("Module…"), "Modules", "modules")));
            _boardWidth = null;
        }
        else
            _body.Children.Add(Columns(2, Theme.Field(Lang.T("Breite außen (mm)"), _boardWidth), Theme.Field(Lang.T("Tiefe außen (mm)"), height)));
        if (BoardGeometry.HasLegs(plan.BoardShape))
        {
            var depth = NumberBox(BoardGeometry.Depth(plan.BoardShape, plan.BoardWidth, plan.BoardHeight, plan.BoardDepth, plan.BoardTurn),
                v => plan.BoardDepth = BoardGeometry.Depth(plan.BoardShape, plan.BoardWidth, plan.BoardHeight, v, plan.BoardTurn), 0);
            var turn = new ComboBox();
            foreach (var (t, i) in new[] { Lang.T("Grundstellung"), Lang.T("90° gedreht"), Lang.T("180° gedreht"), Lang.T("270° gedreht") }.Select((t, i) => (t, i)))
                turn.Items.Add(new ComboBoxItem { Content = t, Tag = i, IsSelected = i == plan.BoardTurn });
            turn.SelectionChanged += (_, _) => { if (turn.SelectedItem is ComboBoxItem { Tag: int t } && t != plan.BoardTurn) _session.Edit(() => plan.BoardTurn = t); };
            _body.Children.Add(Columns(2, Theme.Field(Lang.T("Schenkeltiefe (mm)"), depth), Theme.Field(Lang.T("Ausrichtung"), turn)));
            if (plan.BoardShape == BoardShape.U)
            {
                var full = plan.BoardTurn % 2 == 0 ? plan.BoardHeight : plan.BoardWidth;
                var d = BoardGeometry.Depth(plan.BoardShape, plan.BoardWidth, plan.BoardHeight, plan.BoardDepth, plan.BoardTurn);
                var left = NumberBox(BoardGeometry.Leg(plan.BoardLegLeft, d, full), v => plan.BoardLegLeft = v >= full ? 0 : Math.Max(v, d), 0);
                var right = NumberBox(BoardGeometry.Leg(plan.BoardLegRight, d, full), v => plan.BoardLegRight = v >= full ? 0 : Math.Max(v, d), 0);
                _body.Children.Add(Columns(2, Theme.Field(Lang.T("Linker Schenkel lang (mm)"), left), Theme.Field(Lang.T("Rechter Schenkel lang (mm)"), right)));
            }
            _body.Children.Add(Buttons(Cmd(Lang.T("Schenkel einzeln…"), "Resize", "legs")));
        }

        Section(Lang.T("Planprüfung"));
        var vehicle = NumberBox(plan.EffectiveVehicleLength, v =>
            plan.VehicleLength = Math.Abs(v - VehicleDefaults.Length(gauge)) < 0.5 ? 0 : Math.Clamp(v, 20, 5000), 0);
        vehicle.ToolTip = Lang.T("Länge über Puffer des längsten Wagens oder der längsten Lok. Die Planprüfung meldet damit zu enge Bögen, ") +
                          Lang.T("S-Bögen ohne Zwischengerade und wie weit die Wagen im Bogen ausschwenken.");
        var train = new TextBox { Text = plan.TrainLength > 0 ? Fmt.Num(plan.TrainLength, 0) : "" };
        void CommitTrain()
        {
            var t = train.Text.Trim();
            double? v = t.Length == 0 ? 0 : Fmt.Parse(t);
            if (v is null || v < 0) { _session.Say(Lang.T("Bitte eine Zahl eingeben.")); train.Text = plan.TrainLength > 0 ? Fmt.Num(plan.TrainLength, 0) : ""; return; }
            var value = Math.Min(v.Value, 100000);
            if (Math.Abs(value - plan.TrainLength) > 0.01) _session.Edit(() => plan.TrainLength = value);
        }
        train.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitTrain(); Keyboard.ClearFocus(); e.Handled = true; } };
        train.LostKeyboardFocus += (_, _) => { CommitTrain(); if (_pending) Dispatcher.BeginInvoke(Refresh); };
        train.ToolTip = Lang.T("Für die Nutzlänge der Gleise; leer = nicht prüfen");
        _body.Children.Add(Columns(2, Theme.Field(Lang.T("Längstes Fahrzeug (mm)"), vehicle), Theme.Field(Lang.T("Längster Zug (mm)"), train)));

        Section(Lang.T("Übersicht"));
        Rows((Lang.T("Spur"), $"{gauge.Id} · {gauge.Scale} · {Fmt.Num(gauge.TrackWidth)} mm", null),
             (Lang.T("Gleisstücke"), stats.Pieces.ToString(), null),
             (Lang.T("Weichen und Kreuzungen"), stats.Switches.ToString(), null),
             (Lang.T("Gleislänge"), $"{Fmt.Num(stats.TrackLengthMm / 1000, 2)} m", null),
             (Lang.T("Kleinster Radius"), stats.SmallestRadius is { } r ? $"{Fmt.Num(r)} mm" : "–", null),
             (Lang.T("Größte Steigung"), stats.MaxGrade > 0 ? $"{Fmt.Num(stats.MaxGrade * 100, 1)} %" : Lang.T("eben"), stats.MaxGrade * 100 > PlanCheck.WarnGradePercent ? Theme.DangerBrush : null),
             (Lang.T("Höchster Punkt"), $"{Fmt.Num(stats.MaxHeight, 0)} mm", null),
             (Lang.T("Gelände / Elektrik"), Lang.F($"{plan.Scenery.Count} Objekte · {plan.Feeders.Count} Gleisanschl. · {plan.Signals.Count} Signale") + (plan.Detectors.Count > 0 ? Lang.F($" · {plan.Detectors.Count} RM") : ""), null),
             (Lang.T("Offene Gleisenden"), stats.OpenEnds.ToString(), stats.OpenEnds > 0 ? Theme.OpenEndBrush : Theme.SuccessBrush));
        _body.Children.Add(Buttons(Cmd(Lang.T("Stückliste"), "List", "bom"), Cmd(Lang.T("Planprüfung"), "Shield", "check")));
        _body.Children.Add(new Border
        {
            Background = Theme.TipBackBrush, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 8, 6, 0),
            Child = Theme.Label(Lang.T("So geht’s: Klicke ein orangefarbenes Gleisende an und dann ein Gleis in der Bibliothek – es wird dort angesetzt. ") +
                                Lang.T("Gleise lassen sich mit der Maus verschieben und rasten an freien Enden ein. ") +
                                Lang.T("Rechte Maustaste oder Leertaste + Ziehen verschiebt die Ansicht, das Mausrad zoomt."), 11.5, false, Theme.TextSecondaryBrush),
        });
    }

    private void PieceView(PlacedPiece p)
    {
        var d = p.Def;
        _headerRight.Text = Lang.T("1 Gleis gewählt");
        var head = new DockPanel { Margin = new Thickness(0, 0, 6, 10) };
        head.Children.Add(new Border
        {
            Width = 52, Height = 38, CornerRadius = new CornerRadius(4), Background = Theme.CanvasBrush, Margin = new Thickness(0, 0, 10, 0),
            Child = new System.Windows.Controls.Image { Source = TrackDrawing.Thumbnail(d), Margin = new Thickness(4) },
        });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Theme.Label(d.Name, 13, true));
        titles.Children.Add(Theme.Secondary($"{d.SystemName} · {d.Number}"));
        head.Children.Add(titles);
        _body.Children.Add(head);

        if (!d.Verified)
            _body.Children.Add(new Border
            {
                Background = Theme.WarningBackBrush, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 6, 8),
                Child = Theme.Label(Lang.T("Geometrie vorläufig – vor dem Bau mit dem Herstellerkatalog prüfen."), 11, false, Theme.WarningTextBrush),
            });

        var leftHanded = !d.RightHand != p.Flipped;
        switch (d.Kind)
        {
            case PieceKind.Curve:
                Rows((Lang.T("Radius"), $"{Fmt.Num(d.Radius)} mm", null), (Lang.T("Winkel"), $"{Fmt.Num(d.Angle)}°", null), (Lang.T("Bogenlänge"), $"{Fmt.Num(d.TrackLength)} mm", null), (Lang.T("Richtung"), leftHanded ? Lang.T("links") : Lang.T("rechts"), null));
                break;
            case PieceKind.Switch when !d.IsSimpleSwitch:
                Rows((Lang.T("Maße"), d.Spec, null), (Lang.T("Stränge"), $"{d.RouteCount}", null), (Lang.T("Antriebe"), $"{d.Drives}", null));
                break;
            case PieceKind.Switch:
                Rows((Lang.T("Länge"), $"{Fmt.Num(d.Length)} mm", null), (Lang.T("Abzweig"), $"R {Fmt.Num(d.Radius)} · {Fmt.Num(d.Angle)}°", null), (Lang.T("Richtung"), leftHanded ? Lang.T("links") : Lang.T("rechts"), null));
                break;
            case PieceKind.Crossing:
                Rows((Lang.T("Länge"), $"{Fmt.Num(d.Length)} mm", null), (Lang.T("Kreuzungswinkel"), $"{Fmt.Num(d.Angle)}°", null));
                break;
            case PieceKind.Turntable or PieceKind.TransferTable:
                Rows((Lang.T("Maße"), d.Spec, null), (Lang.T("Anschlüsse"), $"{d.RouteCount}", null));
                break;
            default:
                Rows((Lang.T("Länge"), $"{Fmt.Num(d.Length)} mm", null));
                break;
        }

        Section(Lang.T("Position"));
        _body.Children.Add(Columns(3,
            Theme.Field("X (mm)", NumberBox(p.X, v => p.X = v)),
            Theme.Field("Y (mm)", NumberBox(p.Y, v => p.Y = v)),
            Theme.Field(Lang.T("Drehung (°)"), NumberBox(p.Rotation, v => p.Rotation = Angles.Normalize(v)))));
        Section(Lang.T("Höhe"));
        var heights = p.ConnectorHeights();
        _body.Children.Add(Columns(2,
            Theme.Field(Lang.T("Schienenoberkante Anfang (mm)"), NumberBox(p.Height, v => p.Height = v, 1)),
            Theme.Field(Lang.T("Steigung (%)"), NumberBox(p.EffectiveGrade * 100, v => p.Grade = v / 100, 2))));
        if (heights.Count > 1)
            Rows((Lang.T("Höhe am Ende"), $"{Fmt.Num(heights[1], 1)} mm", null));
        var tunnel = new CheckBox { Content = Lang.T("liegt im Tunnel"), IsChecked = p.Tunnel, Margin = new Thickness(0, 0, 0, 8) };
        tunnel.Click += (_, _) => _session.Edit(() => p.Tunnel = tunnel.IsChecked == true);
        _body.Children.Add(tunnel);
        var locked = new CheckBox { Content = Lang.T("gesperrt (nicht verschieben, drehen oder löschen)"), IsChecked = p.Locked, Margin = new Thickness(0, 0, 0, 8) };
        locked.Click += (_, _) => _session.Edit(() => p.Locked = locked.IsChecked == true);
        _body.Children.Add(locked);
        if (UsableLength.For(_session.Plan, p.Id, _session.Connections) is { } run)
            Rows((Lang.T("Nutzlänge"), UsableLength.Describe(_session.Plan, run), _session.Plan.TrainLength > 0 && !run.Closed && run.Length < _session.Plan.TrainLength ? Theme.DangerBrush : null));
        if (p.Group > 0)
            Rows((Lang.T("Gruppe"), Lang.F($"{_session.Plan.Pieces.Count(x => x.Group == p.Group)} Gleise"), null));
        if (d.Drives > 0)
        {
            Section(Lang.T("Digital"));
            var swName = new TextBox { Text = p.Name, MaxLength = 40 };
            void CommitSwitchName() { var n = swName.Text.Trim(); if (n != p.Name) _session.Edit(() => p.Name = n); }
            swName.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitSwitchName(); Keyboard.ClearFocus(); } };
            swName.LostKeyboardFocus += (_, _) => CommitSwitchName();
            _body.Children.Add(Theme.Field(Lang.T("Bezeichnung (z. B. W1)"), swName));
            var addr = NumberBox(p.Address ?? 0, v => p.Address = v >= 1 ? (int)Math.Round(v) : null, 0);
            _body.Children.Add(Columns(2, Theme.Field(Lang.T("Adresse (0 = keine)"), addr)));
        }

        var conns = _session.Connections.Of(p.Id).ToList();
        var linked = conns.Count(c => !c.IsOpen);
        Rows((Lang.T("Verbindungen"), Lang.F($"{linked} von {conns.Count} verbunden"), linked == conns.Count ? Theme.SuccessBrush : Theme.OpenEndBrush));

        if (!d.IsTable)
        {
            Section(Lang.T("Elektrik"));
            _body.Children.Add(new TextBlock { Text = _session.ConnectionSummary(p.Id), TextWrapping = TextWrapping.Wrap, Foreground = Theme.TextSecondaryBrush, FontSize = 12, Margin = new Thickness(0, 0, 6, 4) });
            var hasFeeder = _session.FeedersOn(p.Id).Count > 0;
            _body.Children.Add(Buttons(
                hasFeeder ? Cmd(Lang.T("Gleisanschluss entfernen"), "Delete", "connFeederRemove") : Cmd(Lang.T("Gleisanschluss"), "Bolt", "connFeeder"),
                Cmd(Lang.T("Trennen"), "Isolate", "connIsolate"),
                Cmd(Lang.T("Rückmelder"), "Detector", "connDetector"),
                Cmd(Lang.T("Kabel"), "Wire", "connWire")));
            _body.Children.Add(Tip(Lang.T("Weitere Anschlüsse (Zentrale bzw. Trafo, Trennstelle je Ende, Polung) über das Kontextmenü: rechte Maustaste auf das Gleis.")));
        }

        var buttons = new List<Button> { Cmd(Lang.T("Drehen"), "RotateRight", "rotR") };
        if (d.CanFlip) buttons.Add(Cmd(Lang.T("Spiegeln"), "Mirror", "flip"));
        if (conns.Count > 1) buttons.Add(Cmd(Lang.T("Anschluss wechseln"), "Swap", "swap"));
        buttons.Add(Cmd(Lang.T("Löschen"), "Delete", "delete", danger: true));
        _body.Children.Add(Buttons([.. buttons]));

        if (SwitchInsert.CanReplace(p) && SwitchInsert.Switches(_session.Plan, _session.CurrentSystem.Id) is { Count: > 0 } switches)
        {
            Section(Lang.T("Weiche einsetzen"));
            _body.Children.Add(new TextBlock { Text = Lang.T("Ersetzt dieses Gleis durch eine Weiche – Höhe, Steigung und Ebene bleiben, eine Lücke schließt ein Flexgleis. Auch in der Gleiswendel; in engen Bögen eine Bogenweiche wählen."), TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = Theme.TextSecondaryBrush, Margin = new Thickness(0, 0, 0, 6) });
            var choice = new ComboBox { Height = 28 };
            foreach (var w in switches)
                choice.Items.Add(new ComboBoxItem { Content = $"{w.Number} · {w.Name}" + (w.SystemId == _session.CurrentSystem.Id ? "" : $" ({w.SystemName})"), Tag = w.Key, IsSelected = w.Key == (_insertKey ?? switches[0].Key) });
            if (choice.SelectedItem is null) choice.SelectedIndex = 0;
            choice.SelectionChanged += (_, _) => _insertKey = (choice.SelectedItem as ComboBoxItem)?.Tag as string;
            _body.Children.Add(choice);
            var reverse = new CheckBox { Content = Lang.T("in Gegenrichtung (Weichenspitze am anderen Ende)"), IsChecked = _insertReverse, Margin = new Thickness(0, 6, 0, 4) };
            reverse.Click += (_, _) => _insertReverse = reverse.IsChecked == true;
            var other = new CheckBox { Content = Lang.T("Abzweig auf die andere Seite"), IsChecked = _insertOther, Margin = new Thickness(0, 0, 0, 6) };
            other.Click += (_, _) => _insertOther = other.IsChecked == true;
            _body.Children.Add(reverse);
            _body.Children.Add(other);
            var insert = Theme.Button(Lang.T("Weiche einsetzen"), accent: true);
            insert.Click += (_, _) => _session.InsertSwitch((choice.SelectedItem as ComboBoxItem)?.Tag as string ?? switches[0].Key, _insertReverse, _insertOther);
            _body.Children.Add(Buttons(insert));
        }
    }

    private string? _insertKey;
    private bool _insertReverse, _insertOther;

    private void MultiView(IReadOnlyList<PlacedPiece> sel)
    {
        _headerRight.Text = Lang.F($"{sel.Count} Gleise gewählt");
        Rows((Lang.T("Gleisstücke"), sel.Count.ToString(), null), (Lang.T("Gleislänge"), $"{Fmt.Num(sel.Sum(p => p.Def.TrackLength) / 1000, 2)} m", null));
        // Gemeinsame Höhe: leer, wenn die Gleise unterschiedlich hoch liegen; Eingabe legt alle waagrecht auf diese Höhe
        var common = _session.SelectionHeight;
        var heightBox = new TextBox { Text = common is { } ch ? Fmt.Num(ch, 1) : "", ToolTip = Lang.T("Legt alle gewählten Gleise waagrecht auf diese Schienenoberkante. Für eine Rampe „Steigung…“ verwenden.") };
        void CommitHeight()
        {
            if (heightBox.Text.Trim().Length == 0) return;
            if (Fmt.Parse(heightBox.Text) is not { } z || z < -500 || z > 5000) { _session.Say(Lang.T("Bitte eine Zahl eingeben.")); return; }
            if (common is { } c0 && Math.Abs(c0 - z) < 1e-9) return;
            _session.SetSelectionHeight(z);
        }
        heightBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitHeight(); Keyboard.ClearFocus(); e.Handled = true; } };
        heightBox.LostKeyboardFocus += (_, _) => { CommitHeight(); if (_pending) Dispatcher.BeginInvoke(Refresh); };
        var all = sel.SelectMany(p => p.ConnectorHeights()).ToList();
        Rows((Lang.T("Höhe"), Math.Abs(all.Max() - all.Min()) < 0.05 ? $"{Fmt.Num(all.Min(), 1)} mm" : $"{Fmt.Num(all.Min(), 1)} – {Fmt.Num(all.Max(), 1)} mm", null));
        _body.Children.Add(Columns(2, Theme.Field(Lang.T("Höhe aller gewählten Gleise (mm)"), heightBox)));
        _body.Children.Add(Buttons(Cmd(Lang.T("15° links"), "RotateLeft", "rotL"), Cmd(Lang.T("15° rechts"), "RotateRight", "rotR"), Cmd(Lang.T("Duplizieren"), "Copy", "duplicate"), Cmd(Lang.T("Löschen"), "Delete", "delete", danger: true)));
        _body.Children.Add(Buttons(Cmd(Lang.T("Steigung…"), "Slope", "gradient"), Cmd(sel.All(p => p.Tunnel) ? Lang.T("Tunnel aufheben") : Lang.T("Als Tunnel"), "Tunnel", "tunnel")));
        var grouped = sel.All(p => p.Group > 0 && p.Group == sel[0].Group);
        _body.Children.Add(Buttons(Cmd(sel.All(p => p.Locked) ? Lang.T("Entsperren") : Lang.T("Sperren"), "Lock", "lock"),
            Cmd(grouped ? Lang.T("Gruppe auflösen") : Lang.T("Gruppieren"), grouped ? "Ungroup" : "Group", grouped ? "ungroup" : "group"),
            Cmd(Lang.T("Parallelgleis…"), "Parallel", "parallel"), Cmd(Lang.T("Höhenprofil"), "Profile", "profile")));
        if (sel.Any(p => p.Locked))
            _body.Children.Add(new Border
            {
                Background = Theme.WarningBackBrush, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 6, 6, 0),
                Child = Theme.Label(Lang.F($"{sel.Count(p => p.Locked)} der gewählten Gleise sind gesperrt."), 11, false, Theme.WarningTextBrush),
            });
        _body.Children.Add(new Border
        {
            Background = Theme.TipBackBrush, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 8, 6, 0),
            Child = Theme.Label(Lang.T("Ziehe die Auswahl an ein freies Gleisende – die ganze Gruppe rastet dort ein."), 11.5, false, Theme.TextSecondaryBrush),
        });
    }

    private void ObjectView(object o)
    {
        var plan = _session.Plan;
        switch (o)
        {
            case SceneryItem s:
                _headerRight.Text = s.IsPower ? PowerSupply.TypeName(PowerSupply.Type(s)) : SceneryItem.KindName(s.Kind);
                var name = new TextBox { Text = s.Name };
                void CommitName()
                {
                    var n = name.Text.Trim();
                    if (n.Length == 0 || n == s.Name) { name.Text = s.Name; return; }
                    // Stromquelle: Gleisanschlüsse wandern mit, doppelte Namen werden abgelehnt
                    if (s.IsPower) { _session.RenamePowerSource(s.Id, n); if (s.Name != n) name.Text = s.Name; }
                    else _session.Edit(() => s.Name = n);
                }
                name.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitName(); Keyboard.ClearFocus(); } };
                name.LostKeyboardFocus += (_, _) => { CommitName(); if (_pending) Dispatcher.BeginInvoke(Refresh); };
                _body.Children.Add(Theme.Field(s.Kind == SceneryKind.Label ? "Text" : Lang.T("Name"), name));
                switch (s.Kind)
                {
                    case SceneryKind.Label:
                        _body.Children.Add(Columns(2,
                            Theme.Field(Lang.T("Schrifthöhe (mm)"), NumberBox(s.Height, v => s.Height = Math.Clamp(v, 2, 500), 0)),
                            Theme.Field(Lang.T("Drehung (°)"), NumberBox(s.Rotation, v => s.Rotation = Angles.Normalize(v), 0))));
                        break;
                    case SceneryKind.Dimension when s.Points.Count == 2:
                        _body.Children.Add(Columns(2,
                            Theme.Field(Lang.T("Abstand der Maßlinie (mm)"), NumberBox(s.Width, v => s.Width = Math.Clamp(v, -5000, 5000), 0)),
                            Theme.Field(Lang.T("Schrifthöhe (mm)"), NumberBox(s.Height, v => s.Height = Math.Clamp(v, 2, 500), 0))));
                        Rows((Lang.T("Länge"), $"{Fmt.Num(Annotation.Dimension(s, s.Height).Length, 1)} mm", null));
                        break;
                    case SceneryKind.Beam when s.Points.Count == 2:
                        _body.Children.Add(Columns(2,
                            Theme.Field(Lang.T("Breite (mm)"), NumberBox(s.Width, v => s.Width = Math.Clamp(v, BeamLayout.MinSection, BeamLayout.MaxWidth), 0)),
                            Theme.Field(Lang.T("Höhe der Latte (mm)"), NumberBox(s.Depth, v => s.Depth = Math.Clamp(v, BeamLayout.MinSection, BeamLayout.MaxDepth), 0))));
                        _body.Children.Add(Columns(2,
                            Theme.Field(Lang.T("Länge (mm)"), NumberBox(BeamLayout.Length(s), v => PlanSession.SetBeamLength(s, Math.Clamp(v, BeamLayout.MinLength, 20000)), 0)),
                            Theme.Field(Lang.T("Oberkante (mm)"), NumberBox(s.Height, v => s.Height = Math.Clamp(v, BeamLayout.MinTop, BeamLayout.MaxTop), 0))));
                        Rows((Lang.T("Winkel"), $"{Fmt.Num(BeamLayout.Angle(s), 1)}°", null));
                        break;
                    case SceneryKind.Plate:
                        _body.Children.Add(Columns(2,
                            Theme.Field(Lang.T("Links (mm)"), NumberBox(s.X, v => s.X = Math.Clamp(v, -20000, 20000), 1)),
                            Theme.Field(Lang.T("Oben (mm)"), NumberBox(s.Y, v => s.Y = Math.Clamp(v, -20000, 20000), 1))));
                        _body.Children.Add(Columns(2,
                            Theme.Field(Lang.T("Breite (mm)"), NumberBox(s.Width, v => s.Width = Math.Clamp(v, PlateLayout.MinSize, PlateLayout.MaxSize), 1)),
                            Theme.Field(Lang.T("Tiefe (mm)"), NumberBox(s.Depth, v => s.Depth = Math.Clamp(v, PlateLayout.MinSize, PlateLayout.MaxSize), 1))));
                        _body.Children.Add(Columns(2,
                            Theme.Field(Lang.T("Stärke (mm)"), NumberBox(PlateLayout.Thickness(s), v => s.Variant = (int)Math.Round(Math.Clamp(v, PlateLayout.MinThickness, PlateLayout.MaxThickness)), 0)),
                            Theme.Field(Lang.T("Oberkante (mm)"), NumberBox(s.Height, v => s.Height = Math.Clamp(v, BeamLayout.MinTop, BeamLayout.MaxTop), 0))));
                        Rows((Lang.T("Fläche"), $"{Fmt.Num(s.Width * s.Depth / 1e6, 2)} m²", null));
                        var fit = Theme.Button(Lang.T("An Rahmen anpassen"), "Resize");
                        fit.ToolTip = Lang.T("Legt die Platte wieder genau über die gezeichneten Leisten bzw. den Rahmen.");
                        fit.Click += (_, _) => _session.FitPlate(s);
                        _body.Children.Add(Buttons(fit));
                        _body.Children.Add(Tip(Lang.T("Links und Oben: linke obere Ecke im Plan. Oberkante über der Plattenoberkante des Plans (0 = Gleisebene). Mit Unterbau steht die Platte in der Materialliste.")));
                        break;
                    case SceneryKind.Terrain:
                        _body.Children.Add(Columns(2, Theme.Field(Lang.T("Höhe (mm)"), NumberBox(s.Height, v => s.Height = Math.Clamp(v, -500, 3000), 0))));
                        Rows((Lang.T("Eckpunkte"), s.Points.Count.ToString(), null), (Lang.T("Fläche"), $"{Fmt.Num(Math.Abs(Geometry2D.SignedArea(s.Points)) / 1e6, 2)} m²", null));
                        break;
                    case SceneryKind.Water:
                        _body.Children.Add(Columns(2, Theme.Field(Lang.T("Tiefe (mm)"), NumberBox(s.Height, v => s.Height = Math.Clamp(v, 0, 200), 0))));
                        Rows((Lang.T("Fläche"), $"{Fmt.Num(Math.Abs(Geometry2D.SignedArea(s.Points)) / 1e6, 2)} m²", null));
                        break;
                    case SceneryKind.Building when ModelLibrary.Of(plan, s) is { } model:
                        _headerRight.Text = model.Name;
                        var (heightLabel, trackHeight) = ModelPlacement.HeightInfo(plan, model, s);
                        if (s.Points.Count >= 2)
                        {
                            _body.Children.Add(Columns(2,
                                Theme.Field(Lang.T("Tiefe (mm)"), NumberBox(s.Depth, v => s.Depth = Math.Clamp(v, 0.5, 5000), 1)),
                                Theme.Field(heightLabel, NumberBox(s.Height, v => s.Height = Math.Clamp(v, 0.5, 2000), 1))));
                            Rows((Lang.T("Länge entlang des Gleises"), $"{Fmt.Num(PlacedModel.PathLength(s.Points), 0)} mm", null));
                        }
                        else
                        {
                            _body.Children.Add(Columns(3,
                                Theme.Field(Lang.T("Länge (mm)"), NumberBox(s.Width, v => s.Width = Math.Clamp(v, 0.5, 20000), 1)),
                                Theme.Field(Lang.T("Tiefe (mm)"), NumberBox(s.Depth, v => s.Depth = Math.Clamp(v, 0.5, 5000), 1)),
                                Theme.Field(heightLabel, NumberBox(s.Height, v => s.Height = Math.Clamp(v, 0.5, 2000), 1))));
                            _body.Children.Add(Columns(3, Theme.Field(Lang.T("Drehung (°)"), NumberBox(s.Rotation, v => s.Rotation = Angles.Normalize(v), 0))));
                        }
                        if (trackHeight is not null) Rows((Lang.T("Gleishöhe"), trackHeight, null));
                        var colors = new ComboBox { Height = 28 };
                        foreach (var (cname, hex) in ModelLibrary.MainColors)
                            colors.Items.Add(new ComboBoxItem { Content = cname, Tag = hex ?? "", IsSelected = string.Equals(hex ?? "", s.Color ?? "", StringComparison.OrdinalIgnoreCase) });
                        if (colors.SelectedItem is null && !string.IsNullOrEmpty(s.Color)) colors.Items.Add(new ComboBoxItem { Content = s.Color, Tag = s.Color, IsSelected = true });
                        colors.SelectionChanged += (_, _) => { if (colors.SelectedItem is ComboBoxItem { Tag: string hex } && hex != (s.Color ?? "")) _session.SetModelColor(s.Id, hex); };
                        _body.Children.Add(Theme.Field(Lang.F($"Hauptfarbe ({ModelBody.MaterialName(model.EffectiveMainMaterial)})"), colors));
                        var dims = model.Size;
                        Rows((Lang.T("Vorbild"), Lang.F($"{Fmt.Num(dims.W, 1)} × {Fmt.Num(dims.D, 1)} × {Fmt.Num(dims.H, 1)} m"), null),
                             (Lang.T("Maßstab"), plan.Gauge.Scale, null));
                        var reset = Theme.Button(Lang.T("Originalgröße"), "Resize");
                        reset.Click += (_, _) => _session.ResetModelSize(s.Id);
                        var editModel = Theme.Button(Lang.T("Modell bearbeiten"), "Palette");
                        editModel.ToolTip = model.IsBuiltin
                            ? Lang.T("Pro: öffnet eine Kopie des Grundmodells im Modell-Editor; dieses Objekt bekommt danach die eigene Fassung.")
                            : Lang.T("Pro: öffnet das Modell im Modell-Editor; Änderungen gelten für alle Objekte mit diesem Modell.");
                        editModel.Click += (_, _) => _run("modelEditSelected");
                        _body.Children.Add(Buttons(reset, editModel));
                        if (model.Snap == ModelSnap.Neben)
                        {
                            var flip = Theme.Button(Lang.T("Seite wechseln"), "Mirror");
                            flip.ToolTip = Lang.T("Setzt das Modell auf die andere Seite des Gleises.");
                            flip.Click += (_, _) => _session.FlipModelSide(s.Id);
                            var both = Theme.Button(Lang.T("Beidseitig"), "Copy");
                            both.ToolTip = Lang.T("Setzt dasselbe Modell zusätzlich auf die andere Seite des Gleises.");
                            both.Click += (_, _) => _session.AddModelOtherSide(s.Id);
                            _body.Children.Add(Buttons(flip, both));
                        }
                        if (!string.IsNullOrWhiteSpace(model.Description)) _body.Children.Add(Tip(model.Description));
                        break;
                    case SceneryKind.Building:
                        _body.Children.Add(Columns(3,
                            Theme.Field(Lang.T("Breite (mm)"), NumberBox(s.Width, v => s.Width = Math.Clamp(v, 5, 5000), 0)),
                            Theme.Field(Lang.T("Tiefe (mm)"), NumberBox(s.Depth, v => s.Depth = Math.Clamp(v, 5, 5000), 0)),
                            Theme.Field(Lang.T("Höhe (mm)"), NumberBox(s.Height, v => s.Height = Math.Clamp(v, 5, 2000), 0))));
                        _body.Children.Add(Columns(3, Theme.Field(Lang.T("Drehung (°)"), NumberBox(s.Rotation, v => s.Rotation = Angles.Normalize(v), 0))));
                        break;
                    case SceneryKind.Room:
                        _body.Children.Add(Columns(2, Theme.Field(Lang.T("Wandstärke (mm)"), NumberBox(s.Width, v => s.Width = Math.Clamp(v, 0, 1000), 0))));
                        Rows((Lang.T("Eckpunkte"), s.Points.Count.ToString(), null), (Lang.T("Grundfläche"), $"{Fmt.Num(Math.Abs(Geometry2D.SignedArea(s.Points)) / 1e6, 2)} m²", null));
                        break;
                    case SceneryKind.Obstacle:
                        var kind = new ComboBox { Height = 28 };
                        foreach (var t in Enum.GetValues<ObstacleType>()) kind.Items.Add(new ComboBoxItem { Content = SceneryItem.ObstacleName(t), Tag = t, IsSelected = t == s.Obstacle });
                        kind.SelectionChanged += (_, _) => { if (kind.SelectedItem is ComboBoxItem { Tag: ObstacleType t } && t != s.Obstacle) _session.Edit(() => s.Variant = (int)t); };
                        _body.Children.Add(Theme.Field(Lang.T("Art"), kind));
                        _body.Children.Add(Columns(2,
                            Theme.Field(Lang.T("Breite (mm)"), NumberBox(s.Width, v => s.Width = Math.Clamp(v, 5, 20000), 0)),
                            Theme.Field(Lang.T("Tiefe (mm)"), NumberBox(s.Depth, v => s.Depth = Math.Clamp(v, 5, 20000), 0))));
                        _body.Children.Add(Columns(2,
                            Theme.Field(Lang.T("Höhe (mm)"), NumberBox(s.Height, v => s.Height = Math.Clamp(v, 0, 5000), 0)),
                            Theme.Field(Lang.T("Drehung (°)"), NumberBox(s.Rotation, v => s.Rotation = Angles.Normalize(v), 0))));
                        if (s.Obstacle == ObstacleType.Tuer)
                            _body.Children.Add(Tip(Lang.T("Die Tür ist oben links angeschlagen und schlägt nach unten auf; die Tiefe ist der Schwenkbereich. Mit der Drehung (90°, 180°, 270°) an die Wand anpassen.")));
                        break;
                    case SceneryKind.Wire:
                        var current = Electrics.Current(plan);
                        var use = new ComboBox { Height = 28 };
                        foreach (var u in Enum.GetValues<WireUse>()) use.Items.Add(new ComboBoxItem { Content = WireLayout.UseName(u), Tag = u, IsSelected = u == WireLayout.Use(s) });
                        use.SelectionChanged += (_, _) => { if (use.SelectedItem is ComboBoxItem { Tag: WireUse u } && u != WireLayout.Use(s)) _session.SetWireUse(s.Id, u); };
                        _body.Children.Add(Theme.Field(Lang.T("Verwendung"), use));
                        var wireHex = WireLayout.ColorOf(s, current);
                        var color = new ComboBox { Height = 28 };
                        foreach (var c in WireLayout.Colors)
                            color.Items.Add(new ComboBoxItem { Content = Lang.T(c.Name), Tag = c.Hex, IsSelected = string.Equals(c.Hex, wireHex, StringComparison.OrdinalIgnoreCase) });
                        color.SelectionChanged += (_, _) => { if (color.SelectedItem is ComboBoxItem { Tag: string h } && !string.Equals(h, s.Color, StringComparison.OrdinalIgnoreCase)) _session.Edit(() => s.Color = h); };
                        _body.Children.Add(Columns(2,
                            Theme.Field(Lang.T("Aderfarbe"), color),
                            Theme.Field(Lang.T("Querschnitt (mm²)"), NumberBox(WireLayout.Section(s, plan.Gauge), v => s.Depth = Math.Clamp(v, 0.04, 10), 2))));
                        var len = WireLayout.Length(s);
                        Rows((Lang.T("Länge"), $"{Fmt.Num(len / 1000, 2)} m", null),
                             (Lang.T("Mit Zugabe"), $"{Fmt.Num(len * (1 + WireLayout.Reserve) / 1000, 2)} m", null),
                             (Lang.T("Anfang"), WireLayout.EndAnchor(plan, s, false)?.Text ?? Lang.T("frei"), null),
                             (Lang.T("Ende"), WireLayout.EndAnchor(plan, s, true)?.Text ?? Lang.T("frei"), null),
                             (Lang.T("Punkte"), s.Points.Count.ToString(), null));
                        _body.Children.Add(Tip(Electrics.CurrentHint(plan)));
                        break;
                    case SceneryKind.Power:
                        var ptype = PowerSupply.Type(s);
                        var pkind = new ComboBox { Height = 28 };
                        foreach (var t in Enum.GetValues<PowerSourceType>()) pkind.Items.Add(new ComboBoxItem { Content = PowerSupply.TypeName(t), Tag = t, IsSelected = t == ptype });
                        pkind.SelectionChanged += (_, _) => { if (pkind.SelectedItem is ComboBoxItem { Tag: PowerSourceType t }) _session.SetPowerType(s.Id, t); };
                        _body.Children.Add(Columns(2,
                            Theme.Field(Lang.T("Art"), pkind),
                            Theme.Field(Lang.T("Drehung (°)"), NumberBox(s.Rotation, v => s.Rotation = Angles.Normalize(v), 0))));
                        var prows = new List<(string, string, System.Windows.Media.Brush?)>();
                        if (ptype == PowerSourceType.Verteiler)
                        {
                            var dcolor = new ComboBox { Height = 28 };
                            foreach (var c in PowerSupply.DistributorColors)
                                dcolor.Items.Add(new ComboBoxItem { Content = Lang.T(c.Name), Tag = c.Hex, IsSelected = string.Equals(c.Hex, PowerSupply.DistributorColor(s), StringComparison.OrdinalIgnoreCase) });
                            dcolor.SelectionChanged += (_, _) => { if (dcolor.SelectedItem is ComboBoxItem { Tag: string h }) _session.SetDistributorColor(s.Id, h); };
                            _body.Children.Add(Theme.Field(Lang.T("Farbe"), dcolor));
                            prows.Add((Lang.T("Klemmen"), PowerSupply.Clamps.ToString(), null));
                            prows.Add((Lang.T("Kabel"), PowerSupply.ClampPoints(s).SelectMany(c => WireLayout.WiresAt(plan, c)).DistinctBy(w => w.Id).Count().ToString(), null));
                        }
                        else prows.Add((Lang.T("Betrieb"), PowerSupply.ModeName(plan.Operation), null));
                        if (PowerSupply.GroundTerminal(s) is { } gt)
                        {
                            var grounded = WireLayout.WiresAt(plan, gt).Any();
                            prows.Add((Lang.T("Masse (0)"), grounded ? Lang.T("angeschlossen") : Lang.T("fehlt"), grounded ? null : Theme.OpenEndBrush));
                        }
                        if (PowerSupply.FeedsTrack(ptype))
                        {
                            var psections = Electrics.Sections(plan, _session.Connections).Where(x => x.Feeders.Any(fd => fd.Circuit == s.Name)).ToList();
                            prows.Add((Lang.T("Gleisanschlüsse"), PowerSupply.FeedersOf(plan, s).Count().ToString(), null));
                            prows.Add((Lang.T("Stromabschnitte"), psections.Count == 0 ? "–" : string.Join(", ", psections.Select(x => x.Number)), null));
                            prows.Add((Lang.T("Gleislänge"), $"{Fmt.Num(psections.Sum(x => x.LengthMm) / 1000, 2)} m", null));
                            if (!PowerSupply.Fits(ptype, plan.Operation))
                                prows.Add((Lang.T("Hinweis"), plan.Operation == PowerMode.Analog ? Lang.T("Passt nicht zum Analogbetrieb.") : Lang.T("Passt nicht zum Digitalbetrieb."), Theme.OpenEndBrush));
                        }
                        Rows(prows.ToArray());
                        _body.Children.Add(Tip(PowerSupply.Description(ptype) + (ptype == PowerSourceType.Verteiler ? "" : " " + (PowerSupply.HasGround(ptype)
                            ? Lang.T("Die rote Linie führt vom Bahnstrom (B) zu den Gleisanschlüssen. Die Masse (0) daneben mit einem braunen Kabel zum Gleis oder zu einem Masse-Verteiler legen.")
                            : Lang.T("Gleisanschlüsse hängen über den Namen daran (gestrichelte Linie); Kabel rasten an der Klemme unten ein.")))));
                        break;
                    case SceneryKind.Tree:
                        _body.Children.Add(Columns(2,
                            Theme.Field(Lang.T("Durchmesser (mm)"), NumberBox(s.Width, v => s.Width = Math.Clamp(v, 3, 1000), 0)),
                            Theme.Field(Lang.T("Höhe (mm)"), NumberBox(s.Height, v => s.Height = Math.Clamp(v, 5, 2000), 0))));
                        break;
                }
                _body.Children.Add(Buttons(Cmd(Lang.T("Löschen"), "Delete", "delete", danger: true)));
                _body.Children.Add(Tip(Lang.T("Ziehen verschiebt das Objekt. Entf löscht es.")));
                break;

            case Feeder f:
                _headerRight.Text = Lang.T("Gleisanschluss");
                var circuit = new ComboBox { IsEditable = true, Text = f.Circuit, Height = 28 };
                foreach (var c in _session.CircuitNames.Append(f.Circuit).Append(_session.NewCircuitName()).Distinct())
                    circuit.Items.Add(c);
                void CommitCircuit()
                {
                    var t = circuit.Text.Trim();
                    if (t.Length == 0 || t == f.Circuit) return;
                    _session.SetFeederCircuit(f.Id, t);
                }
                circuit.LostKeyboardFocus += (_, _) => { CommitCircuit(); if (_pending) Dispatcher.BeginInvoke(Refresh); };
                circuit.SelectionChanged += (_, _) => { if (circuit.SelectedItem is string sel) { circuit.Text = sel; CommitCircuit(); } };
                _body.Children.Add(Theme.Label(plan.Operation == PowerMode.Analog ? Lang.T("Hängt an (Trafo)") : Lang.T("Hängt an (Zentrale / Booster)"), 11, false, Theme.TextSecondaryBrush));
                _body.Children.Add(circuit);
                if (PowerSupply.SourceOf(plan, f) is null)
                    _body.Children.Add(Theme.Label(Lang.F($"Für „{f.Circuit}“ steht noch keine Zentrale bzw. kein Trafo im Plan."), 11, false, Theme.OpenEndBrush));
                _body.Children.Add(new Border { Height = 8 });
                var section = Electrics.Sections(plan, _session.Connections).FirstOrDefault(x => x.PieceIds.Contains(f.PieceId));
                Rows((Lang.T("Gleis"), $"{plan.Find(f.PieceId)?.Def.Number} (Nr. {f.PieceId})", null),
                     (Lang.T("Stromabschnitt"), section is null ? "–" : $"{section.Number} · {Fmt.Num(section.LengthMm / 1000, 2)} m", null),
                     (Lang.T("Stromart"), Electrics.CurrentName(Electrics.Current(plan)), null));
                if (Electrics.Current(plan) == CurrentKind.Gleichstrom)
                {
                    var reversed = new CheckBox { Content = Lang.T("Polung getauscht (Plus und Minus vertauscht)"), IsChecked = f.Reversed, Margin = new Thickness(0, 0, 0, 8) };
                    reversed.Click += (_, _) => _session.ToggleFeederPolarity(f.Id);
                    _body.Children.Add(reversed);
                }
                _body.Children.Add(Buttons(Cmd(Lang.T("Löschen"), "Delete", "delete", danger: true)));
                break;

            case Detector det:
                _headerRight.Text = Lang.T("Rückmelder");
                var dname = new TextBox { Text = det.Name };
                void CommitDetectorName()
                {
                    var n = dname.Text.Trim();
                    if (n == det.Name) return;
                    _session.Edit(() => det.Name = n);
                }
                dname.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitDetectorName(); Keyboard.ClearFocus(); } };
                dname.LostKeyboardFocus += (_, _) => { CommitDetectorName(); if (_pending) Dispatcher.BeginInvoke(Refresh); };
                _body.Children.Add(Theme.Field(Lang.T("Name"), dname));
                var dtype = new ComboBox { Height = 28 };
                foreach (var t in Enum.GetValues<DetectorType>()) dtype.Items.Add(new ComboBoxItem { Content = Detector.TypeName(t), Tag = t, IsSelected = t == det.Type });
                dtype.SelectionChanged += (_, _) => { if (dtype.SelectedItem is ComboBoxItem { Tag: DetectorType t } && t != det.Type) _session.Edit(() => det.Type = t); };
                _body.Children.Add(Theme.Field(Lang.T("Art"), dtype));
                _body.Children.Add(Theme.Field(Lang.T("Kontakt am Rückmeldemodul (0 = keiner)"), NumberBox(det.Address ?? 0, v => det.Address = v >= 1 ? (int)Math.Round(Math.Min(v, 4096)) : null, 0)));
                var drows = new List<(string, string, System.Windows.Media.Brush?)>();
                if (det.Address is { } da) drows.Add((Lang.T("Modul / Eingang"), Lang.F($"{(da - 1) / Detector.InputsPerModule + 1} / {(da - 1) % Detector.InputsPerModule + 1}"), null));
                drows.Add((Lang.T("Gleis"), $"{plan.Find(det.PieceId)?.Def.Number} (Nr. {det.PieceId})", null));
                if (UsableLength.For(plan, det.PieceId, _session.Connections) is { } drun) drows.Add((Lang.T("Meldeabschnitt"), $"{Fmt.Num(drun.Length, 0)} mm", null));
                Rows([.. drows]);
                _body.Children.Add(Buttons(Cmd(Lang.T("Löschen"), "Delete", "delete", danger: true)));
                break;

            case Isolation i:
                _headerRight.Text = Lang.T("Trennstelle");
                Rows((Lang.T("Gleis"), $"{plan.Find(i.PieceId)?.Def.Number} (Nr. {i.PieceId})", null), (Lang.T("Gleisende"), i.Index.ToString(), null),
                     (Lang.T("Ausführung"), Electrics.IsThreeRail(plan) ? Lang.T("Mittelleiter getrennt") : Lang.T("beide Schienen isoliert"), null));
                _body.Children.Add(Buttons(Cmd(Lang.T("Löschen"), "Delete", "delete", danger: true)));
                break;

            case Signal g:
                _headerRight.Text = "Signal";
                var sname = new TextBox { Text = g.Name };
                void CommitSignalName()
                {
                    var n = sname.Text.Trim();
                    if (n == g.Name) return;
                    _session.Edit(() => g.Name = n);
                }
                sname.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitSignalName(); Keyboard.ClearFocus(); } };
                sname.LostKeyboardFocus += (_, _) => { CommitSignalName(); if (_pending) Dispatcher.BeginInvoke(Refresh); };
                _body.Children.Add(Theme.Field(Lang.T("Name"), sname));
                var type = new ComboBox { Height = 28 };
                foreach (var t in Enum.GetValues<SignalType>()) type.Items.Add(new ComboBoxItem { Content = Signal.TypeName(t), Tag = t, IsSelected = t == g.Type });
                type.SelectionChanged += (_, _) => { if (type.SelectedItem is ComboBoxItem { Tag: SignalType t } && t != g.Type) _session.Edit(() => g.Type = t); };
                _body.Children.Add(Theme.Label(Lang.T("Art"), 11, false, Theme.TextSecondaryBrush));
                _body.Children.Add(type);
                var system = new ComboBox { Height = 28 };
                foreach (var sy in Enum.GetValues<SignalSystem>()) system.Items.Add(new ComboBoxItem { Content = Signal.SystemName(sy), Tag = sy, IsSelected = sy == g.System });
                system.SelectionChanged += (_, _) => { if (system.SelectedItem is ComboBoxItem { Tag: SignalSystem sy } && sy != g.System) _session.Edit(() => g.System = sy); };
                _body.Children.Add(Theme.Label(Lang.T("Signalsystem"), 11, false, Theme.TextSecondaryBrush));
                _body.Children.Add(system);
                var face = SignalFace.For(g);
                var picture = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
                picture.Children.Add(new System.Windows.Controls.Image { Source = PlanScene.SignalImage(g.Type, g.System), Width = 34, Height = 88, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top });
                var faceText = new StackPanel();
                faceText.Children.Add(Theme.Label(face.Aspect, 12, true));
                faceText.Children.Add(new TextBlock { Text = face.Description, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = Theme.TextSecondaryBrush });
                picture.Children.Add(faceText);
                _body.Children.Add(picture);
                _body.Children.Add(new Border { Height = 8 });
                _body.Children.Add(Columns(2, Theme.Field(Lang.T("Adresse (0 = keine)"), NumberBox(g.Address ?? 0, v => g.Address = v >= 1 ? (int)Math.Round(v) : null, 0))));
                Rows((Lang.T("Belegt Adressen"), g.AddressCount.ToString(), null), (Lang.T("Gleis"), Lang.F($"{plan.Find(g.PieceId)?.Def.Number} (Nr. {g.PieceId}), Ende {g.Index}"), null));
                _body.Children.Add(Buttons(Cmd(Lang.T("Löschen"), "Delete", "delete", danger: true)));
                break;
        }
    }

    private Border Tip(string text) => new()
    {
        Background = Theme.TipBackBrush, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 8, 6, 0),
        Child = Theme.Label(text, 11.5, false, Theme.TextSecondaryBrush),
    };
}
