using System.Windows;
using System.Windows.Controls;
using TrackMaster.Core;

namespace TrackMaster.App;

public static partial class Dialogs
{
    /// <summary>
    /// Module der Anlage bearbeiten: Name, Lage und Größe je Modul, Module anhängen oder entfernen.
    /// Liefert die Änderungen oder null; <paramref name="appendPlan"/> wird nach dem Schließen aufgerufen, wenn ein Plan angesetzt werden soll.
    /// </summary>
    public static IReadOnlyList<ModuleEdit>? AskModules(Window owner, LayoutPlan plan, Action appendPlan)
    {
        (int Index, TextBox Name, TextBox X, TextBox Y, TextBox W, TextBox D, CheckBox Pick) Row(ModuleEdit m)
            => (m.Index, new TextBox { Text = m.Name }, NumberBox(m.X, 0), NumberBox(m.Y, 0), NumberBox(m.Width, 0), NumberBox(m.Depth, 0),
                new CheckBox { ToolTip = Lang.T("Zum Zusammenführen wählen"), VerticalAlignment = VerticalAlignment.Center });
        var rows = ModuleLayout.Current(plan).Select(Row).ToList();
        ComboBox Box(params string[] items)
        {
            var c = new ComboBox { Height = 28, Margin = new Thickness(0, 0, 6, 4), MinWidth = 90 };
            foreach (var i in items) c.Items.Add(new ComboBoxItem { Content = i });
            c.SelectedIndex = 0;
            return c;
        }
        TextBlock Word(string t) => new() { Text = t, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 4), Foreground = Theme.TextSecondaryBrush };
        var splitModule = Box();
        var splitDir = Box(Lang.T("senkrecht (Breite teilen)"), Lang.T("waagerecht (Tiefe teilen)"));
        var splitAt = new TextBox { Width = 90, Height = 28, Margin = new Thickness(0, 0, 6, 4), ToolTip = Lang.T("bei mm (leer = Mitte)") };
        var splitButton = Theme.Button(Lang.T("Teilen"));
        var joinMove = Box();
        var joinLong = Box(Lang.T("langen Seite"), Lang.T("kurzen Seite"));
        var joinTarget = Box();
        var joinSide = Box(Lang.T("rechts"), Lang.T("unten"), Lang.T("links"), Lang.T("oben"));
        var joinButton = Theme.Button(Lang.T("Anfügen"));
        void Names()
        {
            foreach (var (box, pick) in new[] { (splitModule, 0), (joinMove, 1), (joinTarget, 0) })
            {
                var old = box.SelectedIndex;
                box.Items.Clear();
                foreach (var r in rows) box.Items.Add(new ComboBoxItem { Content = r.Name.Text });
                box.SelectedIndex = old >= 0 && old < rows.Count ? old : Math.Min(pick, rows.Count - 1);
            }
        }
        Names();
        var table = new Grid();
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        foreach (var width in new[] { 2.0, 1, 1, 1, 1 })
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        void Fill()
        {
            table.Children.Clear();
            table.RowDefinitions.Clear();
            table.RowDefinitions.Add(new RowDefinition());
            string[] heads = ["✓", Lang.T("Name"), "X", "Y", Lang.T("Breite"), Lang.T("Tiefe")];
            for (var c = 0; c < heads.Length; c++)
            {
                var t = Theme.Label(heads[c], 12, true, Theme.TextSecondaryBrush);
                Grid.SetColumn(t, c);
                table.Children.Add(t);
            }
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                table.RowDefinitions.Add(new RowDefinition());
                FrameworkElement[] cells = [row.Pick, row.Name, row.X, row.Y, row.W, row.D];
                for (var c = 0; c < cells.Length; c++)
                {
                    if (cells[c].Parent is Panel old) old.Children.Remove(cells[c]);
                    cells[c].Margin = new Thickness(0, 2, 4, 2);
                    Grid.SetRow(cells[c], i + 1); Grid.SetColumn(cells[c], c);
                    table.Children.Add(cells[c]);
                }
                var remove = Theme.Button("✕");
                remove.ToolTip = Lang.T("Modul entfernen");
                remove.IsEnabled = rows.Count > 1;
                remove.Click += (_, _) => { rows.Remove(row); Fill(); Names(); };
                Grid.SetRow(remove, i + 1); Grid.SetColumn(remove, 6);
                table.Children.Add(remove);
            }
        }
        Fill();
        var add = Theme.Button(Lang.T("Modul anhängen"), "Modules");
        add.Click += (_, _) =>
        {
            var temp = new LayoutPlan();
            foreach (var r in rows)
                if (Fmt.Parse(r.X.Text) is { } x && Fmt.Parse(r.Y.Text) is { } y && Fmt.Parse(r.W.Text) is { } w && Fmt.Parse(r.D.Text) is { } d)
                    temp.Modules.Add(new BoardModule { X = x, Y = y, Width = w, Depth = d });
            var next = ModuleLayout.NextModule(temp);
            rows.Add(Row(new ModuleEdit(-1, Lang.F($"Modul {rows.Count + 1}"), next.X, next.Y, next.Width, next.Depth)));
            Fill(); Names();
        };
        var appendButton = Theme.Button(Lang.T("Plan als Modul ansetzen…"), "Open");
        var error = ErrorText();
        List<ModuleEdit>? Edits()
        {
            var errors = new List<string>();
            var edits = new List<ModuleEdit>();
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var name = Lang.F($"Modul {i + 1}");
                Read(r.X, $"{name} X", -50000, 50000, errors, out var x);
                Read(r.Y, $"{name} Y", -50000, 50000, errors, out var y);
                Read(r.W, Lang.F($"{name} Breite"), ModuleLayout.MinSize, ModuleLayout.MaxSize, errors, out var w);
                Read(r.D, Lang.F($"{name} Tiefe"), ModuleLayout.MinSize, ModuleLayout.MaxSize, errors, out var d);
                edits.Add(new ModuleEdit(r.Index, r.Name.Text, x, y, w, d));
            }
            error.Text = string.Join("\n", errors);
            return errors.Count > 0 ? null : edits;
        }
        var merge = Theme.Button(Lang.T("Gewählte zusammenführen"));
        merge.Margin = new Thickness(0, 0, 8, 0);
        merge.Click += (_, _) =>
        {
            if (Edits() is not { } edits) return;
            var (merged, problem) = ModuleLayout.Merge(edits, rows.Select((r, i) => (r, i)).Where(t => t.r.Pick.IsChecked == true).Select(t => t.i).ToList());
            if (merged is null) { error.Text = problem!; return; }
            rows = merged.Select(Row).ToList();
            Fill(); Names();
        };
        var body = new StackPanel();
        body.Children.Add(Hint(plan.BoardShape == BoardShape.Module
            ? Lang.T("Lage (X, Y = linke obere Ecke) und Größe jedes Moduls in mm. Ein verschobenes Modul nimmt seine Gleise und sein Gelände mit; Kanten rasten auf 30 mm an Nachbarmodulen ein. Gleise über Modulgrenzen meldet die Planprüfung.")
            : Lang.F($"Die Platte ({BoardGeometry.Name(plan.BoardShape)}) wird beim Übernehmen in Module zerlegt, je Rechteck eines. Danach lassen sich Module anhängen und verschieben.")));
        body.Children.Add(table);
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        add.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(add);
        buttons.Children.Add(merge);
        buttons.Children.Add(appendButton);
        body.Children.Add(buttons);
        var splitRow = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (UIElement e in new UIElement[] { splitModule, splitDir, splitAt, splitButton }) splitRow.Children.Add(e);
        body.Children.Add(Theme.Label(Lang.T("Teilen"), 12, true, Theme.TextSecondaryBrush));
        body.Children.Add(splitRow);
        var joinRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (UIElement e in new UIElement[] { joinMove, Word(Lang.T("mit der")), joinLong, Word(Lang.T("an")), joinTarget, joinSide, joinButton }) joinRow.Children.Add(e);
        body.Children.Add(Theme.Label(Lang.T("Anfügen"), 12, true, Theme.TextSecondaryBrush));
        body.Children.Add(joinRow);
        body.Children.Add(error);
        splitButton.Click += (_, _) =>
        {
            if (Edits() is not { } edits || splitModule.SelectedIndex < 0) return;
            var i = splitModule.SelectedIndex;
            var across = splitDir.SelectedIndex == 0;
            var at = Fmt.Parse(splitAt.Text) ?? (across ? edits[i].Width : edits[i].Depth) / 2;
            var (split, problem) = ModuleLayout.Split(edits, i, across, at);
            if (split is null) { error.Text = problem!; return; }
            rows = split.Select(Row).ToList();
            splitAt.Text = "";
            Fill(); Names();
        };
        joinButton.Click += (_, _) =>
        {
            if (Edits() is not { } edits || joinMove.SelectedIndex < 0 || joinTarget.SelectedIndex < 0) return;
            var (joined, problem) = ModuleLayout.Join(edits, joinMove.SelectedIndex, joinTarget.SelectedIndex, joinSide.SelectedIndex, joinLong.SelectedIndex == 0);
            if (joined is null) { error.Text = problem!; return; }
            var picks = rows.Select(r => r.Pick.IsChecked).ToList();
            rows = joined.Select(Row).ToList();
            for (var k = 0; k < picks.Count && k < rows.Count; k++) rows[k].Pick.IsChecked = picks[k];
            Fill(); Names();
        };
        var ok = Theme.Button(Lang.T("Übernehmen"), accent: true);
        ok.IsDefault = true;
        var cancel = Theme.Button(Lang.T("Abbrechen"));
        cancel.IsCancel = true;
        var win = Small(owner, Lang.T("Module"), new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 720, 600, cancel, ok);
        IReadOnlyList<ModuleEdit>? result = null;
        var append = false;
        appendButton.Click += (_, _) => { append = true; win.Close(); };
        ok.Click += (_, _) =>
        {
            if (Edits() is not { } edits) return;
            result = edits;
            win.Close();
        };
        win.ShowDialog();
        if (append) appendPlan();
        return result;
    }
}
