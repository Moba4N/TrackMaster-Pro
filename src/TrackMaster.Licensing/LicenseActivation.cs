using System.Reflection;
using System.Text.Json;
using TrackMaster.Core;

namespace TrackMaster.Licensing;

/// <summary>Stand der Online-Aktivierung einer Lizenz auf diesem Gerät.</summary>
public enum ActivationState
{
    /// <summary>Keine Aktivierung nötig (kein Server eingestellt oder rechnergebundene Lizenz).</summary>
    NotRequired,
    /// <summary>Auf diesem Gerät aktiviert.</summary>
    Active,
    /// <summary>Noch nicht aktiviert (z. B. ohne Internet); Pro läuft bis zum Ende der Schonfrist.</summary>
    Grace,
    /// <summary>Der Server hat abgelehnt (alle Geräteplätze belegt oder Lizenz gesperrt), oder die Schonfrist ist abgelaufen.</summary>
    Refused,
}

/// <summary>Was auf dem Gerät über die Aktivierung gespeichert ist.</summary>
public sealed record ActivationRecord
{
    public string LicenseId { get; init; } = "";
    public string DeviceId { get; init; } = "";
    public bool Activated { get; init; }
    public DateTimeOffset FirstSeen { get; init; }
    public DateTimeOffset? LastCheck { get; init; }
    /// <summary>Begründung des Servers bei einer Ablehnung, sonst null.</summary>
    public string? Refusal { get; init; }
    /// <summary>Belegte und erlaubte Geräteplätze laut letzter Antwort des Servers (0 = unbekannt).</summary>
    public int Devices { get; init; }
    public int MaxDevices { get; init; }
}

/// <summary>Antwort des Aktivierungsservers (lizenz/aktivierung.php).</summary>
public sealed record ActivationReply
{
    /// <summary>active, released, limit, blocked, invalid oder error.</summary>
    public string Status { get; init; } = "";
    public int Devices { get; init; }
    public int Max { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Online-Aktivierung: Eine Lizenz (ohne Rechnerbindung) darf auf höchstens so vielen Geräten laufen, wie der Server erlaubt
/// (Standard 2). Beim ersten Start mit der Lizenz meldet sich das Gerät beim Server an; danach prüft es alle
/// <see cref="RecheckDays"/> Tage. Ohne Internet läuft Pro bis zu <see cref="GraceDays"/> Tage ab dem ersten Start weiter,
/// ein einmal aktiviertes Gerät dauerhaft. Lehnt der Server ab, bleibt es bei der Basisversion.
/// </summary>
public sealed class LicenseActivation
{
    public const int GraceDays = 14;
    public const int RecheckDays = 7;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    private readonly Func<string?> _load;
    private readonly Action<string?> _save;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<string, IReadOnlyDictionary<string, string>, Task<string>> _post;

    /// <param name="serverUrl">Adresse von aktivierung.php; leer = keine Aktivierung.</param>
    /// <param name="deviceId">Kennung dieses Geräts (Rechnerkennung, Android-Gerätekennung, Browserkennung).</param>
    /// <param name="platform">Windows, Android oder Web – erscheint in der Übersicht.</param>
    /// <param name="deviceName">Lesbarer Gerätename für die Übersicht.</param>
    /// <param name="load">Liest den gespeicherten Stand (JSON) oder null.</param>
    /// <param name="save">Speichert den Stand (null = löschen).</param>
    /// <param name="post">Schickt ein Formular an den Server und liefert die Antwort (zum Testen austauschbar).</param>
    public LicenseActivation(string? serverUrl, string deviceId, string platform, string deviceName,
        Func<string?> load, Action<string?> save, Func<DateTimeOffset>? clock = null,
        Func<string, IReadOnlyDictionary<string, string>, Task<string>>? post = null)
    {
        ServerUrl = string.IsNullOrWhiteSpace(serverUrl) ? null : serverUrl.Trim();
        DeviceId = deviceId;
        Platform = platform;
        DeviceName = deviceName;
        _load = load;
        _save = save;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _post = post ?? PostAsync;
    }

    /// <summary>Adresse des Aktivierungsservers aus den Programm-Metadaten (ActivationUrl in Directory.Build.props).</summary>
    public static string? DefaultServerUrl
        => typeof(LicenseActivation).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "ActivationUrl")?.Value is { Length: > 0 } url ? url : null;

    /// <summary>Speicher für den Aktivierungsstand in einer Datei (Windows, Android).</summary>
    public static (Func<string?> Load, Action<string?> Save) FileStore(string path) => (
        () =>
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        },
        json =>
        {
            try
            {
                if (json is null) { if (File.Exists(path)) File.Delete(path); return; }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, json);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        });

    public const string FileName = "aktivierung.json";

    /// <summary>Datenschutzerklärung mit dem Abschnitt zu TrackMaster Pro.</summary>
    public const string PrivacyUrl = "https://www.jm-jungwirth.de/datenschutz.php#trackmaster";

    /// <summary>Was bei der Online-Aktivierung übertragen und gespeichert wird (für den Lizenzdialog).</summary>
    public static string PrivacyNotice => Lang.T("Datenschutz: Bei der Online-Aktivierung und der wöchentlichen Prüfung schickt das Programm die Lizenzdatei (Lizenznummer, Inhaber, Gültigkeit), eine zufällige bzw. technische Gerätekennung, den Gerätenamen, die Plattform und die Programmversion an www.jm-jungwirth.de. Gespeichert werden dort Lizenznummer, Inhaber, Gerätekennung, Gerätename, Plattform, Version sowie Datum der ersten und letzten Meldung – nur um die erlaubte Geräteanzahl einzuhalten. Geräte ohne Meldung seit 12 Monaten werden automatisch gelöscht; „Dieses Gerät freigeben“ löscht den Eintrag sofort. Pläne werden nie übertragen.")
        + " " + PrivacyUrl;

    public string? ServerUrl { get; }
    public string DeviceId { get; }
    public string Platform { get; }
    public string DeviceName { get; }

    /// <summary>Muss diese Lizenz online aktiviert werden?</summary>
    public bool Applies(LicenseData? license) => ServerUrl is not null && license is { MachineId: null };

    public ActivationRecord? Record(LicenseData license)
    {
        try
        {
            if (_load() is not { Length: > 0 } json) return null;
            var r = JsonSerializer.Deserialize<ActivationRecord>(json, JsonOptions);
            return r is not null && r.LicenseId == license.LicenseId && string.Equals(r.DeviceId, DeviceId, StringComparison.OrdinalIgnoreCase) ? r : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Store(ActivationRecord r) => _save(JsonSerializer.Serialize(r, JsonOptions));

    /// <summary>Stand auf diesem Gerät, ohne den Server zu fragen. Legt beim ersten Mal den Beginn der Schonfrist an.</summary>
    public ActivationState State(LicenseData? license)
    {
        if (!Applies(license)) return ActivationState.NotRequired;
        var r = Record(license!);
        if (r is null)
        {
            Store(new ActivationRecord { LicenseId = license!.LicenseId, DeviceId = DeviceId, FirstSeen = _clock() });
            return ActivationState.Grace;
        }
        if (r.Activated) return ActivationState.Active;
        if (r.Refusal is not null) return ActivationState.Refused;
        return _clock() - r.FirstSeen < TimeSpan.FromDays(GraceDays) ? ActivationState.Grace : ActivationState.Refused;
    }

    /// <summary>Darf Pro laufen (Aktivierung nicht nötig, aktiviert oder in der Schonfrist)?</summary>
    public bool Allows(LicenseData? license) => State(license) != ActivationState.Refused;

    /// <summary>Sollte jetzt beim Server nachgefragt werden (noch nicht aktiviert oder letzte Prüfung zu lange her)?</summary>
    public bool DueForSync(LicenseData? license)
    {
        if (!Applies(license)) return false;
        var r = Record(license!);
        return r is null || !r.Activated || r.LastCheck is not { } last || _clock() - last > TimeSpan.FromDays(RecheckDays);
    }

    /// <summary>Kurzer Text für den Lizenzdialog.</summary>
    public string Describe(LicenseData? license)
    {
        var state = State(license);
        var r = license is null ? null : Record(license);
        var slots = r is { MaxDevices: > 0 } ? Lang.F($" ({r.Devices} von {r.MaxDevices} Geräten belegt)") : "";
        return state switch
        {
            ActivationState.NotRequired => license is { MachineId: not null } ? Lang.T("An diesen Rechner gebunden, keine Online-Aktivierung nötig.") : "",
            ActivationState.Active => Lang.T("Auf diesem Gerät aktiviert") + slots + ".",
            ActivationState.Grace => Lang.F($"Noch nicht aktiviert – Pro läuft bis {(r?.FirstSeen ?? _clock()).AddDays(GraceDays):dd.MM.yyyy}. Für die Aktivierung braucht das Gerät einmal Internet."),
            _ => r?.Refusal ?? Lang.T("Die Lizenz konnte auf diesem Gerät nicht aktiviert werden (keine Verbindung zum Aktivierungsserver innerhalb der Schonfrist)."),
        };
    }

    /// <summary>
    /// Meldet das Gerät beim Server an bzw. prüft, ob es noch angemeldet ist. Liefert den neuen Stand und eine Meldung;
    /// ohne Verbindung bleibt der bisherige Stand (Meldung „keine Verbindung“).
    /// </summary>
    public async Task<(ActivationState State, string? Message)> SyncAsync(LicenseData license, string licenseText, string appVersion = "")
    {
        if (!Applies(license)) return (ActivationState.NotRequired, null);
        var before = Record(license) ?? new ActivationRecord { LicenseId = license.LicenseId, DeviceId = DeviceId, FirstSeen = _clock() };
        ActivationReply? reply;
        try
        {
            var text = await _post(ServerUrl!, Form("activate", license, licenseText, appVersion)).ConfigureAwait(false);
            reply = JsonSerializer.Deserialize<ActivationReply>(text, JsonOptions);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or NotSupportedException)
        {
            if (Record(license) is null) Store(before);
            return (State(license), Lang.T("Keine Verbindung zum Aktivierungsserver – später erneut versuchen."));
        }
        if (reply is null) return (State(license), Lang.T("Unverständliche Antwort des Aktivierungsservers."));
        var now = _clock();
        switch (reply.Status)
        {
            case "active":
                Store(before with { Activated = true, LastCheck = now, Refusal = null, Devices = reply.Devices, MaxDevices = reply.Max });
                return (ActivationState.Active, Lang.F($"Lizenz auf diesem Gerät aktiviert ({reply.Devices} von {reply.Max} Geräten belegt)."));
            case "limit":
            {
                var text = reply.Message is { Length: > 0 } m ? m
                    : Lang.F($"Diese Lizenz ist schon auf {reply.Devices} von {reply.Max} Geräten aktiviert. Auf einem anderen Gerät unter Lizenz „Dieses Gerät freigeben“ wählen oder beim Hersteller melden.");
                Store(before with { Activated = false, LastCheck = now, Refusal = text, Devices = reply.Devices, MaxDevices = reply.Max });
                return (ActivationState.Refused, text);
            }
            case "blocked":
            case "invalid":
            {
                var text = reply.Message is { Length: > 0 } m ? m : Lang.T("Diese Lizenz wurde vom Hersteller gesperrt.");
                Store(before with { Activated = false, LastCheck = now, Refusal = text });
                return (ActivationState.Refused, text);
            }
            case "error":
                if (Record(license) is null) Store(before);
                return (State(license), reply.Message is { Length: > 0 } em ? em : Lang.T("Keine Verbindung zum Aktivierungsserver – später erneut versuchen."));
            default:
                return (State(license), Lang.T("Unverständliche Antwort des Aktivierungsservers."));
        }
    }

    /// <summary>Gibt den Geräteplatz dieses Geräts frei (z. B. vor einem Rechnerwechsel). Danach läuft hier die Basisversion.</summary>
    public async Task<(bool Ok, string Message)> ReleaseAsync(LicenseData license, string licenseText, string appVersion = "")
    {
        if (!Applies(license)) return (false, Lang.T("Für diese Lizenz gibt es keine Online-Aktivierung."));
        try
        {
            var text = await _post(ServerUrl!, Form("release", license, licenseText, appVersion)).ConfigureAwait(false);
            var reply = JsonSerializer.Deserialize<ActivationReply>(text, JsonOptions);
            if (reply?.Status != "released") return (false, reply?.Message ?? Lang.T("Unverständliche Antwort des Aktivierungsservers."));
            var r = Record(license) ?? new ActivationRecord { LicenseId = license.LicenseId, DeviceId = DeviceId, FirstSeen = _clock() };
            Store(r with { Activated = false, LastCheck = _clock(), Refusal = Lang.T("Dieses Gerät wurde freigegeben. „Jetzt aktivieren“ meldet es wieder an."), Devices = reply.Devices, MaxDevices = reply.Max });
            return (true, Lang.F($"Gerät freigegeben – jetzt {reply.Devices} von {reply.Max} Geräten belegt."));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or NotSupportedException)
        {
            return (false, Lang.T("Keine Verbindung zum Aktivierungsserver – später erneut versuchen."));
        }
    }

    private Dictionary<string, string> Form(string action, LicenseData license, string licenseText, string appVersion) => new()
    {
        ["action"] = action,
        ["license"] = licenseText,
        ["licenseId"] = license.LicenseId,
        ["device"] = DeviceId,
        ["platform"] = Platform,
        ["name"] = DeviceName,
        ["version"] = appVersion,
    };

    private static async Task<string> PostAsync(string url, IReadOnlyDictionary<string, string> form)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        using var content = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync(url, content).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }
}
