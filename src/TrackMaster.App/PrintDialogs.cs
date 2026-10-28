using System.Windows;
using System.Windows.Controls;
using TrackMaster.Core;

namespace TrackMaster.App;

public static partial class Dialogs
{
    /// <summary>Druckart wählen: Übersicht auf einer Seite oder Bauvorlage 1:1 auf mehreren Seiten. null = abgebrochen.</summary>
    public static bool? AskPrintMode(Window owner, int tilePages, string paper)
    {
        var overview = new RadioButton { Content = "Übersicht auf einer Seite (verkleinert, mit Stückliste)", IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
        var full = new RadioButton { Content = $"Bauvorlage im Maßstab 1:1 auf {tilePages} Seiten ({paper})" };
        var body = new StackPanel();
        body.Children.Add(overview);
        body.Children.Add(full);
        body.Children.Add(new Border { Height = 10 });
        body.Children.Add(Hint("Die Bauvorlage hat 10 mm Klebekante, Passmarken alle 100 mm und auf jeder Seite ein Prüfmaß von 100 mm. " +
                               "Stellen Sie im Druckdialog „Tatsächliche Größe“ bzw. 100 % ein und messen Sie das Prüfmaß nach."));
        var ok = Theme.Button("Drucken", "Print", accent: true);
        var cancel = Theme.Button("Abbrechen");
        cancel.IsCancel = true;
        ok.IsDefault = true;
        var window = Small(owner, "Drucken", body, 560, 300, cancel, ok);
        bool? result = null;
        ok.Click += (_, _) => { result = full.IsChecked == true; window.Close(); };
        cancel.Click += (_, _) => window.Close();
        window.ShowDialog();
        return result;
    }
}
