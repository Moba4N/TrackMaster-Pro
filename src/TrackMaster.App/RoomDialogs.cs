using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TrackMaster.Core;

namespace TrackMaster.App;

/// <summary>Dialoge für Raum, Hintergrundbild, Parallelgleis, Höhenprofil, Rückmelder und die Stückliste mit Preisen.</summary>
public static partial class Dialogs
{
    private static Grid Three(UIElement a, UIElement b, UIElement c)
    {
        var g = new Grid();
        for (var i = 0; i < 3; i++) g.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(b, 1); Grid.SetColumn(c, 2);
        g.Children.Add(a); g.Children.Add(b); g.Children.Add(c);
        return g;
    }

    // ---------- Raum ----------

    public sealed record RoomInput(double X, double Y, double Width, double Depth, double Wall);

    public static RoomInput? AskRoom(Window owner, LayoutPlan plan)
    {
        var width = NumberBox(Math.Round((plan.BoardWidth + 1200) / 100) * 100, 0);
        var depth = NumberBox(Math.Round((plan.BoardHeight + 1200) / 100) * 100, 0);
        var x = NumberBox(-600, 0);
        var y = NumberBox(-600, 0);
        var wall = NumberBox(RoomPlan.DefaultWall, 0);
        var error = ErrorText();
        var body = new StackPanel();
        body.Children.Add(Hint(Lang.T("Innenmaße des Raums (von Wand zu Wand). Die Lage gibt an, wo die linke obere Innenecke relativ zur Anlagenplatte liegt – negativ heißt: links bzw. oberhalb der Platte.")));
        if (plan.Scenery.Any(s => s.Kind == SceneryKind.Room))
            body.Children.Add(Hint(Lang.T("Der Plan hat schon einen Raum; es kommt ein weiterer dazu. Den alten bei Bedarf wählen und löschen.")));
        body.Children.Add(Columns(Theme.Field(Lang.T("Breite innen (mm)"), width), Theme.Field(Lang.T("Tiefe innen (mm)"), depth)));
        body.Children.Add(Columns(Theme.Field(Lang.T("Innenecke X (mm)"), x), Theme.Field(Lang.T("Innenecke Y (mm)"), y)));
        body.Children.Add(Theme.Field(Lang.T("Wandstärke (mm)"), wall));
        body.Children.Add(error);
        var ok = Theme.Button(Lang.T("Anlegen"), "Room", accent: true);
        ok.IsDefault = true;
        var cancel = Theme.Button(Lang.T("Abbrechen"));
        cancel.IsCancel = true;
        var w = Small(owner, Lang.T("Raum"), body, 480, 470, cancel, ok);
        RoomInput? result = null;
        ok.Click += (_, _) =>
        {
            var errors = new List<string>();
            Read(width, Lang.T("Breite"), 300, 100000, errors, out var wv);
            Read(depth, Lang.T("Tiefe"), 300, 100000, errors, out var dv);
            Read(x, "X", -100000, 100000, errors, out var xv);
            Read(y, "Y", -100000, 100000, errors, out var yv);
            Read(wall, Lang.T("Wandstärke"), 0, 1000, errors, out var wallv);
            if (errors.Count > 0) { error.Text = string.Join("\n", errors); return; }
            result = new RoomInput(xv, yv, wv, dv, wallv);
            w.Close();
        };
        w.ShowDialog();
        return result;
    }

    // ---------- Hintergrundbild ----------

    /// <summary>Lage, Breite und Deckkraft des Hintergrundbilds. Liefert "remove", "replace" oder null; Änderungen gehen über <paramref name="apply"/>.</summary>
    public static string? AskBackground(Window owner, PlanBackground bg, Action<double, double, double, double, bool> apply)
    {
        var x = NumberBox(bg.X);
        var y = NumberBox(bg.Y);
        var width = NumberBox(bg.Width);
        var opacity = NumberBox(bg.Opacity * 100, 0);
        var visible = new CheckBox { Content = Lang.T("Bild zeigen"), IsChecked = bg.Visible, Margin = new Thickness(0, 4, 0, 8) };
        var error = ErrorText();
        var body = new StackPanel();
        body.Children.Add(Theme.Secondary(Lang.F($"{bg.PixelWidth} × {bg.PixelHeight} Pixel, {Fmt.Num(bg.Width, 0)} × {Fmt.Num(bg.Height, 0)} mm")));
        body.Children.Add(new Border { Height = 8 });
        body.Children.Add(Three(Theme.Field(Lang.T("Links (mm)"), x), Theme.Field(Lang.T("Oben (mm)"), y), Theme.Field(Lang.T("Breite (mm)"), width)));
        body.Children.Add(Theme.Field(Lang.T("Deckkraft (%)"), opacity));
        body.Children.Add(visible);
        body.Children.Add(Hint(Lang.T("Maßstab genau einstellen: Mit dem Werkzeug „Maß“ eine Linie über eine bekannte Strecke im Bild ziehen, die Linie wählen und „Nach Maß skalieren“ aufrufen.")));
        body.Children.Add(error);
        var remove = Theme.Button(Lang.T("Entfernen"), danger: true);
        var replace = Theme.Button(Lang.T("Anderes Bild…"));
        var ok = Theme.Button(Lang.T("Übernehmen"), accent: true);
        ok.IsDefault = true;
        var cancel = Theme.Button(Lang.T("Abbrechen"));
        cancel.IsCancel = true;
        var w = Small(owner, Lang.T("Hintergrundbild"), body, 540, 470, remove, replace, cancel, ok);
        string? result = null;
        remove.Click += (_, _) => { result = "remove"; w.Close(); };
        replace.Click += (_, _) => { result = "replace"; w.Close(); };
        ok.Click += (_, _) =>
        {
            var errors = new List<string>();
            Read(x, Lang.T("Links"), -100000, 100000, errors, out var xv);
            Read(y, Lang.T("Oben"), -100000, 100000, errors, out var yv);
            Read(width, Lang.T("Breite"), 10, 200000, errors, out var wv);
            Read(opacity, Lang.T("Deckkraft"), 5, 100, errors, out var ov);
            if (errors.Count > 0) { error.Text = string.Join("\n", errors); return; }
            apply(xv, yv, wv, ov / 100, visible.IsChecked == true);
            w.Close();
        };
        w.ShowDialog();
        return result;
    }

    /// <summary>Echte Länge einer im Bild gezogenen Maßlinie abfragen.</summary>
    public static double? AskScaleBackground(Window owner, double measured)
    {
        var len = NumberBox(measured, 0);
        var error = ErrorText();
        var body = new StackPanel();
        body.Children.Add(Hint(Lang.F($"Die gewählte Maßlinie misst im Bild jetzt {Fmt.Num(measured, 0)} mm. Wie lang ist diese Strecke in Wirklichkeit? Das Bild wird passend vergrößert oder verkleinert.")));
        body.Children.Add(Theme.Field(Lang.T("Echte Länge (mm)"), len));
        body.Children.Add(error);
        var ok = Theme.Button(Lang.T("Skalieren"), accent: true);
        ok.IsDefault = true;
        var cancel = Theme.Button(Lang.T("Abbrechen"));
        cancel.IsCancel = true;
        var w = Small(owner, Lang.T("Nach Maß skalieren"), body, 460, 300, cancel, ok);
        double? result = null;
        ok.Click += (_, _) =>
        {
            var errors = new List<string>();
            Read(len, Lang.T("Länge"), 1, 1000000, errors, out var v);
            if (errors.Count > 0) { error.Text = string.Join("\n", errors); return; }
            result = v;
            w.Close();
        };
        w.ShowDialog();
        return result;
    }

    // ---------- Parallelgleis ----------

    public static (double Spacing, bool Left)? AskParallel(Window owner, Gauge gauge)
    {
        var spacing = NumberBox(Helix.DefaultSpacing(gauge), 0);
        var left = new RadioButton { Content = Lang.T("links (meist oben)"), IsChecked = true, GroupName = "side", Margin = new Thickness(0, 4, 16, 8) };
        var right = new RadioButton { Content = Lang.T("rechts (meist unten)"), GroupName = "side", Margin = new Thickness(0, 4, 0, 8) };
        var sides = new StackPanel { Orientation = Orientation.Horizontal };
        sides.Children.Add(left);
        sides.Children.Add(right);
        var error = ErrorText();
        var body = new StackPanel();
        body.Children.Add(Hint(Lang.T("Legt ein Flexgleis im festen Abstand neben die gewählten Gleise – Bögen bekommen den passenden größeren bzw. kleineren Radius. Die Seite gilt in Fahrtrichtung, die Kette läuft möglichst von links nach rechts.")));
        body.Children.Add(Theme.Field(Lang.T("Gleisabstand Mitte zu Mitte (mm)"), spacing));
        body.Children.Add(sides);
        body.Children.Add(error);
        var ok = Theme.Button(Lang.T("Anlegen"), "Parallel", accent: true);
        ok.IsDefault = true;
        var cancel = Theme.Button(Lang.T("Abbrechen"));
        cancel.IsCancel = true;
        var w = Small(owner, Lang.T("Parallelgleis"), body, 480, 340, cancel, ok);
        (double, bool)? result = null;
        ok.Click += (_, _) =>
        {
            var errors = new List<string>();
            Read(spacing, Lang.T("Gleisabstand"), gauge.TrackWidth * 1.5, 2000, errors, out var s);
            if (errors.Count > 0) { error.Text = string.Join("\n", errors); return; }
            result = (s, left.IsChecked == true);
            w.Close();
        };
        w.ShowDialog();
        return result;
    }

    // ---------- Höhenprofil ----------

    public static void ShowProfile(Window owner, HeightProfile profile)
    {
        const double width = 640, height = 260;
        var chart = profile.Chart(width, height);
        var canvas = new Canvas { Width = width, Height = height, Background = Theme.PanelBrush };
        var gridPen = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80));
        void Rule(double x1, double y1, double x2, double y2, Brush b, double t = 1)
            => canvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = b, StrokeThickness = t });
        void Text(string s, double x, double y, Brush b, TextAlignment align)
        {
            var tb = new TextBlock { Text = s, FontSize = 11, Foreground = b };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var left = align switch { TextAlignment.Right => x - tb.DesiredSize.Width, TextAlignment.Center => x - tb.DesiredSize.Width / 2, _ => x };
            Canvas.SetLeft(tb, left);
            Canvas.SetTop(tb, y - tb.DesiredSize.Height + 3);
            canvas.Children.Add(tb);
        }
        foreach (var (y, _) in chart.HeightTicks) Rule(chart.Left, y, chart.Right, y, gridPen);
        Rule(chart.Left, chart.Top, chart.Left, chart.Bottom, Theme.TextSecondaryBrush);
        Rule(chart.Left, chart.Bottom, chart.Right, chart.Bottom, Theme.TextSecondaryBrush);
        var fill = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(0x30, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B)) };
        fill.Points.Add(new Point(chart.Line[0].X, chart.Bottom));
        foreach (var (x, y) in chart.Line) fill.Points.Add(new Point(x, y));
        fill.Points.Add(new Point(chart.Line[^1].X, chart.Bottom));
        canvas.Children.Add(fill);
        var line = new Polyline { Stroke = Theme.AccentBrush, StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };
        foreach (var (x, y) in chart.Line) line.Points.Add(new Point(x, y));
        canvas.Children.Add(line);
        foreach (var (x, y) in chart.Line)
        {
            var dot = new Ellipse { Width = 6, Height = 6, Fill = Theme.AccentBrush };
            Canvas.SetLeft(dot, x - 3); Canvas.SetTop(dot, y - 3);
            canvas.Children.Add(dot);
        }
        foreach (var (y, text) in chart.HeightTicks) Text(text, chart.Left - 6, y + 4, Theme.TextSecondaryBrush, TextAlignment.Right);
        foreach (var (x, text) in chart.DistanceTicks) Text(text, x, chart.Bottom + 16, Theme.TextSecondaryBrush, TextAlignment.Center);
        foreach (var g in chart.Grades) Text(g.Text, g.X, g.Y, g.Steep ? Theme.DangerBrush : Theme.TextBrush, TextAlignment.Center);

        var body = new StackPanel();
        body.Children.Add(Theme.Secondary(profile.Summary()));
        body.Children.Add(new Border { BorderBrush = Theme.BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 10, 0, 8), Child = new Viewbox { Child = canvas, Stretch = Stretch.Uniform } });
        body.Children.Add(Theme.Secondary(Lang.T("Höhe der Schienenoberkante in mm über der Platte, entlang der gewählten Gleise. Rote Werte liegen über der empfohlenen Steigung.")));
        var close = Theme.Button(Lang.T("Schließen"), accent: true);
        close.IsDefault = true;
        close.IsCancel = true;
        var w = Frame(owner, Lang.T("Höhenprofil"), body, close);
        w.Width = 760;
        w.Height = 520;
        close.Click += (_, _) => w.Close();
        w.ShowDialog();
    }

    // ---------- Rückmelder ----------

    public static DetectorType? AskDetectorType(Window owner, DetectorType current)
    {
        var body = new StackPanel();
        body.Children.Add(Hint(Lang.T("Art wählen, dann auf die Gleise klicken. Die Kontakte werden fortlaufend nummeriert (16 je Rückmeldemodul, z. B. s88).")));
        DetectorType? result = null;
        var cancel = Theme.Button(Lang.T("Abbrechen"));
        cancel.IsCancel = true;
        var w = Small(owner, Lang.T("Rückmelder setzen"), body, 500, 420, cancel);
        foreach (var t in Enum.GetValues<DetectorType>())
        {
            var type = t;
            var texts = new StackPanel();
            texts.Children.Add(Theme.Label(Detector.TypeName(t), 12, true));
            texts.Children.Add(Theme.Secondary(t switch
            {
                DetectorType.Strom => Lang.T("Meldet ein besetztes Gleis über den Stromverbrauch (Zweileiter: eine Schiene getrennt)."),
                DetectorType.Kontakt => Lang.T("Meldet, wenn ein Zug über das Schaltgleis fährt (Märklin: Kontaktgleis bzw. Schaltgleis)."),
                _ => Lang.T("Reedkontakt im Gleis, ausgelöst durch einen Magneten unter dem Fahrzeug."),
            }));
            ((TextBlock)texts.Children[1]).TextWrapping = TextWrapping.Wrap;
            var b = new Button
            {
                Content = texts, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = t == current ? Theme.AccentLightBrush : Brushes.Transparent, BorderBrush = Theme.BorderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 8, 8, 8), Template = Theme.RoundedTemplate(), Cursor = System.Windows.Input.Cursors.Hand,
            };
            b.Click += (_, _) => { result = type; w.Close(); };
            body.Children.Add(b);
        }
        w.ShowDialog();
        return result;
    }

    // ---------- Stückliste mit Bestand und Preisen ----------

    /// <summary>
    /// Stückliste mit eigenem Bestand und Preisen je Artikel. Die Werte gehen beim Schließen über <paramref name="apply"/>
    /// in den Plan; <paramref name="importPrices"/> liest eine Preisliste ein und gibt true zurück, wenn sich etwas geändert hat.
    /// </summary>
    public static void ShowBillOfMaterials(Window owner, PlanSession session, Action exportCsv, Func<bool> importPrices)
    {
        var plan = session.Plan;
        var rows = BillOfMaterials.Costs(plan);
        var stats = plan.Statistics();
        var body = new StackPanel();
        body.Children.Add(Theme.Secondary(Lang.F($"{plan.Name} · Spur {plan.GaugeId} · {stats.Pieces} Teile · {Fmt.Num(stats.TrackLengthMm / 1000, 2)} m Gleis")));
        var stockBoxes = new Dictionary<string, TextBox>();
        var priceBoxes = new Dictionary<string, TextBox>();
        var totals = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var error = ErrorText();
        if (rows.Count == 0)
        {
            var empty = Theme.Label(Lang.T("Der Plan enthält noch keine Gleise."), 12, false, Theme.TextSecondaryBrush);
            empty.Margin = new Thickness(0, 16, 0, 0);
            body.Children.Add(empty);
        }
        else
        {
            body.Children.Add(new Border { Height = 6 });
            body.Children.Add(Hint(Lang.T("Preise und eigenen Bestand eintragen: Die Liste zeigt dann, was noch zu kaufen ist und was es kostet. Die Werte werden mit dem Plan gespeichert.")));
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var w in new[] { 50.0, double.NaN, 64, 64, 80, 80 })
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            void Cell(UIElement e, int row, int col, bool right = false)
            {
                if (e is FrameworkElement fe) { fe.Margin = new Thickness(0, 5, 8, 5); fe.VerticalAlignment = VerticalAlignment.Center; if (right) fe.HorizontalAlignment = HorizontalAlignment.Right; }
                Grid.SetRow(e, row); Grid.SetColumn(e, col);
                grid.Children.Add(e);
            }
            grid.RowDefinitions.Add(new RowDefinition());
            string[] heads = [Lang.T("Anzahl"), Lang.T("Artikel"), Lang.T("Bestand"), Lang.T("Zu kaufen"), Lang.T("Preis"), Lang.T("Summe")];
            for (var c = 0; c < heads.Length; c++) Cell(Theme.Label(heads[c], 11, true, Theme.TextSecondaryBrush), 0, c, c is 0 or 3 or 5);
            var buyTexts = new Dictionary<string, TextBlock>();
            var sumTexts = new Dictionary<string, TextBlock>();
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                grid.RowDefinitions.Add(new RowDefinition());
                var line = new Border { BorderBrush = Theme.BorderBrush, BorderThickness = new Thickness(0, 1, 0, 0) };
                Grid.SetRow(line, i + 1); Grid.SetColumnSpan(line, heads.Length);
                grid.Children.Add(line);
                Cell(Theme.Label($"{r.Count}×", 12, true), i + 1, 0, right: true);
                var name = new StackPanel();
                name.Children.Add(Theme.Label(r.Article.Length > 0 ? $"{r.Article} · {r.Name}" : r.Name));
                name.Children.Add(Theme.Secondary($"{r.Detail} · {r.System}"));
                Cell(name, i + 1, 1);
                var stock = new TextBox { Text = r.Stock > 0 ? r.Stock.ToString() : "", Height = 26, VerticalContentAlignment = VerticalAlignment.Center, BorderBrush = Theme.ControlBorderBrush };
                var price = new TextBox { Text = r.Price is { } p ? Fmt.Num(p, 2).Replace(Lang.IsEnglish ? "," : ".", "") : "", Height = 26, VerticalContentAlignment = VerticalAlignment.Center, BorderBrush = Theme.ControlBorderBrush };
                System.Windows.Automation.AutomationProperties.SetName(stock, Lang.F($"Bestand {r.Name}"));
                System.Windows.Automation.AutomationProperties.SetName(price, Lang.F($"Preis {r.Name}"));
                stockBoxes[r.Key] = stock;
                priceBoxes[r.Key] = price;
                Cell(stock, i + 1, 2);
                buyTexts[r.Key] = Theme.Label("", 12, true);
                Cell(buyTexts[r.Key], i + 1, 3, right: true);
                Cell(price, i + 1, 4);
                sumTexts[r.Key] = Theme.Label("");
                Cell(sumTexts[r.Key], i + 1, 5, right: true);
            }
            body.Children.Add(grid);
            body.Children.Add(totals);
            void Update()
            {
                double all = 0, buy = 0;
                var withPrice = 0;
                foreach (var r in rows)
                {
                    var st = int.TryParse(stockBoxes[r.Key].Text.Trim(), out var n) && n > 0 ? n : 0;
                    var toBuy = Math.Max(0, r.Count - st);
                    buyTexts[r.Key].Text = toBuy.ToString();
                    buyTexts[r.Key].Foreground = toBuy > 0 ? Theme.TextBrush : Theme.SuccessBrush;
                    var price = BillOfMaterials.ParseMoney(priceBoxes[r.Key].Text.Trim());
                    sumTexts[r.Key].Text = price is { } p ? BillOfMaterials.Money(p * toBuy) : "";
                    if (price is { } pp) { withPrice++; all += pp * r.Count; buy += pp * toBuy; }
                }
                totals.Children.Clear();
                if (withPrice == 0) return;
                void Row(string label, string value, bool bold)
                {
                    var row = new DockPanel { Margin = new Thickness(0, 2, 8, 2) };
                    var v = Theme.Label(value, 12, bold);
                    DockPanel.SetDock(v, Dock.Right);
                    row.Children.Add(v);
                    row.Children.Add(Theme.Label(label, 12, bold));
                    totals.Children.Add(row);
                }
                Row(Lang.T("Noch zu kaufen"), BillOfMaterials.Money(buy), true);
                Row(Lang.T("Alles neu gekauft"), BillOfMaterials.Money(all), false);
                if (withPrice < rows.Count) Row(Lang.T("Ohne Preis"), Lang.F($"{rows.Count - withPrice} Artikel"), false);
            }
            foreach (var b in stockBoxes.Values.Concat(priceBoxes.Values)) b.TextChanged += (_, _) => Update();
            Update();
        }
        body.Children.Add(error);

        bool Apply()
        {
            var changes = new List<(string Key, double? Price, int Stock)>();
            var errors = new List<string>();
            var changed = false;
            foreach (var r in rows)
            {
                var pt = priceBoxes[r.Key].Text.Trim();
                double? price = pt.Length == 0 ? null : BillOfMaterials.ParseMoney(pt);
                if (pt.Length > 0 && price is null) { errors.Add(Lang.F($"{r.Name}: Preis bitte als Zahl eingeben.")); price = r.Price; }
                var st = stockBoxes[r.Key].Text.Trim();
                var stock = st.Length == 0 ? 0 : int.TryParse(st, out var n) && n >= 0 ? n : -1;
                if (stock < 0) { errors.Add(Lang.F($"{r.Name}: Bestand bitte als ganze Zahl eingeben.")); stock = r.Stock; }
                if (price != r.Price || stock != r.Stock) changed = true;
                changes.Add((r.Key, price, stock));
            }
            if (errors.Count > 0) { error.Text = string.Join("\n", errors); return false; }
            if (changed) session.SetCosts(changes);
            return true;
        }

        var csv = Theme.Button(Lang.T("Als CSV speichern…"), "Csv");
        var import = Theme.Button(Lang.T("Preisliste einlesen…"));
        var close = Theme.Button(Lang.T("Schließen"), accent: true);
        close.IsDefault = true;
        var window = Frame(owner, Lang.T("Stückliste"), body, csv, import, close);
        window.Width = 760;
        window.Height = 620;
        var reopen = false;
        csv.Click += (_, _) => { if (Apply()) exportCsv(); };
        import.Click += (_, _) =>
        {
            if (!Apply() || !importPrices()) return;
            reopen = true;
            window.Close();
        };
        close.Click += (_, _) => window.Close();
        // Schließen auf jedem Weg (×, Esc) übernimmt die Eingaben; bei Fehlern bleibt das Fenster offen
        window.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; window.Close(); } };
        window.Closing += (_, e) => { if (!reopen && !Apply()) e.Cancel = true; };
        window.ShowDialog();
        if (reopen) ShowBillOfMaterials(owner, session, exportCsv, importPrices);
    }
}
