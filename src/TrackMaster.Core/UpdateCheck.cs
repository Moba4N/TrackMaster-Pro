using System.Reflection;
using System.Text.Json;

namespace TrackMaster.Core;

/// <summary>Angaben aus version.json: neueste Version, Download-Seite und kurze Beschreibung.</summary>
public sealed record UpdateInfo(Version Version, string? Url, string? Notes);

/// <summary>
/// Prüft beim Start, ob es eine neuere Version gibt. Die Adresse der version.json steht in Directory.Build.props
/// (UpdateUrl) und wird als Assembly-Metadaten mitgegeben; ist sie leer, findet keine Prüfung statt.
/// </summary>
public static class UpdateCheck
{
    public static string? ConfiguredUrl(Assembly assembly)
        => assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "UpdateUrl")?.Value is { Length: > 0 } url
           && Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? url : null;

    private static string OffFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrackMaster Pro", "keine-update-pruefung.txt");

    /// <summary>
    /// Beim Programmstart automatisch nach Updates suchen? Standard: ja. Die Prüfung ruft die version.json bei GitHub ab
    /// (dabei sieht GitHub wie bei jedem Abruf die IP-Adresse); wer das nicht möchte, schaltet es hier ab.
    /// </summary>
    public static bool AutoEnabled
    {
        get
        {
            try { return !File.Exists(OffFile); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; }
        }
        set
        {
            try
            {
                if (value) { if (File.Exists(OffFile)) File.Delete(OffFile); return; }
                Directory.CreateDirectory(Path.GetDirectoryName(OffFile)!);
                File.WriteAllText(OffFile, "aus");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Text für die Frage, ob beim Start automatisch geprüft werden soll.</summary>
    public static string AutoQuestion => Lang.T("Beim Programmstart automatisch nach Updates suchen? Dabei ruft das Programm eine kleine Versionsdatei bei GitHub (github.com, USA) ab; GitHub sieht dabei wie bei jedem Abruf die IP-Adresse und die Programmkennung „TrackMasterPro-UpdateCheck“. Es werden keine Pläne, Lizenzdaten oder Gerätekennungen übertragen.")
        + "\n\n" + (AutoEnabled ? Lang.T("Zurzeit: an.") : Lang.T("Zurzeit: aus."));

    public static UpdateInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("version", out var v) || !Version.TryParse(v.GetString(), out var version)) return null;
            string? Text(string name) => root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            var url = Text("url");
            if (url is not null && !(Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps)) url = null;
            return new UpdateInfo(version, url, Text("notes"));
        }
        catch (JsonException) { return null; }
    }

    public static bool IsNewer(UpdateInfo info, Version? current)
        => current is null || Normalize(info.Version) > Normalize(current);

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    /// <summary>Lädt version.json; bei Fehlern (offline, Zeitüberschreitung, kaputte Datei) null.</summary>
    public static async Task<UpdateInfo?> FetchAsync(string url, TimeSpan timeout)
    {
        try
        {
            using var http = new HttpClient { Timeout = timeout };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TrackMasterPro-UpdateCheck");
            return Parse(await http.GetStringAsync(url).ConfigureAwait(false));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException) { return null; }
    }

    public static string Describe(UpdateInfo info) => Lang.F($"Version {info.Version.ToString(3)} ist verfügbar.") + (info.Notes is { Length: > 0 } n ? " " + n : "");
}
