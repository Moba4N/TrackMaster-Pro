using System.Windows;
using System.Windows.Controls;
using TrackMaster.Core;

namespace TrackMaster.App;

/// <summary>Einfache Fenster im Windows-11-Stil: Stückliste und Tastenkürzel.</summary>
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
            FontFamily = Theme.Font, FontSize = 12, Foreground = Theme.TextBrush, ResizeMode = ResizeMode.CanResizeWithGrip, ShowInTaskbar = false,
        };
    }

    public static void ShowBillOfMaterials(Window owner, LayoutPlan plan, Action exportCsv)
    {
        var rows = BillOfMaterials.Build(plan);
        var stats = plan.Statistics();
        var body = new StackPanel();
        body.Children.Add(Theme.Secondary($"{plan.Name} · Spur {plan.GaugeId} · {stats.Pieces} Teile · {Fmt.Num(stats.TrackLengthMm / 1000, 2)} m Gleis"));
        if (rows.Count == 0)
        {
            var empty = Theme.Label("Der Plan enthält noch keine Gleise.", 12, false, Theme.TextSecondaryBrush);
            empty.Margin = new Thickness(0, 16, 0, 0);
            body.Children.Add(empty);
        }
        else
        {
            var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            foreach (var w in new[] { 90.0, double.NaN, 150, 60 })
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            void Cell(UIElement e, int row, int col, bool right = false)
            {
                if (e is FrameworkElement fe) { fe.Margin = new Thickness(0, 6, 8, 6); if (right) fe.HorizontalAlignment = HorizontalAlignment.Right; }
                Grid.SetRow(e, row); Grid.SetColumn(e, col);
                grid.Children.Add(e);
            }
            grid.RowDefinitions.Add(new RowDefinition());
            string[] heads = ["Artikel", "Bezeichnung", "System", "Anzahl"];
            for (var c = 0; c < heads.Length; c++) Cell(Theme.Label(heads[c], 11, true, Theme.TextSecondaryBrush), 0, c, c == 3);
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                grid.RowDefinitions.Add(new RowDefinition());
                var line = new Border { BorderBrush = Theme.BorderBrush, BorderThickness = new Thickness(0, 1, 0, 0) };
                Grid.SetRow(line, i + 1); Grid.SetColumnSpan(line, 4);
                grid.Children.Add(line);
                Cell(Theme.Label(r.Def.Number), i + 1, 0);
                var name = new StackPanel();
                name.Children.Add(Theme.Label(r.Def.Name));
                name.Children.Add(Theme.Secondary(r.Def.Spec));
                Cell(name, i + 1, 1);
                Cell(Theme.Label(r.Def.SystemName), i + 1, 2);
                Cell(Theme.Label(r.Count.ToString(), 12, true), i + 1, 3, right: true);
            }
            body.Children.Add(grid);
        }
        var extras = BillOfMaterials.Accessories(plan);
        if (extras.Count > 0)
        {
            var head = Theme.Label("Zubehör", 13, true);
            head.Margin = new Thickness(0, 14, 0, 4);
            body.Children.Add(head);
            foreach (var a in extras)
            {
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                var count = Theme.Label(a.Count.ToString(), 12, true);
                DockPanel.SetDock(count, Dock.Right);
                row.Children.Add(count);
                row.Children.Add(Theme.Label($"{a.Name} – {a.Detail}"));
                body.Children.Add(row);
            }
        }
        var csv = Theme.Button("Als CSV speichern…", "Csv");
        var close = Theme.Button("Schließen", accent: true);
        close.IsDefault = true;
        close.IsCancel = true;
        var window = Frame(owner, "Stückliste", body, csv, close);
        csv.Click += (_, _) => exportCsv();
        close.Click += (_, _) => window.Close();
        window.ShowDialog();
    }

    public static readonly (string Keys, string Action)[] Shortcuts =
    [
        ("Klick auf Gleis in der Bibliothek", "am aktiven Gleisende ansetzen"),
        ("Gleis aus der Bibliothek ziehen", "frei platzieren, rastet an nahen Enden ein"),
        ("Klick auf orangefarbenes Ende", "als aktives Gleisende wählen"),
        ("Gleis ziehen", "verschieben (mit Einrasten)"),
        ("Ziehen auf freier Fläche", "Rahmen zum Auswählen"),
        ("Rechte/mittlere Maustaste, Leertaste", "Ansicht verschieben"),
        ("Mausrad", "zoomen"),
        ("R / Umschalt+R", "um 15° drehen"),
        ("F", "spiegeln"),
        ("A", "Anschluss wechseln"),
        ("E", "nächstes offenes Gleisende"),
        ("Entf", "löschen"),
        ("Pfeiltasten", "1 mm verschieben (Umschalt: 10 mm)"),
        ("Strg+Z / Strg+Y", "rückgängig / wiederholen"),
        ("Strg+D", "duplizieren"),
        ("Strg+A", "alles auswählen"),
        ("Strg+N / Strg+O / Strg+S", "neu / öffnen / speichern"),
        ("0", "ganze Anlage zeigen"),
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
        var close = Theme.Button("Schließen", accent: true);
        close.IsDefault = true;
        close.IsCancel = true;
        var window = Frame(owner, "Bedienung und Tastenkürzel", grid, close);
        close.Click += (_, _) => window.Close();
        window.ShowDialog();
    }
}
