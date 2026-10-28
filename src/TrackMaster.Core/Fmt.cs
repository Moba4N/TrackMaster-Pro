using System.Globalization;

namespace TrackMaster.Core;

/// <summary>Zahlen in der Schreibweise der Oberfläche: deutsch 1.234,5, englisch 1,234.5.</summary>
public static class Fmt
{
    public static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    public static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-GB");

    public static string Num(double value, int decimals = 1)
        => value.ToString(decimals <= 0 ? "#,##0" : "#,##0." + new string('#', decimals), Lang.IsEnglish ? En : De);

    /// <summary>Liest „1.234,5“ ebenso wie „1234.5“. Gibt null zurück, wenn keine Zahl erkannt wird.</summary>
    public static double? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim().Replace(" ", "");
        if (Lang.IsEnglish)
            t = t.Contains('.') || System.Text.RegularExpressions.Regex.IsMatch(t, @"^-?\d{1,3}(,\d{3})+$") ? t.Replace(",", "") : t.Replace(',', '.');
        else if (t.Contains(','))
            t = t.Replace(".", "").Replace(',', '.');
        else if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^-?\d{1,3}(\.\d{3})+$"))
            t = t.Replace(".", ""); // „3.000“ wie angezeigt: Tausenderpunkt
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
    }
}
