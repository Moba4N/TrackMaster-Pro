using System.Windows;
using System.Windows.Controls;
using TrackMaster.Core;

namespace TrackMaster.App;

/// <summary>Einfache Fenster im Windows-11-Stil: Rahmen und Tastenkürzel.</summary>
public static partial class Dialogs
{
    private static Window Frame(Window owner, string title, UIElement body, params Button[] footer)
    {
        var root = new DockPanel { Background = Theme.PanelBrush };
        var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var b in footer) foot.Children.Add(b);
        var footBorder = new Border
        {
            Background = Theme.WindowBrush, BorderBrush = Theme.BorderBrush, BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 12, 14, 6), Child = foot,
        };
        DockPanel.SetDock(footBorder, Dock.Bottom);
        root.Children.Add(footBorder);
        var head = Theme.Label(title, 20, true);
        head.Margin = new Thickness(20, 16, 20, 8);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        root.Children.Add(new ScrollViewer { Content = body, Padding = new Thickness(20, 0, 20, 12), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        return new Window
        {
            Owner = owner, Title = title, Content = root, Width = 640, Height = 560, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = Theme.Font, FontSize = 12 * Theme.FontScale, Foreground = Theme.TextBrush, ResizeMode = ResizeMode.CanResizeWithGrip, ShowInTaskbar = false,
        };
    }

    public static readonly (string Keys, string Action)[] Shortcuts =
    [
        (Lang.T("Klick auf Gleis in der Bibliothek"), Lang.T("am aktiven Gleisende ansetzen")),
        (Lang.T("Gleis aus der Bibliothek ziehen"), Lang.T("frei platzieren, rastet an nahen Enden ein")),
        (Lang.T("Klick auf orangefarbenes Ende"), Lang.T("als aktives Gleisende wählen")),
        (Lang.T("Gleis ziehen"), Lang.T("verschieben (mit Einrasten)")),
        (Lang.T("Ziehen auf freier Fläche"), Lang.T("Rahmen zum Auswählen")),
        (Lang.T("Rechte/mittlere Maustaste, Leertaste"), Lang.T("Ansicht verschieben")),
        (Lang.T("Mausrad"), Lang.T("zoomen")),
        (Lang.T("R / Umschalt+R"), Lang.T("um 15° drehen")),
        ("F", Lang.T("spiegeln")),
        ("A", Lang.T("Anschluss wechseln")),
        ("E", Lang.T("nächstes offenes Gleisende")),
        (Lang.T("Entf"), Lang.T("löschen")),
        (Lang.T("Pfeiltasten"), Lang.T("1 mm verschieben (Umschalt: 10 mm)")),
        (Lang.T("Strg+Z / Strg+Y"), Lang.T("rückgängig / wiederholen")),
        (Lang.T("Strg+D"), Lang.T("duplizieren")),
        (Lang.T("Strg+A"), Lang.T("alles auswählen")),
        (Lang.T("Strg+N / Strg+O / Strg+S"), Lang.T("neu / öffnen / speichern")),
        ("0", Lang.T("ganze Anlage zeigen")),
    ];

    public static void ShowShortcuts(Window owner)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < Shortcuts.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            var key = new Border
            {
                Background = Theme.WindowBrush, BorderBrush = Theme.ControlBorderBrush, BorderThickness = new Thickness(1, 1, 1, 2),
                CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 2, 16, 2),
                HorizontalAlignment = HorizontalAlignment.Left, Child = Theme.Label(Shortcuts[i].Keys, 11),
            };
            var action = Theme.Label(Shortcuts[i].Action);
            action.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(key, i); Grid.SetRow(action, i); Grid.SetColumn(action, 1);
            grid.Children.Add(key); grid.Children.Add(action);
        }
        var close = Theme.Button(Lang.T("Schließen"), accent: true);
        close.IsDefault = true;
        close.IsCancel = true;
        var window = Frame(owner, Lang.T("Bedienung und Tastenkürzel"), grid, close);
        close.Click += (_, _) => window.Close();
        window.ShowDialog();
    }
}
