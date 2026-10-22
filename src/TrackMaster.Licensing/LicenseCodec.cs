using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TrackMaster.Licensing;

public enum LicenseStatus { Valid, Missing, Malformed, InvalidSignature, Expired, WrongMachine, WrongProduct }

public sealed record LicenseCheckResult(LicenseStatus Status, LicenseData? License)
{
    public bool IsValid => Status == LicenseStatus.Valid;

    public string StatusText => Status switch
    {
        LicenseStatus.Valid => "gültig",
        LicenseStatus.Missing => "keine Lizenzdatei gefunden",
        LicenseStatus.Malformed => "Lizenzdatei ist beschädigt",
        LicenseStatus.InvalidSignature => "Signatur ungültig",
        LicenseStatus.Expired => "abgelaufen",
        LicenseStatus.WrongMachine => "für einen anderen Rechner ausgestellt",
        LicenseStatus.WrongProduct => "Lizenz für ein anderes Programm",
        _ => Status.ToString(),
    };
}

/// <summary>Signiert und prüft Lizenzdateien mit RSA-SHA256.</summary>
public static class LicenseCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>Erstellt eine Lizenzdatei. Nur im Lizenz-Werkzeug mit dem privaten Schlüssel verwenden.</summary>
    public static string Sign(LicenseData data, string privateKeyPem)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var signature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var file = new LicenseFile(Convert.ToBase64String(payload), Convert.ToBase64String(signature));
        return JsonSerializer.Serialize(file, JsonOptions);
    }

    public static LicenseCheckResult Verify(string licenseText, string publicKeyPem, DateTimeOffset now, string? machineId)
    {
        LicenseFile? file;
        byte[] payload, signature;
        try
        {
            file = JsonSerializer.Deserialize<LicenseFile>(licenseText, JsonOptions);
            if (file is null || string.IsNullOrEmpty(file.Payload) || string.IsNullOrEmpty(file.Signature))
                return new(LicenseStatus.Malformed, null);
            payload = Convert.FromBase64String(file.Payload);
            signature = Convert.FromBase64String(file.Signature);
        }
        catch (Exception e) when (e is JsonException or FormatException)
        {
            return new(LicenseStatus.Malformed, null);
        }

        using (var rsa = RSA.Create())
        {
            rsa.ImportFromPem(publicKeyPem);
            if (!rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                return new(LicenseStatus.InvalidSignature, null);
        }

        LicenseData? data;
        try
        {
            data = JsonSerializer.Deserialize<LicenseData>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return new(LicenseStatus.Malformed, null);
        }
        if (data is null)
            return new(LicenseStatus.Malformed, null);

        if (!string.Equals(data.Product, AppFolders.Name, StringComparison.Ordinal))
            return new(LicenseStatus.WrongProduct, data);
        if (data.ExpiresAt is { } expires && expires < now)
            return new(LicenseStatus.Expired, data);
        if (data.MachineId is { } bound && !string.Equals(bound, machineId, StringComparison.OrdinalIgnoreCase))
            return new(LicenseStatus.WrongMachine, data);

        return new(LicenseStatus.Valid, data);
    }

    /// <summary>Erzeugt ein neues Schlüsselpaar (privat, öffentlich) im PEM-Format.</summary>
    public static (string PrivateKeyPem, string PublicKeyPem) CreateKeyPair(int keySize = 3072)
    {
        using var rsa = RSA.Create(keySize);
        return (rsa.ExportPkcs8PrivateKeyPem(), rsa.ExportSubjectPublicKeyInfoPem());
    }
}

/// <summary>Kennung des Rechners für rechnergebundene Lizenzen.</summary>
public static class MachineFingerprint
{
    public static string Current
    {
        get
        {
            var raw = $"{Environment.MachineName}|{Environment.UserDomainName}|{Environment.ProcessorCount}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(hash, 0, 10);
        }
    }
}
