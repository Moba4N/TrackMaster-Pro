using System.Windows;
using System.Windows.Controls;
using TrackMaster.Core;

namespace TrackMaster.App;

public static partial class Dialogs
{
    /// <summary>Text für eine Beschriftung abfragen. null = abgebrochen.</summary>
    public static string? AskText(Window owner, string title, string label, string value = "")
    {
        var box = new TextBox { Text = value, Height = 28 };
        var body = new StackPanel();
        body.Children.Add(Theme.Field(label, box));
        var ok = Theme.Button(Lang.T("Übernehmen"), accent: true);
        var cancel = Theme.Button(Lang.T("Abbrechen"));
        ok.IsDefault = true;
        cancel.IsCancel = true;
        var window = Small(owner, title, body, 440, 200, cancel, ok);
        string? result = null;
        ok.Click += (_, _) => { if (box.Text.Trim().Length > 0) { result = box.Text.Trim(); window.Close(); } };
        cancel.Click += (_, _) => window.Close();
        window.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        window.ShowDialog();
        return result;
    }

    /// <summary>Druckart wählen: Übersicht auf einer Seite oder Bauvorlage 1:1 auf mehreren Seiten. null = abgebrochen.</summary>
    public static bool? AskPrintMode(Window owner, int tilePages, string paper)
    {
        var overview = new RadioButton { Content = Lang.T("Übersicht auf einer Seite (verkleinert, mit Stückliste)"), IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
        var full = new RadioButton { Content = Lang.F($"Bauvorlage im Maßstab 1:1 auf {tilePages} Seiten ({paper})") };
        var body = new StackPanel();
        body.Children.Add(overview);
        body.Children.Add(full);
        body.Children.Add(new Border { Height = 10 });
        body.Children.Add(Hint(Lang.T("Die Bauvorlage hat 10 mm Klebekante, Passmarken alle 100 mm und auf jeder Seite ein Prüfmaß von 100 mm. ") +
                               Lang.T("Stellen Sie im Druckdialog „Tatsächliche Größe“ bzw. 100 % ein und messen Sie das Prüfmaß nach.")));
        var ok = Theme.Button(Lang.T("Drucken"), "Print", accent: true);
        var cancel = Theme.Button(Lang.T("Abbrechen"));
        cancel.IsCancel = true;
        ok.IsDefault = true;
        var window = Small(owner, Lang.T("Drucken"), body, 560, 300, cancel, ok);
        bool? result = null;
        ok.Click += (_, _) => { result = full.IsChecked == true; window.Close(); };
        cancel.Click += (_, _) => window.Close();
        window.ShowDialog();
        return result;
    }
}
