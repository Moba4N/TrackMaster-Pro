using System.Globalization;

namespace TrackMaster.Core;

/// <summary>Zahlen in deutscher Schreibweise (1.234,5).</summary>
public static class Fmt
{
    public static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static string Num(double value, int decimals = 1)
        => value.ToString(decimals <= 0 ? "#,##0" : "#,##0." + new string('#', decimals), De);

    /// <summary>Liest „1.234,5“ ebenso wie „1234.5“. Gibt null zurück, wenn keine Zahl erkannt wird.</summary>
    public static double? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim().Replace(" ", "");
        if (t.Contains(','))
            t = t.Replace(".", "").Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
    }
}
