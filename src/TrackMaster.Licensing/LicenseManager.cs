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

    public LicenseManager(string licensePath, string publicKeyPem, bool developerMode, Func<DateTimeOffset>? clock = null, string? machineId = null)
    {
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

    /// <summary>Pro-Funktionen frei (gültige Lizenz oder Entwicklerversion).</summary>
    public bool IsPro => Result.IsValid || IsDeveloperMode;

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

    public string EditionName => Result.IsValid ? Lang.T("Pro") : IsDeveloperMode ? Lang.T("Pro (Entwicklerversion)") : Lang.T("Basis");

    public string Summary
    {
        get
        {
            if (Result.IsValid)
            {
                var l = Result.License!;
                return Lang.F($"Pro – lizenziert für {l.Licensee}{(l.ExpiresAt is { } e ? Lang.F($", gültig bis {e:dd.MM.yyyy}") : "")}");
            }
            if (IsDeveloperMode) return Lang.T("Entwicklerversion (alle Funktionen frei)");
            return Result.Status == LicenseStatus.Missing ? Lang.T("Basisversion (ohne Lizenz)") : Lang.F($"Basisversion – Lizenz {Result.StatusText}");
        }
    }
}
