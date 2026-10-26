using System.IO;
using System.Windows;
using System.Windows.Controls.Ribbon;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TrackMaster.Licensing;

namespace TrackMaster.App;

/// <summary>
/// Startpunkt. Aufruf: TrackMasterPro.exe [Plan.trackmaster.json]
/// Für den Build: --screenshot Datei.png [--tab Gleise] [--select] [--pro] [--3d orbit|ride|evening]
/// erzeugt ein Bildschirmfoto und beendet sich. --pro schaltet nur im Bildschirmfoto-Modus alle Funktionen frei.
/// </summary>
public static class Program
{
    public static bool ScreenshotMode { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        var screenshot = ArgumentAfter(args, "--screenshot");
        ScreenshotMode = screenshot is not null;
        // Darstellung vor dem ersten Zugriff auf Theme festlegen
        Appearance.Load();
        if (screenshot is not null)
            Appearance.Use(new Core.AppearanceSettings(ArgumentAfter(args, "--design") ?? "trackplan", ArgumentAfter(args, "--font") ?? "standard"));
        else if (!args.Contains("--restore"))
            new SplashScreen(typeof(Program).Assembly, "Assets/Splash.png").Show(autoClose: true, topMost: false);
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        if (screenshot is not null)
        {
            app.DispatcherUnhandledException += (_, e) => { WriteError(screenshot, e.Exception); e.Handled = true; app.Shutdown(1); };
            AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteError(screenshot, e.ExceptionObject as Exception);
        }

        if (screenshot is not null && args.Contains("--neutral")) Core.Catalog.UseNeutralNames();

        MainWindow window;
        try
        {
#if DEBUG
            const bool developer = true;
#else
            const bool developer = false;
#endif
            var license = new LicenseManager(LicenseManager.DefaultLicensePath, LicensePublicKey.Pem,
                developerMode: developer || (screenshot is not null && args.Contains("--pro")));
            window = new MainWindow(license);
        }
        catch (Exception ex) when (screenshot is not null)
        {
            WriteError(screenshot, ex);
            return 1;
        }

        var file = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal) && a != screenshot && a != ArgumentAfter(args, "--tab") && a != ArgumentAfter(args, "--3d") && a != ArgumentAfter(args, "--dialog") && a != ArgumentAfter(args, "--restore") && a != ArgumentAfter(args, "--path") && File.Exists(a));
        if (file is not null) window.Loaded += (_, _) => window.OpenPath(file);
        if (ArgumentAfter(args, "--restore") is { } restore)
            window.Loaded += (_, _) => window.RestorePlan(restore, ArgumentAfter(args, "--path"), args.Contains("--dirty"));
        if (screenshot is not null && ArgumentAfter(args, "--rotate") is { } rotate && double.TryParse(rotate, System.Globalization.CultureInfo.InvariantCulture, out var degrees))
            window.ContentRendered += (_, _) => { window.Canvas.SetViewRotation(degrees); window.Canvas.Fit(); };

        if (screenshot is not null)
        {
            window.Width = 1440;
            window.Height = 900;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = 0;
            window.Top = 0;
            if (ArgumentAfter(args, "--tab") is { } tab)
                window.Loaded += (_, _) =>
                {
                    if (window.Ribbon.Items.OfType<RibbonTab>().FirstOrDefault(t => Equals(t.Header, tab)) is { } ribbonTab)
                        window.Ribbon.SelectedItem = ribbonTab;
                };
            if (args.Contains("--select"))
                window.Loaded += (_, _) =>
                {
                    // Eine Weiche wählen und ein Gleisende aktiv setzen, damit Eigenschaften und Hinweise zu sehen sind
                    var piece = window.Session.Plan.Pieces.FirstOrDefault(p => p.Def.Kind == Core.PieceKind.Switch);
                    if (piece is not null) window.Session.Select([piece.Id], add: false);
                };
            if (ArgumentAfter(args, "--3d") is { } mode3D)
            {
                var mode = mode3D switch { "ride" => View3DMode.Ride, "evening" => View3DMode.Evening, _ => View3DMode.Orbit };
                window.ContentRendered += (_, _) =>
                {
                    var view = new View3DWindow(window.Session.Plan, window.Session.Plan.Pieces.FirstOrDefault()?.Id, mode)
                    {
                        Owner = window, WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0,
                    };
                    view.ContentRendered += (_, _) =>
                    {
                        // Beim Mitfahren ein Stück fahren lassen, bevor das Bild entsteht
                        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(mode == View3DMode.Ride ? 2.5 : 0.5) };
                        timer.Tick += (_, _) =>
                        {
                            timer.Stop();
                            try
                            {
                                Capture(view, screenshot);
                                app.Shutdown(0);
                            }
                            catch (Exception ex)
                            {
                                WriteError(screenshot, ex);
                                app.Shutdown(1);
                            }
                        };
                        timer.Start();
                    };
                    view.Show();
                };
            }
            else if (ArgumentAfter(args, "--dialog") is { } dialog)
            {
                window.ContentRendered += (_, _) =>
                {
                    // Den Dialog öffnen (er läuft modal) und das erste weitere Fenster aufnehmen, sobald es gezeichnet ist
                    var ticks = 0;
                    Window? found = null;
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.4) };
                    timer.Tick += (_, _) =>
                    {
                        var open = app.Windows.OfType<Window>().FirstOrDefault(w => w != window && w.IsVisible);
                        if (open is null || open != found)
                        {
                            found = open;
                            if (++ticks > 50) { timer.Stop(); WriteError(screenshot, new TimeoutException($"Dialog „{dialog}“ wurde nicht geöffnet.")); app.Shutdown(1); }
                            return;
                        }
                        timer.Stop();
                        try
                        {
                            Capture(open, screenshot);
                            app.Shutdown(0);
                        }
                        catch (Exception ex)
                        {
                            WriteError(screenshot, ex);
                            app.Shutdown(1);
                        }
                    };
                    timer.Start();
                    window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => window.OpenForScreenshot(dialog));
                };
            }
            else
            {
                window.ContentRendered += (_, _) =>
                    window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
                    {
                        try
                        {
                            Capture(window, screenshot);
                            app.Shutdown(0);
                        }
                        catch (Exception ex)
                        {
                            WriteError(screenshot, ex);
                            app.Shutdown(1);
                        }
                    });
            }
        }
        return app.Run(window);
    }

    private static string? ArgumentAfter(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static void Capture(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var dpi = VisualTreeHelper.GetDpi(window);
        var width = (int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY);
        var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void WriteError(string screenshotPath, Exception? ex)
    {
        try { File.WriteAllText(screenshotPath + ".error.txt", ex?.ToString() ?? "Unbekannter Fehler"); }
        catch (IOException) { }
    }
}
