using System.Windows;
using System.Windows.Controls;
using TrackMaster.Core;

namespace TrackMaster.App;

public static partial class Dialogs
{
    /// <summary>
    /// Ebenen bearbeiten: Name, Grundhöhe, sichtbar, aktive Ebene; Ebenen hinzufügen oder entfernen.
    /// Liefert die Zeilen und die aktive Ebene oder null bei Abbruch. <paramref name="moveSelection"/>: Gewählte auf die aktive Ebene legen.
    /// </summary>
    public static (IReadOnlyList<LevelEdit> Edits, int Active)? AskLevels(Window owner, LayoutPlan plan, int selected, out bool moveSelection)
    {
        var rows = plan.Levels.Select((l, i) => (Index: i, Name: new TextBox { Text = l.Name, Margin = new Thickness(0, 2, 6, 2) },
            Height: new TextBox { Text = Fmt.Num(l.Height, 0), Margin = new Thickness(0, 2, 6, 2) }, Visible: new CheckBox { IsChecked = l.Visible, VerticalAlignment = VerticalAlignment.Center })).ToList();
        var active = plan.ActiveLevel;
        var table = new Grid();
        foreach (var w in new[] { 0.6, 0.5, 2.2, 1.0, 1.1, 0.5 })
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
        var add = Theme.Button(Lang.T("Ebene hinzufügen"));
        void Fill()
        {
            table.Children.Clear();
            table.RowDefinitions.Clear();
            table.RowDefinitions.Add(new RowDefinition());
            string[] heads = [Lang.T("Aktiv"), "👁", Lang.T("Name"), Lang.T("Grundhöhe"), "", ""];
            for (var c = 0; c < heads.Length; c++)
            {
                var t = Theme.Label(heads[c], 12, true, Theme.TextSecondaryBrush);
                Grid.SetColumn(t, c);
                table.Children.Add(t);
            }
            for (var i = 0; i < rows.Count; i++)
            {
                var k = i;
                var r = rows[k];
                foreach (var v in new FrameworkElement[] { r.Name, r.Height, r.Visible }) (v.Parent as Panel)?.Children.Remove(v);
                table.RowDefinitions.Add(new RowDefinition());
                var radio = new RadioButton { GroupName = "activeLevel", IsChecked = active == k, VerticalAlignment = VerticalAlignment.Center, ToolTip = Lang.T("Aktive Ebene") };
                radio.Checked += (_, _) => { active = k; r.Visible.IsChecked = true; };
                var only = Theme.Button(Lang.T("Nur diese"));
                only.ToolTip = Lang.T("Nur diese Ebene zeigen");
                only.Margin = new Thickness(0, 2, 6, 2);
                only.Click += (_, _) => { foreach (var o in rows) o.Visible.IsChecked = ReferenceEquals(o.Name, r.Name); active = k; Fill(); };
                var remove = Theme.Button("✕");
                remove.ToolTip = Lang.T("Ebene entfernen – ihre Gleise kommen auf die Ebene darunter");
                remove.IsEnabled = rows.Count > 1;
                remove.Margin = new Thickness(0, 2, 0, 2);
                remove.Click += (_, _) => { rows.RemoveAt(k); if (active >= k) active = Math.Max(0, active - 1); Fill(); };
                r.Visible.ToolTip = Lang.T("Sichtbar");
                foreach (var (el, col) in new (FrameworkElement, int)[] { (radio, 0), (r.Visible, 1), (r.Name, 2), (r.Height, 3), (only, 4), (remove, 5) })
                {
                    Grid.SetRow(el, k + 1);
                    Grid.SetColumn(el, col);
                    table.Children.Add(el);
                }
            }
            add.IsEnabled = rows.Count < LayoutPlan.MaxLevels;
        }
        add.Click += (_, _) =>
        {
            if (rows.Count >= LayoutPlan.MaxLevels) return;
            var top = rows.Select(r => Fmt.Parse(r.Height.Text) ?? 0).DefaultIfEmpty(0).Max();
            rows.Add((-1, new TextBox { Text = PlanLevel.DefaultName(rows.Count), Margin = new Thickness(0, 2, 6, 2) },
                new TextBox { Text = Fmt.Num(top + plan.LevelStep, 0), Margin = new Thickness(0, 2, 6, 2) }, new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center }));
            active = rows.Count - 1;
            Fill();
        };
        var all = Theme.Button(Lang.T("Alle zeigen"));
        all.Click += (_, _) => { foreach (var r in rows) r.Visible.IsChecked = true; };
        var move = Theme.Button(Lang.F($"Gewählte ({selected}) auf aktive Ebene"));
        move.ToolTip = Lang.T("Gewählte Gleise bzw. das gewählte Objekt auf die aktive Ebene legen");
        move.IsEnabled = selected > 0;
        Fill();
        var error = ErrorText();
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var b in new[] { add, all, move }) { b.Margin = new Thickness(0, 0, 6, 6); buttons.Children.Add(b); }
        var body = new StackPanel();
        body.Children.Add(Hint(Lang.T("Neue Gleise kommen auf die aktive Ebene und beginnen in ihrer Grundhöhe. Ausgeblendete Ebenen fehlen in Plan und 3D-Ansicht, bleiben aber in Stückliste und Planprüfung. Ändert sich die Grundhöhe, wandern die Gleise der Ebene mit.")));
        body.Children.Add(new ScrollViewer { Content = table, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        body.Children.Add(buttons);
        body.Children.Add(error);
        var ok = Theme.Button(Lang.T("Übernehmen"), accent: true);
        ok.IsDefault = true;
        var cancel = Theme.Button(Lang.T("Abbrechen"));
        cancel.IsCancel = true;
        var win = Small(owner, Lang.T("Ebenen"), body, 640, 560, cancel, ok);
        (IReadOnlyList<LevelEdit>, int)? result = null;
        var doMove = false;
        List<LevelEdit>? Edits()
        {
            var errors = new List<string>();
            var edits = new List<LevelEdit>();
            for (var i = 0; i < rows.Count; i++)
            {
                Read(rows[i].Height, Lang.F($"Ebene {i + 1} Grundhöhe"), -1000, 5000, errors, out var h);
                edits.Add(new LevelEdit(rows[i].Index, rows[i].Name.Text, h, rows[i].Visible.IsChecked == true || i == active));
            }
            if (errors.Count > 0) { error.Text = string.Join("\n", errors); return null; }
            return edits;
        }
        ok.Click += (_, _) => { if (Edits() is { } e) { result = (e, active); win.Close(); } };
        move.Click += (_, _) => { if (Edits() is { } e) { result = (e, active); doMove = true; win.Close(); } };
        win.ShowDialog();
        moveSelection = doMove;
        return result;
    }
}
