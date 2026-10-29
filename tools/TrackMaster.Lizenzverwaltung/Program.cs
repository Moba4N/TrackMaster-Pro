using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using TrackMaster.Licensing;
using TrackMaster.Licensing.Management;

namespace TrackMaster.Lizenzverwaltung;

/// <summary>
/// Lizenzverwaltung für den Hersteller: Kunde eintragen, Pro-Lizenz speichern, ausgestellte Lizenzen wiederfinden.
/// Aufruf mit --screenshot datei.png erzeugt ein Bildschirmfoto für den Build.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var window = new MainWindow();
        var i = Array.IndexOf(args, "--screenshot");
        if (i >= 0 && i + 1 < args.Length)
        {
            var path = args[i + 1];
            window.ContentRendered += (_, _) => window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                try
                {
                    var content = (FrameworkElement)window.Content;
                    var dpi = VisualTreeHelper.GetDpi(window);
                    var bmp = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                    bmp.Render(content);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bmp));
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                    using var s = File.Create(path);
                    enc.Save(s);
                }
                catch (Exception ex)
                {
                    File.WriteAllText(path + ".error.txt", ex.ToString());
                }
                app.Shutdown();
            });
        }
        return app.Run(window);
    }
}

internal sealed class Settings
{
    public string? KeyPath { get; set; }
    public string? LastFolder { get; set; }

    private static string FilePath => Path.Combine(LicenseRegister.DefaultFolder, "einstellungen.json");

    public static Settings Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new() : new(); }
        catch (JsonException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(LicenseRegister.DefaultFolder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }
}

public sealed class MainWindow : Window
{
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xC0));
    private static readonly Brush Secondary = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
    private static readonly Brush Border = new SolidColorBrush(Color.FromRgb(0xE1, 0xE1, 0xE1));

    private readonly Settings _settings = Settings.Load();
    private readonly LicenseRegister _register = LicenseRegister.Load(Path.Combine(LicenseRegister.DefaultFolder, "lizenzen.json"));
    private LoadedKey? _key;

    private readonly TextBlock _keyText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _name = new();
    private readonly TextBox _email = new();
    private readonly DatePicker _expires = new() { SelectedDateFormat = DatePickerFormat.Short };
    private readonly TextBox _machine = new();
    private readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBox _search = new();
    private readonly ListBox _list = new() { MinHeight = 200 };

    public MainWindow()
    {
        Title = "TrackPlan – Lizenzverwaltung";
        Width = 980; Height = 720; MinWidth = 820; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
        Language = System.Windows.Markup.XmlLanguage.GetLanguage("de-DE");
        // Platzhalter der Datumsauswahl ist sonst je nach Windows-Sprache englisch
        _expires.Loaded += (_, _) =>
        {
            if (_expires.Template.FindName("PART_TextBox", _expires) is System.Windows.Controls.Primitives.DatePickerTextBox box)
            {
                box.ApplyTemplate();
                if (box.Template.FindName("PART_Watermark", box) is ContentControl watermark) watermark.Content = "Datum wählen";
            }
        };

        var root = new DockPanel { Margin = new Thickness(20) };
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        head.Children.Add(new TextBlock { Text = "Lizenzverwaltung", FontSize = 24, FontWeight = FontWeights.SemiBold });
        head.Children.Add(new TextBlock { Text = "Erstellt signierte Pro-Lizenzen für TrackPlan. Ohne Lizenz läuft das Programm als Basisversion.", Foreground = Secondary });
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var editions = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        editions.ColumnDefinitions.Add(new ColumnDefinition());
        editions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        editions.ColumnDefinitions.Add(new ColumnDefinition());
        editions.Children.Add(Card("Basis (ohne Lizenz)", EditionInfo.Basis));
        var pro = Card("Pro (mit Lizenz)", EditionInfo.Pro);
        Grid.SetColumn(pro, 2);
        editions.Children.Add(pro);
        DockPanel.SetDock(editions, Dock.Top);
        root.Children.Add(editions);

        var main = new Grid();
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        main.ColumnDefinitions.Add(new ColumnDefinition());

        var form = new StackPanel();
        form.Children.Add(Heading("Schlüssel"));
        form.Children.Add(_keyText);
        var keyButtons = new WrapPanel { Margin = new Thickness(0, 6, 0, 10) };
        keyButtons.Children.Add(Button("Schlüssel wählen…", ChooseKey));
        keyButtons.Children.Add(Button("Neues Schlüsselpaar…", NewKeyPair));
        form.Children.Add(keyButtons);
        form.Children.Add(Heading("Neue Pro-Lizenz"));
        form.Children.Add(Field("Lizenznehmer (Kunde)", _name));
        form.Children.Add(Field("E-Mail (optional)", _email));
        form.Children.Add(Field("Gültig bis (leer = unbefristet)", _expires));
        form.Children.Add(Field("Rechnerkennung (optional, aus TrackPlan › Datei › Lizenz)", _machine));
        var save = Button("Pro-Lizenz speichern…", SaveLicense, accent: true);
        save.HorizontalAlignment = HorizontalAlignment.Left;
        form.Children.Add(save);
        form.Children.Add(_result);
        main.Children.Add(Panel(form));

        var issued = new DockPanel();
        var issuedHead = Heading("Ausgestellte Lizenzen");
        DockPanel.SetDock(issuedHead, Dock.Top);
        issued.Children.Add(issuedHead);
        var searchField = Field("Suchen", _search);
        DockPanel.SetDock(searchField, Dock.Top);
        issued.Children.Add(searchField);
        var again = Button("Erneut speichern…", SaveAgain);
        again.HorizontalAlignment = HorizontalAlignment.Left;
        again.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(again, Dock.Bottom);
        issued.Children.Add(again);
        issued.Children.Add(_list);
        var right = Panel(issued);
        Grid.SetColumn(right, 2);
        main.Children.Add(right);
        root.Children.Add(main);
        // Hintergrund am Inhalt, damit auch das Bildschirmfoto ihn hat
        Content = new Border { Background = Background, Child = root };

        _search.TextChanged += (_, _) => FillList();
        LoadKey(LoadedKey.Locate(_settings.KeyPath));
        FillList();
        if (_register.LoadWarning is { } warning) _result.Text = warning;
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };

    private static Border Panel(UIElement child) => new()
    {
        Background = Brushes.White, BorderBrush = Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Child = child,
    };

    private static Border Card(string title, IEnumerable<string> lines)
    {
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        foreach (var l in lines) s.Children.Add(new TextBlock { Text = "• " + l, TextWrapping = TextWrapping.Wrap, Foreground = Secondary });
        return Panel(s);
    }

    private static StackPanel Field(string label, Control box)
    {
        box.MinHeight = 28;
        box.VerticalContentAlignment = VerticalAlignment.Center;
        var p = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        p.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Secondary, Margin = new Thickness(0, 0, 0, 3) });
        p.Children.Add(box);
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        return p;
    }

    private static Button Button(string text, Action click, bool accent = false)
    {
        var b = new Button
        {
            Content = text, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0),
            Background = accent ? Accent : Brushes.White, Foreground = accent ? Brushes.White : Brushes.Black, BorderBrush = accent ? Accent : Border,
        };
        b.Click += (_, _) => click();
        return b;
    }

    private void LoadKey(string? path)
    {
        if (path is null)
        {
            _key = null;
            _keyText.Text = $"Kein privater Schlüssel gefunden. Bitte {KeyTools.PrivateFileName} wählen (gesucht neben dem Programm, in Dokumente\\{AppFolders.Name} Lizenzen, Dokumente und Downloads).";
            _keyText.Foreground = Brushes.DarkRed;
            return;
        }
        try
        {
            _key = LoadedKey.FromFile(path);
            _settings.KeyPath = path;
            _settings.Save();
            _keyText.Text = $"{path}\nFingerabdruck {_key.Fingerprint} – " + (_key.MatchesEmbedded
                ? "passt zum Programm."
                : "passt NICHT zum öffentlichen Schlüssel im Programm. Lizenzen dieses Schlüssels erkennt TrackPlan erst nach einem neuen Build an.");
            _keyText.Foreground = _key.MatchesEmbedded ? Brushes.DarkGreen : Brushes.DarkOrange;
        }
        catch (InvalidDataException e)
        {
            _key = null;
            _keyText.Text = e.Message;
            _keyText.Foreground = Brushes.DarkRed;
        }
    }

    private void ChooseKey()
    {
        var dlg = new OpenFileDialog { Title = "Privaten Schlüssel wählen", Filter = "Privater Schlüssel (*.pem)|*.pem|Alle Dateien (*.*)|*.*", FileName = KeyTools.PrivateFileName };
        if (dlg.ShowDialog(this) == true) LoadKey(dlg.FileName);
    }

    private void NewKeyPair()
    {
        var answer = MessageBox.Show(this,
            "Ein neues Schlüsselpaar macht alle bisher ausgestellten Lizenzen ungültig, sobald der neue öffentliche Schlüssel ins Programm eingebaut ist.\n\nTrotzdem ein neues Schlüsselpaar erzeugen?",
            Title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        var folder = Path.Combine(AppFolders.LicensesInDocuments, "Schlüssel " + DateTime.Now.ToString("yyyy-MM-dd HHmm"));
        try
        {
            var (privatePath, publicPath) = KeyTools.CreateKeyPair(folder);
            LoadKey(privatePath);
            _result.Text = $"Neues Schlüsselpaar in {folder}. Den Inhalt von {Path.GetFileName(publicPath)} in src/TrackMaster.Licensing/LicensePublicKey.cs eintragen.";
        }
        catch (IOException e)
        {
            _result.Text = e.Message;
        }
    }

    private void SaveLicense()
    {
        if (_key is null) { _result.Text = "Bitte zuerst den privaten Schlüssel wählen."; _result.Foreground = Brushes.DarkRed; return; }
        var request = new LicenseRequest { Licensee = _name.Text, Email = _email.Text, ExpiresOn = _expires.SelectedDate, MachineId = _machine.Text };
        var errors = LicenseIssuer.Validate(request, DateTimeOffset.Now);
        if (errors.Count > 0) { _result.Text = string.Join("\n", errors); _result.Foreground = Brushes.DarkRed; return; }
        var folder = _settings.LastFolder is { } f && Directory.Exists(f) ? f : AppFolders.LicensesInDocuments;
        Directory.CreateDirectory(folder);
        var dlg = new SaveFileDialog { Title = "Pro-Lizenz speichern", Filter = "Lizenzdatei (*.lic)|*.lic", InitialDirectory = folder, FileName = LicenseIssuer.FileNameFor(request.Licensee) };
        if (dlg.ShowDialog(this) != true) return;
        var (data, text) = LicenseIssuer.Issue(request, _key.PrivateKeyPem, DateTimeOffset.Now);
        File.WriteAllText(dlg.FileName, text);
        _register.Add(IssuedLicense.From(data, text, dlg.FileName));
        _settings.LastFolder = Path.GetDirectoryName(dlg.FileName);
        _settings.Save();
        _result.Text = $"Pro-Lizenz {data.LicenseId} für {data.Licensee} gespeichert: {dlg.FileName}";
        _result.Foreground = Brushes.DarkGreen;
        FillList();
    }

    private void SaveAgain()
    {
        if (_list.SelectedItem is not ListBoxItem { Tag: IssuedLicense entry }) { _result.Text = "Bitte links in der Liste eine Lizenz wählen."; return; }
        var dlg = new SaveFileDialog { Title = "Lizenz erneut speichern", Filter = "Lizenzdatei (*.lic)|*.lic", FileName = LicenseIssuer.FileNameFor(entry.Licensee) };
        if (dlg.ShowDialog(this) != true) return;
        File.WriteAllText(dlg.FileName, entry.SignedText);
        _result.Text = $"Gespeichert: {dlg.FileName}";
        _result.Foreground = Brushes.DarkGreen;
    }

    private void FillList()
    {
        _list.Items.Clear();
        foreach (var e in _register.Search(_search.Text))
        {
            var s = new StackPanel { Margin = new Thickness(2, 4, 2, 4) };
            s.Children.Add(new TextBlock { Text = e.Licensee, FontWeight = FontWeights.SemiBold });
            s.Children.Add(new TextBlock
            {
                Text = $"{e.Id} · {e.IssuedAt:dd.MM.yyyy} · {(e.ExpiresAt is { } x ? $"bis {x:dd.MM.yyyy}" : "unbefristet")}{(e.MachineId is null ? "" : " · Rechner " + e.MachineId)}",
                Foreground = Secondary, FontSize = 12,
            });
            _list.Items.Add(new ListBoxItem { Content = s, Tag = e });
        }
        if (_list.Items.Count == 0) _list.Items.Add(new ListBoxItem { Content = new TextBlock { Text = "Noch keine Lizenzen ausgestellt.", Foreground = Secondary }, IsEnabled = false });
    }
}
