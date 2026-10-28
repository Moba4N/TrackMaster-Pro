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
        var header = Theme.PaneHeader("Eigenschaften", out _headerRight);
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
            if (v is null) { _session.Say("Bitte eine Zahl eingeben."); box.Text = Fmt.Num(value, decimals); return; }
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
        _headerRight.Text = "Plan";

        var name = new TextBox { Text = plan.Name };
        void CommitName()
        {
            var n = name.Text.Trim();
            if (n.Length == 0 || n == plan.Name) { name.Text = plan.Name; return; }
            _session.Edit(() => plan.Name = n);
        }
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitName(); Keyboard.ClearFocus(); } };
        name.LostKeyboardFocus += (_, _) => { CommitName(); if (_pending) Dispatcher.BeginInvoke(Refresh); };
        _body.Children.Add(Theme.Field("Planname", name));

        Section("Anlagenplatte");
        _boardWidth = NumberBox(plan.BoardWidth, v => plan.BoardWidth = Math.Clamp(v, 100, 50000), 0);
        var height = NumberBox(plan.BoardHeight, v => plan.BoardHeight = Math.Clamp(v, 100, 50000), 0);
        var shape = new ComboBox();
        foreach (var f in BoardGeometry.All) shape.Items.Add(new ComboBoxItem { Content = BoardGeometry.Name(f), Tag = f, IsSelected = f == plan.BoardShape });
        shape.SelectionChanged += (_, _) =>
        {
            if (shape.SelectedItem is not ComboBoxItem { Tag: BoardShape f } || f == plan.BoardShape) return;
            _session.Edit(() =>
            {
                plan.BoardShape = f;
                plan.BoardDepth = BoardGeometry.Depth(f, plan.BoardWidth, plan.BoardHeight, plan.BoardDepth, plan.BoardTurn);
            });
        };
        _body.Children.Add(Theme.Field("Form", shape));
        _body.Children.Add(Columns(2, Theme.Field("Breite außen (mm)", _boardWidth), Theme.Field("Tiefe außen (mm)", height)));
        if (BoardGeometry.HasLegs(plan.BoardShape))
        {
            var depth = NumberBox(BoardGeometry.Depth(plan.BoardShape, plan.BoardWidth, plan.BoardHeight, plan.BoardDepth, plan.BoardTurn),
                v => plan.BoardDepth = BoardGeometry.Depth(plan.BoardShape, plan.BoardWidth, plan.BoardHeight, v, plan.BoardTurn), 0);
            var turn = new ComboBox();
            foreach (var (t, i) in new[] { "Grundstellung", "90° gedreht", "180° gedreht", "270° gedreht" }.Select((t, i) => (t, i)))
                turn.Items.Add(new ComboBoxItem { Content = t, Tag = i, IsSelected = i == plan.BoardTurn });
            turn.SelectionChanged += (_, _) => { if (turn.SelectedItem is ComboBoxItem { Tag: int t } && t != plan.BoardTurn) _session.Edit(() => plan.BoardTurn = t); };
            _body.Children.Add(Columns(2, Theme.Field("Schenkeltiefe (mm)", depth), Theme.Field("Ausrichtung", turn)));
            if (plan.BoardShape == BoardShape.U)
            {
                var full = plan.BoardTurn % 2 == 0 ? plan.BoardHeight : plan.BoardWidth;
                var d = BoardGeometry.Depth(plan.BoardShape, plan.BoardWidth, plan.BoardHeight, plan.BoardDepth, plan.BoardTurn);
                var left = NumberBox(BoardGeometry.Leg(plan.BoardLegLeft, d, full), v => plan.BoardLegLeft = v >= full ? 0 : Math.Max(v, d), 0);
                var right = NumberBox(BoardGeometry.Leg(plan.BoardLegRight, d, full), v => plan.BoardLegRight = v >= full ? 0 : Math.Max(v, d), 0);
                _body.Children.Add(Columns(2, Theme.Field("Linker Schenkel lang (mm)", left), Theme.Field("Rechter Schenkel lang (mm)", right)));
            }
        }

        Section("Übersicht");
        Rows(("Spur", $"{gauge.Id} · {gauge.Scale} · {Fmt.Num(gauge.TrackWidth)} mm", null),
             ("Gleisstücke", stats.Pieces.ToString(), null),
             ("Weichen und Kreuzungen", stats.Switches.ToString(), null),
             ("Gleislänge", $"{Fmt.Num(stats.TrackLengthMm / 1000, 2)} m", null),
             ("Kleinster Radius", stats.SmallestRadius is { } r ? $"{Fmt.Num(r)} mm" : "–", null),
             ("Größte Steigung", stats.MaxGrade > 0 ? $"{Fmt.Num(stats.MaxGrade * 100, 1)} %" : "eben", stats.MaxGrade * 100 > PlanCheck.WarnGradePercent ? Theme.DangerBrush : null),
             ("Höchster Punkt", $"{Fmt.Num(stats.MaxHeight, 0)} mm", null),
             ("Gelände / Elektrik", $"{plan.Scenery.Count} Objekte · {plan.Feeders.Count} Einsp. · {plan.Signals.Count} Signale", null),
             ("Offene Gleisenden", stats.OpenEnds.ToString(), stats.OpenEnds > 0 ? Theme.OpenEndBrush : Theme.SuccessBrush));
        _body.Children.Add(Buttons(Cmd("Stückliste", "List", "bom"), Cmd("Planprüfung", "Shield", "check")));
        _body.Children.Add(new Border
        {
            Background = Theme.TipBackBrush, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 8, 6, 0),
            Child = Theme.Label("So geht’s: Klicke ein orangefarbenes Gleisende an und dann ein Gleis in der Bibliothek – es wird dort angesetzt. " +
                                "Gleise lassen sich mit der Maus verschieben und rasten an freien Enden ein. " +
                                "Rechte Maustaste oder Leertaste + Ziehen verschiebt die Ansicht, das Mausrad zoomt.", 11.5, false, Theme.TextSecondaryBrush),
        });
    }

    private void PieceView(PlacedPiece p)
    {
        var d = p.Def;
        _headerRight.Text = "1 Gleis gewählt";
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
                Child = Theme.Label("Geometrie vorläufig – vor dem Bau mit dem Herstellerkatalog prüfen.", 11, false, Theme.WarningTextBrush),
            });

        var leftHanded = !d.RightHand != p.Flipped;
        switch (d.Kind)
        {
            case PieceKind.Curve:
                Rows(("Radius", $"{Fmt.Num(d.Radius)} mm", null), ("Winkel", $"{Fmt.Num(d.Angle)}°", null), ("Bogenlänge", $"{Fmt.Num(d.TrackLength)} mm", null), ("Richtung", leftHanded ? "links" : "rechts", null));
                break;
            case PieceKind.Switch when !d.IsSimpleSwitch:
                Rows(("Maße", d.Spec, null), ("Stränge", $"{d.RouteCount}", null), ("Antriebe", $"{d.Drives}", null));
                break;
            case PieceKind.Switch:
                Rows(("Länge", $"{Fmt.Num(d.Length)} mm", null), ("Abzweig", $"R {Fmt.Num(d.Radius)} · {Fmt.Num(d.Angle)}°", null), ("Richtung", leftHanded ? "links" : "rechts", null));
                break;
            case PieceKind.Crossing:
                Rows(("Länge", $"{Fmt.Num(d.Length)} mm", null), ("Kreuzungswinkel", $"{Fmt.Num(d.Angle)}°", null));
                break;
            case PieceKind.Turntable or PieceKind.TransferTable:
                Rows(("Maße", d.Spec, null), ("Anschlüsse", $"{d.RouteCount}", null));
                break;
            default:
                Rows(("Länge", $"{Fmt.Num(d.Length)} mm", null));
                break;
        }

        Section("Position");
        _body.Children.Add(Columns(3,
            Theme.Field("X (mm)", NumberBox(p.X, v => p.X = v)),
            Theme.Field("Y (mm)", NumberBox(p.Y, v => p.Y = v)),
            Theme.Field("Drehung (°)", NumberBox(p.Rotation, v => p.Rotation = Angles.Normalize(v)))));
        Section("Höhe");
        var heights = p.ConnectorHeights();
        _body.Children.Add(Columns(2,
            Theme.Field("Schienenoberkante Anfang (mm)", NumberBox(p.Height, v => p.Height = v, 1)),
            Theme.Field("Steigung (%)", NumberBox(p.EffectiveGrade * 100, v => p.Grade = v / 100, 2))));
        if (heights.Count > 1)
            Rows(("Höhe am Ende", $"{Fmt.Num(heights[1], 1)} mm", null));
        var tunnel = new CheckBox { Content = "liegt im Tunnel", IsChecked = p.Tunnel, Margin = new Thickness(0, 0, 0, 8) };
        tunnel.Click += (_, _) => _session.Edit(() => p.Tunnel = tunnel.IsChecked == true);
        _body.Children.Add(tunnel);
        if (d.Drives > 0)
        {
            Section("Digital");
            var addr = NumberBox(p.Address ?? 0, v => p.Address = v >= 1 ? (int)Math.Round(v) : null, 0);
            _body.Children.Add(Columns(2, Theme.Field("Adresse (0 = keine)", addr)));
        }

        var conns = _session.Connections.Of(p.Id).ToList();
        var linked = conns.Count(c => !c.IsOpen);
        Rows(("Verbindungen", $"{linked} von {conns.Count} verbunden", linked == conns.Count ? Theme.SuccessBrush : Theme.OpenEndBrush));

        var buttons = new List<Button> { Cmd("Drehen", "RotateRight", "rotR") };
        if (d.CanFlip) buttons.Add(Cmd("Spiegeln", "Mirror", "flip"));
        if (conns.Count > 1) buttons.Add(Cmd("Anschluss wechseln", "Swap", "swap"));
        buttons.Add(Cmd("Löschen", "Delete", "delete", danger: true));
        _body.Children.Add(Buttons([.. buttons]));
    }

    private void MultiView(IReadOnlyList<PlacedPiece> sel)
    {
        _headerRight.Text = $"{sel.Count} Gleise gewählt";
        Rows(("Gleisstücke", sel.Count.ToString(), null), ("Gleislänge", $"{Fmt.Num(sel.Sum(p => p.Def.TrackLength) / 1000, 2)} m", null));
        _body.Children.Add(Buttons(Cmd("15° links", "RotateLeft", "rotL"), Cmd("15° rechts", "RotateRight", "rotR"), Cmd("Duplizieren", "Copy", "duplicate"), Cmd("Löschen", "Delete", "delete", danger: true)));
        _body.Children.Add(Buttons(Cmd("Steigung…", "Slope", "gradient"), Cmd(sel.All(p => p.Tunnel) ? "Tunnel aufheben" : "Als Tunnel", "Tunnel", "tunnel")));
        _body.Children.Add(new Border
        {
            Background = Theme.TipBackBrush, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 8, 6, 0),
            Child = Theme.Label("Ziehe die Auswahl an ein freies Gleisende – die ganze Gruppe rastet dort ein.", 11.5, false, Theme.TextSecondaryBrush),
        });
    }

    private void ObjectView(object o)
    {
        var plan = _session.Plan;
        switch (o)
        {
            case SceneryItem s:
                _headerRight.Text = SceneryItem.KindName(s.Kind);
                var name = new TextBox { Text = s.Name };
                void CommitName()
                {
                    var n = name.Text.Trim();
                    if (n.Length == 0 || n == s.Name) { name.Text = s.Name; return; }
                    _session.Edit(() => s.Name = n);
                }
                name.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitName(); Keyboard.ClearFocus(); } };
                name.LostKeyboardFocus += (_, _) => { CommitName(); if (_pending) Dispatcher.BeginInvoke(Refresh); };
                _body.Children.Add(Theme.Field("Name", name));
                switch (s.Kind)
                {
                    case SceneryKind.Terrain:
                        _body.Children.Add(Columns(2, Theme.Field("Höhe (mm)", NumberBox(s.Height, v => s.Height = Math.Clamp(v, -500, 3000), 0))));
                        Rows(("Eckpunkte", s.Points.Count.ToString(), null), ("Fläche", $"{Fmt.Num(Math.Abs(Geometry2D.SignedArea(s.Points)) / 1e6, 2)} m²", null));
                        break;
                    case SceneryKind.Water:
                        _body.Children.Add(Columns(2, Theme.Field("Tiefe (mm)", NumberBox(s.Height, v => s.Height = Math.Clamp(v, 0, 200), 0))));
                        Rows(("Fläche", $"{Fmt.Num(Math.Abs(Geometry2D.SignedArea(s.Points)) / 1e6, 2)} m²", null));
                        break;
                    case SceneryKind.Building:
                        _body.Children.Add(Columns(3,
                            Theme.Field("Breite (mm)", NumberBox(s.Width, v => s.Width = Math.Clamp(v, 5, 5000), 0)),
                            Theme.Field("Tiefe (mm)", NumberBox(s.Depth, v => s.Depth = Math.Clamp(v, 5, 5000), 0)),
                            Theme.Field("Höhe (mm)", NumberBox(s.Height, v => s.Height = Math.Clamp(v, 5, 2000), 0))));
                        _body.Children.Add(Columns(3, Theme.Field("Drehung (°)", NumberBox(s.Rotation, v => s.Rotation = Angles.Normalize(v), 0))));
                        break;
                    case SceneryKind.Tree:
                        _body.Children.Add(Columns(2,
                            Theme.Field("Durchmesser (mm)", NumberBox(s.Width, v => s.Width = Math.Clamp(v, 3, 1000), 0)),
                            Theme.Field("Höhe (mm)", NumberBox(s.Height, v => s.Height = Math.Clamp(v, 5, 2000), 0))));
                        break;
                }
                _body.Children.Add(Buttons(Cmd("Löschen", "Delete", "delete", danger: true)));
                _body.Children.Add(Tip("Ziehen verschiebt das Objekt. Entf löscht es."));
                break;

            case Feeder f:
                _headerRight.Text = "Einspeisung";
                var circuit = new ComboBox { IsEditable = true, Text = f.Circuit, Height = 28 };
                foreach (var c in plan.Feeders.Select(x => x.Circuit).Append("Stromkreis 1").Append("Stromkreis 2").Append("Booster 1").Append("Booster 2").Distinct())
                    circuit.Items.Add(c);
                void CommitCircuit()
                {
                    var t = circuit.Text.Trim();
                    if (t.Length == 0 || t == f.Circuit) return;
                    _session.Edit(() => f.Circuit = t);
                }
                circuit.LostKeyboardFocus += (_, _) => { CommitCircuit(); if (_pending) Dispatcher.BeginInvoke(Refresh); };
                circuit.SelectionChanged += (_, _) => { if (circuit.SelectedItem is string sel) { circuit.Text = sel; CommitCircuit(); } };
                _body.Children.Add(Theme.Label("Stromkreis / Booster", 11, false, Theme.TextSecondaryBrush));
                _body.Children.Add(circuit);
                _body.Children.Add(new Border { Height = 8 });
                var section = Electrics.Sections(plan).FirstOrDefault(x => x.PieceIds.Contains(f.PieceId));
                Rows(("Gleis", $"{plan.Find(f.PieceId)?.Def.Number} (Nr. {f.PieceId})", null),
                     ("Stromabschnitt", section is null ? "–" : $"{section.Number} · {Fmt.Num(section.LengthMm / 1000, 2)} m", null));
                _body.Children.Add(Buttons(Cmd("Löschen", "Delete", "delete", danger: true)));
                break;

            case Isolation i:
                _headerRight.Text = "Trennstelle";
                Rows(("Gleis", $"{plan.Find(i.PieceId)?.Def.Number} (Nr. {i.PieceId})", null), ("Gleisende", i.Index.ToString(), null),
                     ("Ausführung", Electrics.IsThreeRail(plan) ? "Mittelleiter getrennt" : "beide Schienen isoliert", null));
                _body.Children.Add(Buttons(Cmd("Löschen", "Delete", "delete", danger: true)));
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
                _body.Children.Add(Theme.Field("Name", sname));
                var type = new ComboBox { Height = 28 };
                foreach (var t in Enum.GetValues<SignalType>()) type.Items.Add(new ComboBoxItem { Content = Signal.TypeName(t), Tag = t, IsSelected = t == g.Type });
                type.SelectionChanged += (_, _) => { if (type.SelectedItem is ComboBoxItem { Tag: SignalType t } && t != g.Type) _session.Edit(() => g.Type = t); };
                _body.Children.Add(Theme.Label("Art", 11, false, Theme.TextSecondaryBrush));
                _body.Children.Add(type);
                var face = SignalFace.For(g.Type);
                var picture = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
                picture.Children.Add(new System.Windows.Controls.Image { Source = PlanScene.SignalImage(g.Type), Width = 34, Height = 88, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top });
                var faceText = new StackPanel();
                faceText.Children.Add(Theme.Label(face.Aspect, 12, true));
                faceText.Children.Add(new TextBlock { Text = face.Description, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = Theme.TextSecondaryBrush });
                picture.Children.Add(faceText);
                _body.Children.Add(picture);
                _body.Children.Add(new Border { Height = 8 });
                _body.Children.Add(Columns(2, Theme.Field("Adresse (0 = keine)", NumberBox(g.Address ?? 0, v => g.Address = v >= 1 ? (int)Math.Round(v) : null, 0))));
                Rows(("Belegt Adressen", g.AddressCount.ToString(), null), ("Gleis", $"{plan.Find(g.PieceId)?.Def.Number} (Nr. {g.PieceId}), Ende {g.Index}", null));
                _body.Children.Add(Buttons(Cmd("Löschen", "Delete", "delete", danger: true)));
                break;
        }
    }

    private Border Tip(string text) => new()
    {
        Background = Theme.TipBackBrush, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 8, 6, 0),
        Child = Theme.Label(text, 11.5, false, Theme.TextSecondaryBrush),
    };
}
