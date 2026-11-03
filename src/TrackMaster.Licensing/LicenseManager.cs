using TrackMaster.Core;

namespace TrackMaster.Licensing;

/// <summary>
/// Liest die Lizenzdatei <c>trackmaster.lic</c>. Ohne gültige Lizenz läuft die Basisversion,
/// jede gültige, signierte Lizenz ist eine Pro-Lizenz. Entwicklungs-Builds schalten ohne Lizenz alles frei.
/// </summary>
public sealed class LicenseManager
{
    private readonly string _publicKeyPem;
    private readonly Func<DateTimeOffset> _clock;

    public LicenseManager(string licensePath, string publicKeyPem, bool developerMode, Func<DateTimeOffset>? clock = null, string? machineId = null,
        LicenseActivation? activation = null)
    {
        Activation = activation;
        MachineId = machineId ?? MachineFingerprint.Current;
        LicensePath = licensePath;
        _publicKeyPem = publicKeyPem;
        _clock = clock ?? (() => DateTimeOffset.Now);
        Result = File.Exists(licensePath)
            ? LicenseCodec.Verify(File.ReadAllText(licensePath), publicKeyPem, _clock(), MachineId)
            : new LicenseCheckResult(LicenseStatus.Missing, null);
        IsDeveloperMode = developerMode && !Result.IsValid;
    }

    public static string DefaultLicensePath => Path.Combine(AppFolders.Data, AppFolders.LicenseFileName);

    /// <summary>Rechnerkennung für rechnergebundene Lizenzen (unter Android die Gerätekennung).</summary>
    public string MachineId { get; }

    public LicenseCheckResult Result { get; private set; }
    public bool IsDeveloperMode { get; private set; }
    public string LicensePath { get; }

    /// <summary>Online-Aktivierung (höchstens N Geräte je Lizenz); null = keine.</summary>
    public LicenseActivation? Activation { get; }

    /// <summary>Gültige Lizenz, die auf diesem Gerät auch laufen darf (aktiviert, Schonfrist oder keine Aktivierung nötig).</summary>
    public bool IsLicensed => Result.IsValid && (Activation?.Allows(Result.License) ?? true);

    /// <summary>Pro-Funktionen frei (gültige, aktivierte Lizenz oder Entwicklerversion).</summary>
    public bool IsPro => IsLicensed || IsDeveloperMode;

    /// <summary>Stand der Online-Aktivierung als Text (leer, wenn keine nötig).</summary>
    public string ActivationText => Result.IsValid && Activation is { } a ? a.Describe(Result.License) : "";

    /// <summary>Soll beim Start (im Hintergrund) beim Aktivierungsserver nachgefragt werden?</summary>
    public bool ActivationDue => Result.IsValid && Activation?.DueForSync(Result.License) == true;

    /// <summary>Meldet dieses Gerät beim Aktivierungsserver an bzw. prüft die Anmeldung. Liefert eine Meldung oder null.</summary>
    public async Task<string?> SyncActivationAsync(string appVersion = "")
    {
        if (!Result.IsValid || Activation is not { } a || !File.Exists(LicensePath)) return null;
        var (_, message) = await a.SyncAsync(Result.License!, await File.ReadAllTextAsync(LicensePath).ConfigureAwait(false), appVersion).ConfigureAwait(false);
        return message;
    }

    /// <summary>Gibt den Geräteplatz dieses Geräts beim Aktivierungsserver frei.</summary>
    public async Task<string> ReleaseActivationAsync(string appVersion = "")
    {
        if (!Result.IsValid || Activation is not { } a || !File.Exists(LicensePath)) return Lang.T("Für diese Lizenz gibt es keine Online-Aktivierung.");
        var (_, message) = await a.ReleaseAsync(Result.License!, await File.ReadAllTextAsync(LicensePath).ConfigureAwait(false), appVersion).ConfigureAwait(false);
        return message;
    }

    public bool IsFeatureEnabled(string feature) => feature == Features.Basis || IsPro;

    /// <summary>Prüft eine neue Lizenzdatei und übernimmt sie bei Erfolg.</summary>
    public LicenseCheckResult Install(string licenseText)
    {
        var result = LicenseCodec.Verify(licenseText, _publicKeyPem, _clock(), MachineId);
        if (result.IsValid)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LicensePath)!);
            File.WriteAllText(LicensePath, licenseText);
            Result = result;
            IsDeveloperMode = false;
        }
        return result;
    }

    public string EditionName => IsLicensed ? Lang.T("Pro") : IsDeveloperMode ? Lang.T("Pro (Entwicklerversion)") : Lang.T("Basis");

    public string Summary
    {
        get
        {
            if (Result.IsValid && !IsLicensed && !IsDeveloperMode) return Lang.T("Basisversion – Lizenz auf diesem Gerät nicht aktiviert");
            if (IsLicensed)
            {
                var l = Result.License!;
                return Lang.F($"Pro – lizenziert für {l.Licensee}{(l.ExpiresAt is { } e ? Lang.F($", gültig bis {e:dd.MM.yyyy}") : "")}");
            }
            if (IsDeveloperMode) return Lang.T("Entwicklerversion (alle Funktionen frei)");
            return Result.Status == LicenseStatus.Missing ? Lang.T("Basisversion (ohne Lizenz)") : Lang.F($"Basisversion – Lizenz {Result.StatusText}");
        }
    }
}
