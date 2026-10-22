using System.IO;
using System.Windows;
using System.Windows.Controls.Ribbon;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TrackMaster.App;

/// <summary>
/// Startpunkt. Aufruf: TrackMasterPro.exe [Plan.trackmaster.json]
/// Für den Build: --screenshot Datei.png [--tab Gleise] [--select] erzeugt ein Bildschirmfoto und beendet sich.
/// </summary>
public static class Program
{
    public static bool ScreenshotMode { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        var screenshot = ArgumentAfter(args, "--screenshot");
        ScreenshotMode = screenshot is not null;
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        if (screenshot is not null)
        {
            app.DispatcherUnhandledException += (_, e) => { WriteError(screenshot, e.Exception); e.Handled = true; app.Shutdown(1); };
            AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteError(screenshot, e.ExceptionObject as Exception);
        }

        MainWindow window;
        try
        {
            window = new MainWindow();
        }
        catch (Exception ex) when (screenshot is not null)
        {
            WriteError(screenshot, ex);
            return 1;
        }

        var file = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal) && a != screenshot && a != ArgumentAfter(args, "--tab") && File.Exists(a));
        if (file is not null) window.Loaded += (_, _) => window.OpenPath(file);

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
