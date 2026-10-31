using System.Windows;
using System.Windows.Controls;
using TrackMaster.Core;

namespace TrackMaster.App;

public static partial class Dialogs
{
    /// <summary>
    /// Maße des Holz-Unterbaus. Liefert die Maße oder null bei Abbruch; <paramref name="remove"/> ist true, wenn der Unterbau entfernt werden soll.
    /// </summary>
    public static BenchworkSpec? AskBenchwork(Window owner, LayoutPlan plan, out bool remove)
    {
        var s = plan.Benchwork ?? BenchworkSpec.Defaults(plan.Gauge);
        TextBox Box(double v) => NumberBox(v, 0);
        var height = Box(s.Height); var top = Box(s.TopThickness); var ft = Box(s.FrameThickness); var fh = Box(s.FrameHeight); var cross = Box(s.CrossSpacing);
        var style = new ComboBox { Height = 28, Items = { new ComboBoxItem { Content = Lang.T("Kantholz") }, new ComboBoxItem { Content = Lang.T("L-Bein aus zwei Latten") } }, SelectedIndex = (int)s.LegStyle };
        var lw = Box(s.LegWidth); var ld = Box(s.LegDepth); var lspace = Box(s.LegSpacing);
        var braces = new CheckBox { Content = Lang.T("Fußtraversen in Höhe"), IsChecked = s.Braces, Margin = new Thickness(0, 6, 0, 2) };
        var bh = Box(s.BraceHeight);
        var feet = new CheckBox { Content = Lang.T("Stellfüße, Höhe"), IsChecked = s.Feet, Margin = new Thickness(0, 6, 0, 2) };
        var foot = Box(s.FootHeight);
        var stock = Box(s.StockLength); var sw = Box(s.SheetWidth); var sd = Box(s.SheetDepth);
        StackPanel Check(CheckBox c, TextBox b) { var p = new StackPanel(); p.Children.Add(c); p.Children.Add(b); return p; }
        var error = ErrorText();
        var body = new StackPanel();
        body.Children.Add(Hint(Lang.F($"Holzkonstrukt unter der Platte ({BoardGeometry.Describe(plan)}): Zargen rundum und Querriegel unter jedem Plattenteil, Beine innen in den Ecken und entlang der langen Seiten. Alle Maße in mm. Die 3D-Ansicht zeigt den Unterbau.")));
        body.Children.Add(Columns(Theme.Field(Lang.T("Höhe Oberkante Platte über Boden"), height), Theme.Field(Lang.T("Plattenstärke (0 = offener Rahmen)"), top)));
        body.Children.Add(Cells(Theme.Field(Lang.T("Latte für Rahmen: Stärke"), ft), Theme.Field(Lang.T("Latte für Rahmen: Höhe"), fh), Theme.Field(Lang.T("Querriegel höchstens alle"), cross)));
        body.Children.Add(Cells(Theme.Field(Lang.T("Beine"), style), Theme.Field(Lang.T("Bein: Breite"), lw), Theme.Field(Lang.T("Bein: Tiefe"), ld), Theme.Field(Lang.T("Beine höchstens alle"), lspace)));
        body.Children.Add(Columns(Check(braces, bh), Check(feet, foot)));
        body.Children.Add(Cells(Theme.Field(Lang.T("Lattenlänge im Handel"), stock), Theme.Field(Lang.T("Plattenformat: Breite"), sw), Theme.Field(Lang.T("Plattenformat: Tiefe"), sd)));
        body.Children.Add(error);
        var ok = Theme.Button(Lang.T("Übernehmen"), "Table", accent: true);
        ok.IsDefault = true;
        var cancel = Theme.Button(Lang.T("Abbrechen"));
        cancel.IsCancel = true;
        var drop = Theme.Button(Lang.T("Entfernen"));
        drop.Visibility = plan.Benchwork is null ? Visibility.Collapsed : Visibility.Visible;
        var w = Small(owner, Lang.T("Unterbau"), body, 680, 560, drop, cancel, ok);
        BenchworkSpec? result = null;
        var doRemove = false;
        drop.Click += (_, _) => { doRemove = true; w.Close(); };
        ok.Click += (_, _) =>
        {
            double V(TextBox b) => Fmt.Parse(b.Text) ?? double.NaN;
            var spec = new BenchworkSpec
            {
                Height = V(height), TopThickness = V(top), FrameThickness = V(ft), FrameHeight = V(fh), CrossSpacing = V(cross),
                LegStyle = style.SelectedIndex == 1 ? BenchLegStyle.LBein : BenchLegStyle.Kantholz, LegWidth = V(lw), LegDepth = V(ld), LegSpacing = V(lspace),
                Braces = braces.IsChecked == true, BraceHeight = V(bh), Feet = feet.IsChecked == true, FootHeight = V(foot),
                StockLength = V(stock), SheetWidth = V(sw), SheetDepth = V(sd),
            };
            var errors = Benchwork.Validate(spec);
            if (errors.Count > 0) { error.Text = string.Join("\n", errors); return; }
            result = spec;
            w.Close();
        };
        w.ShowDialog();
        remove = doRemove;
        return result;
    }

    /// <summary>Mehrere Felder nebeneinander, gleich breit.</summary>
    private static System.Windows.Controls.Primitives.UniformGrid Cells(params UIElement[] items)
    {
        var g = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = items.Length };
        foreach (var i in items)
        {
            if (i is FrameworkElement f) f.Margin = new Thickness(0, 0, 8, 0);
            g.Children.Add(i);
        }
        return g;
    }

    /// <summary>Materialliste und Zuschnitt des Unterbaus.</summary>
    public static void ShowBenchwork(Window owner, BenchworkPlan b, Action edit, Action saveCsv)
    {
        var body = new StackPanel();
        foreach (var n in b.Notes) body.Children.Add(new TextBlock { Text = "• " + n, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2), Foreground = Theme.TextSecondaryBrush });
        Grid Table(string[] heads, IEnumerable<string[]> rows)
        {
            var table = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var _ in heads) table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            table.ColumnDefinitions[^1].Width = new GridLength(1, GridUnitType.Star);
            void Cell(string text, int row, int col, bool head)
            {
                var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 14, 3), FontWeight = head ? FontWeights.SemiBold : FontWeights.Normal };
                if (head) t.Foreground = Theme.TextSecondaryBrush;
                Grid.SetRow(t, row); Grid.SetColumn(t, col);
                table.Children.Add(t);
            }
            table.RowDefinitions.Add(new RowDefinition());
            for (var c = 0; c < heads.Length; c++) Cell(heads[c], 0, c, true);
            var i = 1;
            foreach (var r in rows)
            {
                table.RowDefinitions.Add(new RowDefinition());
                var line = new Border { BorderBrush = Theme.BorderBrush, BorderThickness = new Thickness(0, 1, 0, 0) };
                Grid.SetRow(line, i); Grid.SetColumnSpan(line, heads.Length);
                table.Children.Add(line);
                for (var c = 0; c < r.Length; c++) Cell(r[c], i, c, false);
                i++;
            }
            return table;
        }
        body.Children.Add(Table([Lang.T("Menge"), Lang.T("Material"), Lang.T("Maß"), Lang.T("Hinweis")],
            b.Materials.Select(m => new[] { $"{Fmt.Num(m.Quantity, 0)} {m.Unit}", m.Item, m.Size, m.Note })));
        body.Children.Add(new TextBlock { Text = Lang.T("Zuschnitt"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
        body.Children.Add(Table([Lang.T("Anzahl"), Lang.T("Länge"), Lang.T("Teil"), Lang.T("Querschnitt")],
            b.Cuts.Select(c => new[] { $"{c.Count} ×", $"{Fmt.Num(c.Length, 0)} mm", c.Use, $"{c.Section} mm" })));
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var change = Theme.Button(Lang.T("Maße ändern…"));
        var copy = Theme.Button(Lang.T("Kopieren"));
        copy.Click += (_, _) => { try { Clipboard.SetText(b.ToText()); } catch (System.Runtime.InteropServices.COMException) { } };
        var csv = Theme.Button(Lang.T("Als CSV speichern…"));
        csv.Click += (_, _) => saveCsv();
        var close = Theme.Button(Lang.T("Schließen"), accent: true);
        close.IsCancel = true;
        var w = Small(owner, Lang.T("Unterbau – Materialliste"), scroll, 720, 640, change, copy, csv, close);
        var doEdit = false;
        change.Click += (_, _) => { doEdit = true; w.Close(); };
        close.Click += (_, _) => w.Close();
        w.ShowDialog();
        if (doEdit) edit();
    }
}
