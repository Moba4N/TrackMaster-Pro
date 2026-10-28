using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrackMaster.Core;
using TrackMaster.Licensing;

namespace TrackMaster.App;

/// <summary>Dialoge für Prüfung, Höhe, Flexgleis, Digitaladressen, Kabelplan und Lizenz.</summary>
public static partial class Dialogs
{
    private static Window Small(Window owner, string title, UIElement body, double width, double height, params Button[] footer)
    {
        var w = Frame(owner, title, body, footer);
        w.Width = width;
        w.Height = height;
        w.ResizeMode = ResizeMode.NoResize;
        return w;
    }

    private static TextBox NumberBox(double value, int decimals = 1) => new() { Text = Fmt.Num(value, decimals) };

    private static bool Read(TextBox box, string name, double min, double max, List<string> errors, out double value)
    {
        value = Fmt.Parse(box.Text) ?? double.NaN;
        if (double.IsNaN(value) || value < min || value > max)
        {
            errors.Add(Lang.F($"{name}: bitte eine Zahl zwischen {Fmt.Num(min)} und {Fmt.Num(max)} eingeben."));
            return false;
        }
        return true;
    }

    private static Border Hint(string text) => new()
    {
        Background = Theme.TipBackBrush, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 4, 6, 8),
        Child = Theme.Label(text, 11.5, false, Theme.TextSecondaryBrush),
    };

    private static TextBlock ErrorText() => new() { Foreground = Theme.DangerBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };

    // ---------- Planprüfung ----------

    /// <summary>Liste der Befunde; ein Klick springt zur Stelle. Das Fenster bleibt offen, während man weiterarbeitet.</summary>
    public static Window ShowIssues(Window owner, string title, Func<IReadOnlyList<PlanIssue>> run, Action<PlanIssue> pick, string? footerNote = null)
    {
        var list = new StackPanel();
        var summary = Theme.Secondary("");
        var body = new StackPanel();
        body.Children.Add(summary);
        if (footerNote is not null) body.Children.Add(Hint(footerNote));
        body.Children.Add(list);
        var again = Theme.Button(Lang.T("Erneut prüfen"), "Shield");
        var close = Theme.Button(Lang.T("Schließen"), accent: true);
        close.IsCancel = true;
        var window = Frame(owner, title, body, again, close);
        window.Width = 620;
        window.ShowInTaskbar = false;
        void Fill()
        {
            var issues = run();
            list.Children.Clear();
            var errors = issues.Count(i => i.Level == IssueLevel.Fehler);
            var warnings = issues.Count(i => i.Level == IssueLevel.Warnung);
            summary.Text = issues.Count == 0 ? Lang.T("Keine Befunde – alles in Ordnung.") : Lang.F($"{errors} Fehler, {warnings} Warnungen, {issues.Count - errors - warnings} Hinweise. Klick auf einen Eintrag zeigt die Stelle.");
            foreach (var i in issues)
            {
                var color = i.Level switch { IssueLevel.Fehler => Theme.DangerBrush, IssueLevel.Warnung => Theme.OpenEndBrush, _ => Theme.AccentBrush };
                var row = new DockPanel { Margin = new Thickness(0, 0, 6, 0) };
                var badge = new Border { Background = color, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 1, 10, 0), VerticalAlignment = VerticalAlignment.Top, Child = Theme.Label(i.Level.Text(), 10.5, true, Brushes.White) };
                row.Children.Add(badge);
                var texts = new StackPanel();
                texts.Children.Add(Theme.Label(i.Category, 11, true, Theme.TextSecondaryBrush));
                texts.Children.Add(Theme.Label(i.Text));
                row.Children.Add(texts);
                var b = new Button
                {
                    Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent, BorderBrush = Theme.BorderBrush,
                    BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(4, 8, 4, 8), Template = Theme.RoundedTemplate(), Cursor = System.Windows.Input.Cursors.Hand,
                };
                ((FrameworkElement)b.Content).HorizontalAlignment = HorizontalAlignment.Stretch;
                b.Click += (_, _) => pick(i);
                list.Children.Add(b);
            }
        }
        again.Click += (_, _) => Fill();
        close.Click += (_, _) => window.Close();
        Fill();
        window.Show();
        return window;
    }

    // ---------- Höhe ----------

    public sealed record GradientInput(double StartHeight, double Percent, bool Reverse);

    public static GradientInput? AskGradient(Window owner, int pieces, double startHeight)
    {
        var start = NumberBox(startHeight, 0);
        var percent = NumberBox(2.5);
        var reverse = new CheckBox { Content = Lang.T("Am anderen Ende beginnen"), Margin = new Thickness(0, 4, 0, 8) };
        var error = ErrorText();
        var body = new StackPanel();
        body.Children.Add(Hint(Lang.F($"Die Steigung wird gleichmäßig über die {pieces} gewählten Gleise gelegt, vom einen Ende der Gleisfolge zum anderen. ") +
                               Lang.T("Empfohlen: höchstens 2,5–3 %, in Bögen weniger. Negative Werte ergeben ein Gefälle.")));
        body.Children.Add(Theme.Field(Lang.T("Höhe am Anfang (mm über Platte)"), start));
        body.Children.Add(Theme.Field(Lang.T("Steigung (%)"), percent));
        body.Children.Add(reverse);
        body.Children.Add(error);
        var ok = Theme.Button(Lang.T("Steigung anlegen"), "Slope", accent: true);
        ok.IsDefault = true;
        var cancel = Theme.Button(Lang.T("Abbrechen"));
        cancel.IsCancel = true;
        var w = Small(owner, Lang.T("Steigung"), body, 460, 430, cancel, ok);
        GradientInput? result = null;
        ok.Click += (_, _) =>
        {
            var errors = new List<string>();
            Read(start, Lang.T("Höhe am Anfang"), -500, 3000, errors, out var s);
            Read(percent, Lang.T("Steigung"), -15, 15, errors, out var p);
            if (errors.Count > 0) { error.Text = string.Join("\n", errors); return; }
            result = new GradientInput(s, p, reverse.IsChecked == true);
            w.Close();
        };
        w.ShowDialog();
        return result;
    }

    /// <summary>Gleiswendel: rund oder oval, ein- oder zweigleisig. Liefert die Angaben zum Bauen oder null.</summary>
    public static HelixSpec? AskHelix(Window owner, Gauge gauge, IReadOnlyList<TrackPieceDef> curves, IReadOnlyList<TrackPieceDef> straights, bool fromEnd)
    {
        var shape = new ComboBox { Height = 28, Items = { new ComboBoxItem { Content = Lang.T("Rund") }, new ComboBoxItem { Content = Lang.T("Oval (mit Geraden an den Längsseiten)") } }, SelectedIndex = 0 };
        var tracks = new ComboBox { Height = 28, Items = { new ComboBoxItem { Content = Lang.T("eingleisig") }, new ComboBoxItem { Content = Lang.T("zweigleisig") } }, SelectedIndex = 0 };
        var curveBox = new ComboBox { Height = 28 };
        var straightBox = new ComboBox { Height = 28 };
        foreach (var st in straights.OrderBy(x => x.Length))
            straightBox.Items.Add(new ComboBoxItem { Content = $"{st.Number} – {Fmt.Num(st.Length)} mm ({st.SystemName})", Tag = st });
        straightBox.SelectedIndex = straightBox.Items.Count == 0 ? -1 : straightBox.Items.Cast<ComboBoxItem>().Select((x, k) => (x, k)).MinBy(t => Math.Abs(((TrackPieceDef)t.x.Tag).Length - gauge.TrackWidth * 11)).k;
        var straightCount = NumberBox(2, 0);
        var outerBox = new ComboBox { Height = 28 };
        var spacing = NumberBox(Helix.DefaultSpacing(gauge), 1);
        var turns = NumberBox(2);
        var rise = NumberBox(gauge.ClearanceHeight + 10, 0);
        var direction = new ComboBox { Height = 28, Items = { new ComboBoxItem { Content = Lang.T("links herum (gegen den Uhrzeigersinn)") }, new ComboBoxItem { Content = Lang.T("rechts herum (im Uhrzeigersinn)") } }, SelectedIndex = 0 };
        var info = Theme.Secondary("");
        var error = ErrorText();
        var ovalPanel = new StackPanel();
        ovalPanel.Children.Add(Theme.Label(Lang.T("Gerades Gleis an den Längsseiten"), 11, false, Theme.TextSecondaryBrush));
        ovalPanel.Children.Add(straightBox);
        ovalPanel.Children.Add(new Border { Height = 6 });
        ovalPanel.Children.Add(Theme.Field(Lang.T("Anzahl Geraden je Längsseite"), straightCount));
        var outerPanel = new StackPanel();
        outerPanel.Children.Add(Theme.Label(Lang.T("Außengleis"), 11, false, Theme.TextSecondaryBrush));
        outerPanel.Children.Add(outerBox);
        outerPanel.Children.Add(new Border { Height = 6 });
        var spacingField = Theme.Field(Lang.T("Gleisabstand bei Flexgleis (mm)"), spacing);
        outerPanel.Children.Add(spacingField);

        HelixShape Shape() => shape.SelectedIndex == 1 ? HelixShape.Oval : HelixShape.Rund;
        TrackPieceDef? Inner() => curveBox.SelectedItem is ComboBoxItem { Tag: TrackPieceDef d } ? d : null;
        bool updating = false;
        void FillCurves()
        {
            updating = true;
            var keep = Inner()?.Key;
            curveBox.Items.Clear();
            foreach (var c in curves.Where(c => Helix.Fits(c, Shape())).OrderBy(c => c.Radius))
                curveBox.Items.Add(new ComboBoxItem { Content = $"{c.Number} – R {Fmt.Num(c.Radius)} mm, {Fmt.Num(c.Angle)}° ({c.SystemName})", Tag = c, IsSelected = c.Key == keep });
            // Voreinstellung: zweitkleinster Radius, falls vorhanden
            if (curveBox.SelectedIndex < 0) curveBox.SelectedIndex = Math.Min(1, curveBox.Items.Count - 1);
            updating = false;
            FillOuter();
        }
        void FillOuter()
        {
            updating = true;
            outerBox.Items.Clear();
            if (Inner() is { } inner)
            {
                var sp = Fmt.Parse(spacing.Text) ?? Helix.DefaultSpacing(gauge);
                var best = Helix.OuterCurve(curves, inner, sp, Shape());
                foreach (var c in curves.Where(c => c.Radius > inner.Radius && Helix.Fits(c, Shape())).OrderBy(c => c.Radius))
                    outerBox.Items.Add(new ComboBoxItem { Content = Lang.F($"{c.Number} – R {Fmt.Num(c.Radius)} mm (Abstand {Fmt.Num(c.Radius - inner.Radius)} mm)"), Tag = c, IsSelected = c == best });
                outerBox.Items.Insert(0, new ComboBoxItem { Content = Lang.T("Flexgleis mit frei gewähltem Abstand"), Tag = null, IsSelected = best is null });
            }
            updating = false;
            Update();
        }
        HelixSpec? Spec(List<string>? errors)
        {
            errors ??= [];
            if (Inner() is not { } inner) { errors.Add(Lang.T("Bitte ein Bogengleis wählen.")); return null; }
            Read(turns, Lang.T("Windungen"), 0.25, 20, errors, out var t);
            Read(rise, Lang.T("Höhe je Windung"), 10, 1000, errors, out var r);
            var oval = Shape() == HelixShape.Oval;
            var two = tracks.SelectedIndex == 1;
            double n = 0, sp = 0;
            TrackPieceDef? straight = null;
            if (oval)
            {
                straight = straightBox.SelectedItem is ComboBoxItem { Tag: TrackPieceDef st } ? st : null;
                if (straight is null) errors.Add(Lang.T("Bitte ein gerades Gleis wählen."));
                Read(straightCount, Lang.T("Geraden je Längsseite"), 1, 50, errors, out n);
            }
            var outer = two && outerBox.SelectedItem is ComboBoxItem { Tag: TrackPieceDef oc } ? oc : null;
            if (two && outer is null) Read(spacing, Lang.T("Gleisabstand"), gauge.TrackWidth * PlanCheck.MinSpacingFactor, gauge.TrackWidth * 20, errors, out sp);
            if (errors.Count > 0) return null;
            return new HelixSpec(inner.Key, t, r, direction.SelectedIndex == 1, Shape(), straight?.Key, (int)Math.Round(n), two ? 2 : 1,
                outer is not null ? outer.Radius - inner.Radius : sp, outer?.Key);
        }
        void Update()
        {
            if (updating) return;
            ovalPanel.Visibility = Shape() == HelixShape.Oval ? Visibility.Visible : Visibility.Collapsed;
            outerPanel.Visibility = tracks.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            spacingField.Visibility = outerBox.SelectedItem is ComboBoxItem { Tag: null } ? Visibility.Visible : Visibility.Collapsed;
            if (Spec(null) is not { } spec) { info.Text = ""; return; }
            var m = Helix.Material(spec, gauge);
            info.Text = Lang.F($"Steigung {Fmt.Num(Helix.GradePercent(spec), 1)} % · Gesamthöhe {Fmt.Num(spec.TotalHeight, 0)} mm · Platzbedarf {Fmt.Num(2 * m.OuterBoardRadius + spec.StraightLength, 0)} × {Fmt.Num(2 * m.OuterBoardRadius, 0)} mm");
        }
        shape.SelectionChanged += (_, _) => FillCurves();
        curveBox.SelectionChanged += (_, _) => { if (!updating) FillOuter(); };
        outerBox.SelectionChanged += (_, _) => Update();
        tracks.SelectionChanged += (_, _) => Update();
        straightBox.SelectionChanged += (_, _) => Update();
        foreach (var box in new[] { turns, rise, straightCount }) box.TextChanged += (_, _) => Update();
        spacing.TextChanged += (_, _) => Update();

        var body = new StackPanel();
        body.Children.Add(Hint(fromEnd
            ? Lang.T("Die Wendel beginnt am aktiven Gleisende und steigt gleichmäßig an. Nach dem Bauen folgt die Holzliste.")
            : Lang.T("Die Wendel entsteht in der Mitte der Ansicht. Tipp: Vorher ein Gleisende anklicken, dann schließt sie dort an. Nach dem Bauen folgt die Holzliste.")));
        body.Children.Add(Columns(shape, tracks));
        body.Children.Add(new Border { Height = 8 });
        body.Children.Add(Theme.Label(Lang.T("Bogengleis (innen)"), 11, false, Theme.TextSecondaryBrush));
        body.Children.Add(curveBox);
        body.Children.Add(new Border { Height = 8 });
        body.Children.Add(ovalPanel);
        body.Children.Add(outerPanel);
        body.Children.Add(new Border { Height = 4 });
        body.Children.Add(Theme.Field(Lang.T("Windungen"), turns));
        body.Children.Add(Theme.Field(Lang.F($"Höhe je Windung (mm, mindestens {Fmt.Num(gauge.ClearanceHeight, 0)} mm Durchfahrtshöhe)"), rise));
        body.Children.Add(Theme.Label(Lang.T("Richtung"), 11, false, Theme.TextSecondaryBrush));
        body.Children.Add(direction);
        body.Children.Add(new Border { Height = 8 });
        body.Children.Add(info);
        body.Children.Add(error);
        FillCurves();
        var ok = Theme.Button(Lang.T("Gleiswendel bauen"), "Helix", accent: true);
        ok.IsDefault = true;
        var cancel = Theme.Button(Lang.T("Abbrechen"));
        cancel.IsCancel = true;
        var w = Small(owner, Lang.T("Gleiswendel"), new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 560, 720, cancel, ok);
        HelixSpec? result = null;
        ok.Click += (_, _) =>
        {
            var errors = new List<string>();
            var spec = Spec(errors);
            if (spec is null) { error.Text = string.Join("\n", errors); return; }
            if (spec.Rise < gauge.ClearanceHeight) { error.Text = Lang.F($"Mit {Fmt.Num(spec.Rise, 0)} mm passt kein Zug unter die nächste Windung (mindestens {Fmt.Num(gauge.ClearanceHeight, 0)} mm)."); return; }
            result = spec;
            w.Close();
        };
        w.ShowDialog();
        return result;
    }

    private static Grid Columns(UIElement left, UIElement right)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(right, 2);
        g.Children.Add(left);
        g.Children.Add(right);
        return g;
    }

    /// <summary>Holzliste der Gleiswendel mit allen Zuschnittmaßen; kopieren oder als CSV speichern.</summary>
    public static void ShowHelixMaterial(Window owner, HelixMaterial m, Action<string> saveCsv)
    {
        var body = new StackPanel();
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var width in new[] { 50.0, 190, double.NaN })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(width) ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
        void Cell(UIElement e, int row, int col)
        {
            if (e is FrameworkElement fe) fe.Margin = new Thickness(0, 6, 8, 6);
            Grid.SetRow(e, row); Grid.SetColumn(e, col);
            grid.Children.Add(e);
        }
        grid.RowDefinitions.Add(new RowDefinition());
        string[] heads = [Lang.T("Anzahl"), Lang.T("Teil"), Lang.T("Maße")];
        for (var c = 0; c < heads.Length; c++) Cell(Theme.Label(heads[c], 11, true, Theme.TextSecondaryBrush), 0, c);
        for (var i = 0; i < m.Parts.Count; i++)
        {
            var p = m.Parts[i];
            grid.RowDefinitions.Add(new RowDefinition());
            var line = new Border { BorderBrush = Theme.BorderBrush, BorderThickness = new Thickness(0, 1, 0, 0) };
            Grid.SetRow(line, i + 1); Grid.SetColumnSpan(line, 3);
            grid.Children.Add(line);
            Cell(Theme.Label(p.Count.ToString(), 12, true), i + 1, 0);
            Cell(new TextBlock { Text = p.Name, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold }, i + 1, 1);
            var size = new StackPanel();
            if (p.Size.Length > 0) size.Children.Add(new TextBlock { Text = p.Size, TextWrapping = TextWrapping.Wrap });
            if (p.Note.Length > 0) size.Children.Add(new TextBlock { Text = p.Note, TextWrapping = TextWrapping.Wrap, Foreground = Theme.TextSecondaryBrush, FontSize = 11.5 });
            Cell(size, i + 1, 2);
        }
        body.Children.Add(grid);
        var notes = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var n in m.Notes) notes.Children.Add(new TextBlock { Text = "• " + n, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2), Foreground = n.StartsWith(Lang.T("Achtung")) ? Theme.DangerBrush : Theme.TextSecondaryBrush });
        body.Children.Add(notes);
        var copy = Theme.Button(Lang.T("Kopieren"));
        copy.Click += (_, _) => { try { Clipboard.SetText(m.ToText()); } catch (System.Runtime.InteropServices.COMException) { } };
        var csv = Theme.Button(Lang.T("Als CSV speichern…"));
        csv.Click += (_, _) => saveCsv(m.ToCsv());
        var close = Theme.Button(Lang.T("Schließen"), accent: true);
        close.IsCancel = true;
        var w = Frame(owner, Lang.T("Holzliste Gleiswendel"), new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, copy, csv, close);
        w.Width = 760;
        w.Height = 620;
        close.Click += (_, _) => w.Close();
        w.Show();
    }

    /// <summary>Fragt Maße für ein Flexgleis ab und liefert den Katalogschlüssel.</summary>
    public static string? AskFlex(Window owner, Gauge gauge, FlexStock stock)
    {
        var straight = new RadioButton { Content = Lang.T("Gerade"), IsChecked = true, Margin = new Thickness(0, 0, 16, 0) };
        var curve = new RadioButton { Content = Lang.T("Bogen") };
        var kinds = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        kinds.Children.Add(straight);
        kinds.Children.Add(curve);
        var length = NumberBox(Math.Round(stock.Length / 2), 0);
        var radius = NumberBox(Math.Round(gauge.TrackWidth * 30), 0);
        var angle = NumberBox(30);
        var lengthField = Theme.Field(Lang.T("Länge (mm)"), length);
        var radiusField = Theme.Field("Radius (mm)", radius);
        var angleField = Theme.Field(Lang.T("Winkel (°)"), angle);
        void Sync()
        {
            lengthField.Visibility = straight.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            radiusField.Visibility = angleField.Visibility = straight.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
        }
        straight.Checked += (_, _) => Sync();
        curve.Checked += (_, _) => Sync();
        var error = ErrorText();
        var body = new StackPanel();
        body.Children.Add(Hint(Lang.F($"Flexgleis {stock.Name}{(stock.Number == Lang.T("Flexgleis") ? "" : $" ({stock.Number})")}. Es wird am aktiven Gleisende angesetzt; Bögen lassen sich danach mit F spiegeln. In der Stückliste wird die Gesamtlänge in Stücke zu {Fmt.Num(stock.Length, 0)} mm umgerechnet.")));
        body.Children.Add(kinds);
        body.Children.Add(lengthField);
        body.Children.Add(radiusField);
        body.Children.Add(angleField);
        body.Children.Add(error);
        Sync();
        var ok = Theme.Button(Lang.T("Ansetzen"), "Flex", accent: true);
        ok.IsDefault = true;
        var cancel = Theme.Button(Lang.T("Abbrechen"));
        cancel.IsCancel = true;
        var w = Small(owner, Lang.T("Flexgleis"), body, 440, 400, cancel, ok);
        string? key = null;
        ok.Click += (_, _) =>
        {
            var errors = new List<string>();
            if (straight.IsChecked == true)
            {
                if (Read(length, Lang.T("Länge"), 5, 5000, errors, out var l)) key = Catalog.FlexStraightKey(gauge.Id, l, stock.SystemId);
            }
            else
            {
                var okR = Read(radius, Lang.T("Radius"), 50, 20000, errors, out var r);
                var okA = Read(angle, Lang.T("Winkel"), 1, 180, errors, out var a);
                if (okR && okA) key = Catalog.FlexCurveKey(gauge.Id, r, a, stock.SystemId);
            }
            if (errors.Count > 0) { key = null; error.Text = string.Join("\n", errors); return; }
            w.Close();
        };
        w.ShowDialog();
        return key;
    }

    // ---------- Digitaladressen ----------

    public static void ShowAddresses(Window owner, PlanSession session, Action exportCsv)
    {
        var grid = new Grid();
        var first = NumberBox(1, 0);
        first.Width = 70;
        var onlyMissing = new CheckBox { Content = Lang.T("nur fehlende"), IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var auto = Theme.Button(Lang.T("Automatisch vergeben ab"), "Hash");
        auto.Margin = new Thickness(0, 0, 8, 0);
        var autoRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 10) };
        autoRow.Children.Add(auto);
        first.Height = 28;
        first.VerticalContentAlignment = VerticalAlignment.Center;
        autoRow.Children.Add(first);
        autoRow.Children.Add(onlyMissing);
        var body = new StackPanel();
        body.Children.Add(Hint(Lang.T("Weichen belegen eine Adresse, DKW und Hauptsignale zwei (Hp0/Hp1/Hp2). Ein Weichendecoder hat vier Ausgänge: Adressen 1–4 = Decoder 1, 5–8 = Decoder 2 usw.")));
        body.Children.Add(autoRow);
        body.Children.Add(grid);
        void Fill()
        {
            grid.Children.Clear();
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();
            foreach (var w in new[] { 80.0, double.NaN, 90, 70, 70 })
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            var items = Electrics.SwitchingItems(session.Plan);
            var conflicts = Electrics.AddressConflicts(items).SelectMany(c => c.Items).ToHashSet();
            grid.RowDefinitions.Add(new RowDefinition());
            string[] heads = [Lang.T("Art"), Lang.T("Bezeichnung"), Lang.T("Adresse"), Lang.T("Decoder"), Lang.T("Ausgang")];
            for (var c = 0; c < heads.Length; c++)
            {
                var h = Theme.Label(heads[c], 11, true, Theme.TextSecondaryBrush);
                h.Margin = new Thickness(0, 0, 8, 6);
                Grid.SetColumn(h, c);
                grid.Children.Add(h);
            }
            if (items.Count == 0)
            {
                grid.RowDefinitions.Add(new RowDefinition());
                var none = Theme.Label(Lang.T("Der Plan hat noch keine Weichen oder Signale."), 12, false, Theme.TextSecondaryBrush);
                Grid.SetRow(none, 1); Grid.SetColumnSpan(none, 5);
                grid.Children.Add(none);
            }
            for (var i = 0; i < items.Count; i++)
            {
                var it = items[i];
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
                void Cell(UIElement e, int col)
                {
                    if (e is FrameworkElement fe) { fe.VerticalAlignment = VerticalAlignment.Center; fe.Margin = new Thickness(0, 0, 8, 0); }
                    Grid.SetRow(e, i + 1); Grid.SetColumn(e, col);
                    grid.Children.Add(e);
                }
                Cell(Theme.Label(it.Kind), 0);
                Cell(Theme.Label(it.Label, 12, false, conflicts.Contains(it) ? Theme.DangerBrush : null), 1);
                var box = new TextBox { Text = it.Address?.ToString() ?? "", Height = 26, VerticalContentAlignment = VerticalAlignment.Center, BorderBrush = conflicts.Contains(it) ? Theme.DangerBrush : Theme.ControlBorderBrush };
                System.Windows.Automation.AutomationProperties.SetName(box, Lang.T("Adresse ") + it.Label);
                var item = it;
                void Commit()
                {
                    int? value = string.IsNullOrWhiteSpace(box.Text) ? null : int.TryParse(box.Text.Trim(), out var v) && v is > 0 and <= 2048 ? v : -1;
                    if (value == -1) { session.Say(Lang.T("Adressen von 1 bis 2048.")); box.Text = item.Address?.ToString() ?? ""; return; }
                    if (value == item.Address) return;
                    session.Edit(() =>
                    {
                        if (item.PieceId is { } pid && session.Plan.Find(pid) is { } p) p.Address = value;
                        else if (session.Plan.Signals.FirstOrDefault(s => s.Id == item.SignalId) is { } s) s.Address = value;
                    });
                    box.Dispatcher.BeginInvoke(Fill);
                }
                box.LostKeyboardFocus += (_, _) => Commit();
                box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Commit(); };
                Cell(box, 2);
                Cell(Theme.Label(it.Decoder?.ToString() ?? "–"), 3);
                Cell(Theme.Label(it.Port?.ToString() ?? "–"), 4);
            }
        }
        auto.Click += (_, _) =>
        {
            var start = (int)(Fmt.Parse(first.Text) ?? 1);
            session.AssignAddresses(Math.Clamp(start, 1, 2048), onlyMissing.IsChecked == true);
            Fill();
        };
        var csv = Theme.Button(Lang.T("Kabelplan…"), "Wire");
        var close = Theme.Button(Lang.T("Schließen"), accent: true);
        close.IsCancel = true;
        var window = Frame(owner, Lang.T("Decoder und Digitaladressen"), body, csv, close);
        window.Width = 760;
        window.Height = 620;
        csv.Click += (_, _) => exportCsv();
        close.Click += (_, _) => window.Close();
        Fill();
        window.ShowDialog();
    }

    public static void ShowText(Window owner, string title, string text, string saveLabel, Action save)
    {
        var box = new TextBox
        {
            Text = text, IsReadOnly = true, FontFamily = new FontFamily(Lang.T("Cascadia Mono, Consolas")), FontSize = 12, TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0), MinHeight = 360,
        };
        var saveButton = Theme.Button(saveLabel, "Save");
        var close = Theme.Button(Lang.T("Schließen"), accent: true);
        close.IsCancel = true;
        var window = Frame(owner, title, box, saveButton, close);
        window.Width = 780;
        window.Height = 640;
        saveButton.Click += (_, _) => save();
        close.Click += (_, _) => window.Close();
        window.ShowDialog();
    }

    // ---------- Lizenz ----------

    /// <param name="install">Öffnet den Dateidialog und spielt eine Lizenz ein; liefert eine Meldung.</param>
    public static void ShowLicense(Window owner, LicenseManager license, Func<string?> install)
    {
        var body = new StackPanel();
        var status = Theme.Label("", 14, true);
        var detail = Theme.Secondary("");
        void Sync()
        {
            status.Text = Lang.F($"TrackMaster Pro {license.EditionName}");
            status.Foreground = license.IsPro ? Theme.SuccessBrush : Theme.TextBrush;
            detail.Text = license.Summary;
        }
        Sync();
        var head = new DockPanel();
        var logo = new Image { Source = Brand.Logo, Width = 72, Height = 72, Margin = new Thickness(12, -8, 6, 0) };
        DockPanel.SetDock(logo, Dock.Right);
        head.Children.Add(logo);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(status);
        texts.Children.Add(detail);
        head.Children.Add(texts);
        body.Children.Add(head);
        var editions = new Grid { Margin = new Thickness(0, 12, 6, 8) };
        editions.ColumnDefinitions.Add(new ColumnDefinition());
        editions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        editions.ColumnDefinitions.Add(new ColumnDefinition());
        Border Card(string title, IEnumerable<string> lines, bool highlight)
        {
            var s = new StackPanel();
            s.Children.Add(Theme.Label(title, 12.5, true));
            foreach (var l in lines) s.Children.Add(Theme.Label("• " + l, 11.5, false, Theme.TextSecondaryBrush));
            return new Border { Background = highlight ? Theme.TipBackBrush : Theme.WindowBrush, CornerRadius = new CornerRadius(6), Padding = new Thickness(10), Child = s };
        }
        editions.Children.Add(Card(Lang.T("Basis (ohne Lizenz)"), EditionInfo.Basis, !license.IsPro));
        var pro = Card(Lang.T("Pro (mit Lizenz)"), EditionInfo.Pro, license.IsPro);
        Grid.SetColumn(pro, 2);
        editions.Children.Add(pro);
        body.Children.Add(editions);
        var machine = new TextBox { Text = license.MachineId, IsReadOnly = true };
        var machineRow = new DockPanel { Margin = new Thickness(0, 0, 6, 0) };
        var copy = Theme.Button(Lang.T("Kopieren"), "Copy");
        copy.Margin = new Thickness(6, 0, 0, 8);
        DockPanel.SetDock(copy, Dock.Right);
        machineRow.Children.Add(copy);
        machineRow.Children.Add(Theme.Field(Lang.T("Rechnerkennung (für eine rechnergebundene Lizenz an den Hersteller schicken)"), machine));
        copy.Click += (_, _) => { try { Clipboard.SetText(machine.Text); } catch (System.Runtime.InteropServices.COMException) { } };
        body.Children.Add(machineRow);
        var message = Theme.Label("", 12);
        body.Children.Add(message);
        var installButton = Theme.Button(Lang.T("Lizenz einspielen…"), "Open");
        var close = Theme.Button(Lang.T("Schließen"), accent: true);
        close.IsCancel = true;
        var window = Frame(owner, Lang.T("Lizenz"), body, installButton, close);
        window.Width = 720;
        window.Height = 560;
        installButton.Click += (_, _) =>
        {
            if (install() is { } m) { message.Text = m; Sync(); }
        };
        close.Click += (_, _) => window.Close();
        window.ShowDialog();
    }

    /// <summary>Hinweis, dass eine Funktion die Pro-Version braucht. Liefert true, wenn der Lizenzdialog geöffnet werden soll.</summary>
    public static bool ShowProRequired(Window owner, string feature)
    {
        var body = new StackPanel();
        body.Children.Add(Theme.Label(Lang.F($"„{feature}“ gehört zu TrackMaster Pro."), 13, true));
        body.Children.Add(new Border { Height = 8 });
        body.Children.Add(Theme.Label(Lang.T("Die Basisversion enthält alle Planungsfunktionen – Gleise, Höhen, Gleiswendel, Gelände, Elektrik und 3D. ") +
                                      Lang.T("Pläne mit bis zu 50 Gleisen lassen sich auch in der Basisversion speichern. Mit einer Pro-Lizenz kommen Speichern ohne Größengrenze, die Stücklisten, das Drucken und die Exporte dazu.")));
        body.Children.Add(new Border { Height = 8 });
        foreach (var l in EditionInfo.Pro.Skip(1)) body.Children.Add(Theme.Label("• " + l, 11.5, false, Theme.TextSecondaryBrush));
        var open = Theme.Button(Lang.T("Lizenz…"), "Shield", accent: true);
        var close = Theme.Button(Lang.T("Schließen"));
        close.IsCancel = true;
        var window = Small(owner, Lang.T("Pro-Funktion"), body, 520, 400, close, open);
        var wants = false;
        open.Click += (_, _) => { wants = true; window.Close(); };
        close.Click += (_, _) => window.Close();
        window.ShowDialog();
        return wants;
    }
}
