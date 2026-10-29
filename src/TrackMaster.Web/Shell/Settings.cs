using TrackMaster.Core;
using TrackMaster.Web.Interop;

namespace TrackMaster.Web;

/// <summary>Einstellungen und Sicherungen im Speicher des Browsers (localStorage).</summary>
public static class Settings
{
    public const string LanguageKey = "trackmaster.sprache";
    public const string LicenseKey = "trackmaster.lic";
    public const string DeviceKey = "trackmaster.geraet";
    public const string AutosaveKey = "trackmaster.sicherung";
    public const string AppearanceKey = "trackmaster.darstellung";
    public const string ViewKey = "trackmaster.ansicht";

    public static AppearanceSettings LoadAppearance()
    {
        var raw = Js.StorageGet(AppearanceKey);
        if (string.IsNullOrEmpty(raw)) return new AppearanceSettings();
        var parts = raw.Split('|');
        var scale = parts.Length > 2 && double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 1.0;
        return new AppearanceSettings(parts[0], parts.Length > 1 ? parts[1] : "standard", scale);
    }

    public static void SaveAppearance(AppearanceSettings a)
        => Js.StorageSet(AppearanceKey, $"{a.Design}|{a.Font}|{a.Scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

    /// <summary>Schriftart als CSS-Schriftfamilie (mit Ersatzschriften für andere Systeme).</summary>
    public static string CssFont(FontChoice f) => f.Id switch
    {
        "bahnschrift" => "Bahnschrift,'Roboto Condensed','Arial Narrow',sans-serif",
        "calibri" => "Calibri,Carlito,'Segoe UI',sans-serif",
        "arial" => "Arial,Helvetica,'Liberation Sans',sans-serif",
        "verdana" => "Verdana,Geneva,'DejaVu Sans',sans-serif",
        "georgia" => "Georgia,'Times New Roman',serif",
        "consolas" => "Consolas,'Courier New',monospace",
        _ => "'Segoe UI Variable Text','Segoe UI',Roboto,system-ui,-apple-system,'Helvetica Neue',sans-serif",
    };

    /// <summary>Zufällige Kennung dieses Browsers; daraus entsteht die Gerätekennung für gebundene Lizenzen.</summary>
    public static string DeviceSeed()
    {
        var seed = Js.StorageGet(DeviceKey);
        if (string.IsNullOrEmpty(seed))
        {
            seed = Guid.NewGuid().ToString("N");
            Js.StorageSet(DeviceKey, seed);
        }
        return seed;
    }
}
