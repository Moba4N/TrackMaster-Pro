using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrackMaster.Core;

namespace TrackMaster.App;

public static partial class Dialogs
{
    /// <summary>Auswahl einer Gebäudevorlage für die Spur des Plans, nach Art gruppiert mit Maßen.</summary>
    public static BuildingKit? AskBuildingKit(Window owner, Gauge gauge)
    {
        var kits = BuildingKits.For(gauge);
        var body = new DockPanel();
        var hint = Hint(kits.FirstOrDefault()?.Converted == true
            ? $"Für Spur {gauge.Id} gibt es keine eigenen Bausätze; die Vorlagen sind maßstäblich auf {gauge.Scale} umgerechnet."
            : $"Vorlagen nach Bausätzen in Spur {gauge.Id}, mit Grundmaß und Höhe. Das Gebäude wird in der Mitte der Ansicht gesetzt und lässt sich danach verschieben und drehen.");
        DockPanel.SetDock(hint, Dock.Top);
        body.Children.Add(hint);
        var filter = new TextBox { Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(6, 4, 6, 4), ToolTip = "Name oder Artikelnummer suchen" };
        DockPanel.SetDock(filter, Dock.Top);
        body.Children.Add(filter);
        var list = new ListBox { BorderBrush = Theme.BorderBrush, MinHeight = 260 };
        void Fill()
        {
            list.Items.Clear();
            var q = filter.Text.Trim();
            foreach (var cat in BuildingKits.Categories)
            {
                var items = kits.Where(k => k.Category == cat && (q.Length == 0 || k.Label.Contains(q, StringComparison.CurrentCultureIgnoreCase))).ToList();
                if (items.Count == 0) continue;
                list.Items.Add(new ListBoxItem { Content = Theme.Label(cat, 12, true, Theme.TextSecondaryBrush), IsEnabled = false, Margin = new Thickness(0, 6, 0, 0) });
                foreach (var k in items)
                {
                    var row = new StackPanel { Margin = new Thickness(8, 2, 0, 2) };
                    row.Children.Add(Theme.Label(k.Label, 12.5, true));
                    row.Children.Add(Theme.Label(k.Details, 11.5, false, Theme.TextSecondaryBrush));
                    list.Items.Add(new ListBoxItem { Content = row, Tag = k });
                }
            }
            list.SelectedItem = list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is BuildingKit);
        }
        filter.TextChanged += (_, _) => Fill();
        Fill();
        body.Children.Add(list);
        var ok = Theme.Button("Setzen", "Building", accent: true);
        ok.IsDefault = true;
        var cancel = Theme.Button("Abbrechen");
        cancel.IsCancel = true;
        var w = Small(owner, "Gebäudevorlage", body, 520, 600, cancel, ok);
        BuildingKit? result = null;
        void Take()
        {
            if (list.SelectedItem is ListBoxItem { Tag: BuildingKit k }) { result = k; w.Close(); }
        }
        ok.Click += (_, _) => Take();
        list.MouseDoubleClick += (_, _) => Take();
        w.ShowDialog();
        return result;
    }
}
