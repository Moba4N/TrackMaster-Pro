using System.Windows;
using System.Windows.Controls;
using TrackMaster.Core;

namespace TrackMaster.App;

public static partial class Dialogs
{
    /// <summary>
    /// Verlauf: bis zu 20 Schritte in jede Richtung. Liefert die Zahl der Schritte, positiv = wiederholen, negativ = zurücknehmen, 0 = nichts.
    /// </summary>
    public static int AskHistory(Window owner, EditHistory history)
    {
        var redo = history.RedoLabels;
        var undo = history.UndoLabels;
        var list = new ListBox { BorderThickness = new Thickness(0), Margin = new Thickness(0, 4, 0, 8), MaxHeight = 420 };
        for (var i = redo.Count - 1; i >= 0; i--)
            list.Items.Add(new ListBoxItem { Content = $"{Lang.F($"{i + 1} vor")}   {redo[i]}", Tag = i + 1, Opacity = 0.7, Padding = new Thickness(8, 5, 8, 5) });
        var now = new ListBoxItem { Content = $"{Lang.T("Jetzt")}   {Lang.T("Aktueller Stand")}", Tag = 0, FontWeight = FontWeights.SemiBold, Padding = new Thickness(8, 5, 8, 5), IsSelected = true };
        list.Items.Add(now);
        for (var i = 0; i < undo.Count; i++)
            list.Items.Add(new ListBoxItem { Content = $"{Lang.F($"{i + 1} zurück")}   {undo[i]}", Tag = -(i + 1), Padding = new Thickness(8, 5, 8, 5) });
        var body = new StackPanel();
        body.Children.Add(Hint(Lang.T("Ein Klick springt zu diesem Stand: oben die zurückgenommenen Schritte (Wiederholen), unten die letzten Änderungen (Rückgängig).")));
        body.Children.Add(list);
        var ok = Theme.Button(Lang.T("Springen"), accent: true);
        ok.IsDefault = true;
        var cancel = Theme.Button(Lang.T("Abbrechen"));
        cancel.IsCancel = true;
        var w = Small(owner, Lang.T("Verlauf"), body, 520, 600, cancel, ok);
        var result = 0;
        void Pick()
        {
            if (list.SelectedItem is ListBoxItem { Tag: int steps }) result = steps;
            w.Close();
        }
        ok.Click += (_, _) => Pick();
        list.MouseDoubleClick += (_, _) => Pick();
        w.Loaded += (_, _) => list.ScrollIntoView(now);
        w.ShowDialog();
        return result;
    }
}
