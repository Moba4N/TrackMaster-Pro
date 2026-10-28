using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TrackMaster.Core;
using TrackMaster.Licensing;

namespace TrackMaster.App;

/// <summary>Logo des Programms aus den eingebetteten Bildern.</summary>
public static class Brand
{
    public static ImageSource Logo { get; } = Load("Logo.png");
    public static ImageSource LogoSmall { get; } = Load("LogoSmall.png");

    private static BitmapImage Load(string name)
    {
        var b = new BitmapImage();
        b.BeginInit();
        b.UriSource = new Uri($"pack://application:,,,/Assets/{name}");
        b.CacheOption = BitmapCacheOption.OnLoad;
        b.EndInit();
        b.Freeze();
        return b;
    }
}

public static partial class Dialogs
{
    /// <summary>Fragt einen Winkel in Grad ab (Ansicht drehen, Auswahl frei drehen).</summary>
    public static double? AskAngle(Window owner, string title, string hint, double value, string action)
    {
        var box = NumberBox(value, 1);
        var error = ErrorText();
        var body = new StackPanel();
        body.Children.Add(Hint(hint));
        body.Children.Add(Theme.Field(Lang.T("Winkel (°, positiv = im Uhrzeigersinn)"), box));
        var quick = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var a in new[] { -90, -45, -15, 15, 45, 90 })
        {
            var b = Theme.Button($"{(a > 0 ? "+" : "")}{a}°");
            b.Click += (_, _) => box.Text = Fmt.Num(a, 1);
            quick.Children.Add(b);
        }
        body.Children.Add(quick);
        body.Children.Add(error);
        var ok = Theme.Button(action, "RotateFree", accent: true);
        ok.IsDefault = true;
        var cancel = Theme.Button(Lang.T("Abbrechen"));
        cancel.IsCancel = true;
        var w = Small(owner, title, body, 460, 360, cancel, ok);
        double? result = null;
        ok.Click += (_, _) =>
        {
            var errors = new List<string>();
            Read(box, Lang.T("Winkel"), -360, 360, errors, out var v);
            if (errors.Count > 0) { error.Text = string.Join("\n", errors); return; }
            result = v;
            w.Close();
        };
        w.ShowDialog();
        return result;
    }

    /// <summary>Sprache der Oberfläche wählen; zweisprachig beschriftet, damit jeder sie findet.</summary>
    public static Language? AskLanguage(Window owner, Language current)
    {
        var body = new StackPanel();
        var list = new ListBox { BorderThickness = new Thickness(0), Margin = new Thickness(0, 4, 0, 8) };
        foreach (var (lang, text) in new[] { (Language.Deutsch, "Deutsch"), (Language.English, "English") })
            list.Items.Add(new ListBoxItem { Content = text, Tag = lang, IsSelected = lang == current, Padding = new Thickness(8, 6, 8, 6) });
        body.Children.Add(Hint("Sprache der Oberfläche / User interface language"));
        body.Children.Add(list);
        body.Children.Add(Hint("Das Programm startet danach neu. / The program restarts afterwards."));
        var ok = Theme.Button("OK", accent: true);
        ok.IsDefault = true;
        var cancel = Theme.Button(Lang.T("Abbrechen"));
        cancel.IsCancel = true;
        var w = Small(owner, "Sprache / Language", body, 460, 360, cancel, ok);
        Language? result = null;
        ok.Click += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: Language l }) result = l;
            w.Close();
        };
        w.ShowDialog();
        return result;
    }

    /// <summary>Design, Schriftart und Schriftgröße wählen. Liefert die neue Auswahl oder null.</summary>
    public static AppearanceSettings? AskAppearance(Window owner, AppearanceSettings current)
    {
        var design = current.Design;
        var body = new StackPanel();
        body.Children.Add(Hint(Lang.T("Das Design bestimmt Akzentfarbe, Zeichenfläche und Gleisdarstellung. Nach „Übernehmen“ startet das Fenster kurz neu; der geöffnete Plan bleibt erhalten.")));
        body.Children.Add(Theme.Label(Lang.T("Design"), 12.5, true));
        var cards = new WrapPanel { Margin = new Thickness(0, 6, 0, 10) };
        var borders = new List<(Border Card, string Id)>();
        void Mark()
        {
            foreach (var (card, id) in borders)
            {
                card.BorderBrush = id == design ? Theme.AccentBrush : Theme.BorderBrush;
                card.BorderThickness = new Thickness(id == design ? 2 : 1);
                card.Background = id == design ? Theme.TipBackBrush : Theme.PanelBrush;
            }
        }
        foreach (var d in Design.All)
        {
            var s = new StackPanel { Width = 196 };
            s.Children.Add(Preview(d));
            s.Children.Add(Theme.Label(d.Name, 12.5, true));
            s.Children.Add(Theme.Label(d.Description, 11, false, Theme.TextSecondaryBrush));
            var card = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8), Margin = new Thickness(0, 0, 10, 10), Child = s, Cursor = System.Windows.Input.Cursors.Hand };
            var id = d.Id;
            card.MouseLeftButtonUp += (_, _) => { design = id; Mark(); };
            borders.Add((card, id));
            cards.Children.Add(card);
        }
        Mark();
        body.Children.Add(cards);

        var font = new ComboBox();
        foreach (var f in UiFonts.All)
            font.Items.Add(new ComboBoxItem { Content = f.Name, FontFamily = new FontFamily(f.Windows), Tag = f.Id, IsSelected = f.Id == current.FontChoice.Id });
        var size = new ComboBox();
        foreach (var (name, scale) in UiFonts.Sizes)
            size.Items.Add(new ComboBoxItem { Content = name, Tag = scale, IsSelected = Math.Abs(scale - current.Scale) < 0.01 });
        if (size.SelectedIndex < 0) size.SelectedIndex = 1;
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var fontField = Theme.Field(Lang.T("Schriftart"), font);
        var sizeField = Theme.Field(Lang.T("Schriftgröße"), size);
        Grid.SetColumn(sizeField, 1);
        row.Children.Add(fontField);
        row.Children.Add(sizeField);
        body.Children.Add(row);
        var sample = new TextBlock { Text = Lang.T("Gleisplan Bahnhof Neustadt · Weiche links 24611 · R 437,5 mm · 2,5 %"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 6, 0), Foreground = Theme.TextBrush };
        void Sample()
        {
            if (font.SelectedItem is ComboBoxItem { Tag: string fid }) sample.FontFamily = new FontFamily(UiFonts.Find(fid).Windows);
            if (size.SelectedItem is ComboBoxItem { Tag: double sc }) sample.FontSize = 14 * sc;
        }
        font.SelectionChanged += (_, _) => Sample();
        size.SelectionChanged += (_, _) => Sample();
        Sample();
        body.Children.Add(new Border { Background = Theme.WindowBrush, CornerRadius = new CornerRadius(6), Padding = new Thickness(10), Margin = new Thickness(0, 0, 6, 0), Child = sample });

        var ok = Theme.Button(Lang.T("Übernehmen"), "Palette", accent: true);
        ok.IsDefault = true;
        var cancel = Theme.Button(Lang.T("Abbrechen"));
        cancel.IsCancel = true;
        var w = Frame(owner, Lang.T("Darstellung"), new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, cancel, ok);
        w.Width = 700;
        w.Height = 720;
        AppearanceSettings? result = null;
        ok.Click += (_, _) =>
        {
            var chosen = new AppearanceSettings(design,
                font.SelectedItem is ComboBoxItem { Tag: string fid } ? fid : current.Font,
                size.SelectedItem is ComboBoxItem { Tag: double sc } ? sc : current.Scale);
            result = chosen == current ? null : chosen;
            w.Close();
        };
        w.ShowDialog();
        return result;
    }

    /// <summary>Kleine Vorschau eines Designs: Zeichenfläche, Platte, Gleisbogen und Akzentleiste.</summary>
    private static UIElement Preview(Design d)
    {
        static SolidColorBrush B(Rgb c) => new(Color.FromRgb(c.R, c.G, c.B));
        var canvas = new Canvas { Width = 196, Height = 92, Background = B(d.Canvas), Margin = new Thickness(0, 0, 0, 6), ClipToBounds = true };
        canvas.Children.Add(new Rectangle { Width = 196, Height = 10, Fill = B(d.Accent) });
        var board = new Rectangle { Width = 160, Height = 64, Fill = B(d.Board), Stroke = B(d.BoardStroke), StrokeThickness = 1 };
        Canvas.SetLeft(board, 18); Canvas.SetTop(board, 20);
        canvas.Children.Add(board);
        var geo = Geometry.Parse("M40,76 A44,44 0 0 1 128,52 L160,52");
        canvas.Children.Add(new System.Windows.Shapes.Path { Data = geo, Stroke = B(d.Bed), StrokeThickness = 10 });
        canvas.Children.Add(new System.Windows.Shapes.Path { Data = geo, Stroke = B(d.Sleeper), StrokeThickness = 8, StrokeDashArray = [0.25, 0.6] });
        canvas.Children.Add(new System.Windows.Shapes.Path { Data = geo, Stroke = B(d.Rail), StrokeThickness = 1.4 });
        return new Border { CornerRadius = new CornerRadius(4), BorderBrush = Theme.BorderBrush, BorderThickness = new Thickness(1), Child = canvas, ClipToBounds = true };
    }

    /// <summary>Info über das Programm mit Logo, Version und Ausbaustufe.</summary>
    public static void ShowAbout(Window owner, LicenseManager license)
    {
        var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        body.Children.Add(new Image { Source = Brand.Logo, Width = 220, Height = 220, Margin = new Thickness(0, 0, 0, 12) });
        var version = typeof(Dialogs).Assembly.GetName().Version;
        foreach (var (text, size, bold) in new[]
                 {
                     ($"{AppFolders.Name} {license.EditionName}", 16.0, true),
                     ($"Version {version?.Major}.{version?.Minor}.{version?.Build}", 12.0, false),
                     (Lang.T("Gleisplanung für Modellbahnanlagen – Gleise, Höhen, Gleiswendel, Landschaft, Elektrik und 3D."), 12.0, false),
                     (license.Summary, 11.0, false),
                     (Catalog.DataNotice, 10.0, false),
                 })
        {
            var t = Theme.Label(text, size, bold, bold ? Theme.TextBrush : Theme.TextSecondaryBrush);
            t.TextAlignment = TextAlignment.Center;
            t.Margin = new Thickness(0, 0, 0, 4);
            body.Children.Add(t);
        }
        var close = Theme.Button(Lang.T("Schließen"), accent: true);
        close.IsCancel = true;
        close.IsDefault = true;
        var w = Small(owner, Lang.T("Über das Programm"), body, 460, 520, close);
        close.Click += (_, _) => w.Close();
        w.ShowDialog();
    }
}
