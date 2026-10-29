using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TrackMaster.Core;
using TrackMaster.Licensing;

namespace TrackMaster.Web.Shell;

/// <summary>
/// Basis/Pro wie in den Apps: Ohne gültige Lizenz läuft die Basisversion, eine signierte Lizenzdatei macht Pro daraus.
/// Die Datei liegt im Speicher des Browsers. RSA gibt es in .NET im Browser nicht; die Signatur (RSA-SHA256, PKCS#1 v1.5)
/// wird deshalb hier mit dem öffentlichen Schlüssel aus <see cref="LicensePublicKey"/> nachgerechnet.
/// </summary>
public sealed class WebLicense
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _publicKeyPem;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<string?> _load;
    private readonly Action<string> _save;

    public WebLicense(string machineId, Func<string?> load, Action<string> save, string? publicKeyPem = null, Func<DateTimeOffset>? clock = null)
    {
        MachineId = machineId;
        _load = load;
        _save = save;
        _publicKeyPem = publicKeyPem ?? LicensePublicKey.Pem;
        _clock = clock ?? (() => DateTimeOffset.Now);
        Result = _load() is { Length: > 0 } text ? Verify(text) : new LicenseCheckResult(LicenseStatus.Missing, null);
    }

    /// <summary>Gerätekennung für gerätegebundene Lizenzen (je Browser).</summary>
    public string MachineId { get; }

    public LicenseCheckResult Result { get; private set; }

    public bool IsPro => Result.IsValid;

    public string EditionName => Result.IsValid ? Lang.T("Pro") : Lang.T("Basis");

    public string Summary
    {
        get
        {
            if (Result.IsValid)
            {
                var l = Result.License!;
                return Lang.F($"Pro – lizenziert für {l.Licensee}{(l.ExpiresAt is { } e ? Lang.F($", gültig bis {e:dd.MM.yyyy}") : "")}");
            }
            return Result.Status == LicenseStatus.Missing ? Lang.T("Basisversion (ohne Lizenz)") : Lang.F($"Basisversion – Lizenz {Result.StatusText}");
        }
    }

    /// <summary>Prüft eine neue Lizenzdatei und übernimmt sie bei Erfolg.</summary>
    public LicenseCheckResult Install(string licenseText)
    {
        var result = Verify(licenseText);
        if (result.IsValid)
        {
            _save(licenseText);
            Result = result;
        }
        return result;
    }

    /// <summary>Wie LicenseCodec.Verify, aber ohne die im Browser fehlende RSA-Klasse.</summary>
    public LicenseCheckResult Verify(string licenseText)
    {
        byte[] payload, signature;
        try
        {
            var file = JsonSerializer.Deserialize<LicenseFile>(licenseText, JsonOptions);
            if (file is null || string.IsNullOrEmpty(file.Payload) || string.IsNullOrEmpty(file.Signature))
                return new(LicenseStatus.Malformed, null);
            payload = Convert.FromBase64String(file.Payload);
            signature = Convert.FromBase64String(file.Signature);
        }
        catch (Exception e) when (e is JsonException or FormatException or NotSupportedException)
        {
            return new(LicenseStatus.Malformed, null);
        }

        if (!RsaSha256.Verify(_publicKeyPem, payload, signature))
            return new(LicenseStatus.InvalidSignature, null);

        LicenseData? data;
        try { data = JsonSerializer.Deserialize<LicenseData>(payload, JsonOptions); }
        catch (JsonException) { return new(LicenseStatus.Malformed, null); }
        if (data is null) return new(LicenseStatus.Malformed, null);

        if (!string.Equals(data.Product, AppFolders.Name, StringComparison.Ordinal))
            return new(LicenseStatus.WrongProduct, data);
        if (data.ExpiresAt is { } expires && expires < _clock())
            return new(LicenseStatus.Expired, data);
        if (data.MachineId is { } bound && !string.Equals(bound, MachineId, StringComparison.OrdinalIgnoreCase))
            return new(LicenseStatus.WrongMachine, data);
        return new(LicenseStatus.Valid, data);
    }

    /// <summary>Gerätekennung im Format der Windows-Rechnerkennung aus einer zufälligen, im Browser gespeicherten Kennung.</summary>
    public static string DeviceId(string raw)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("web|" + raw));
        return Convert.ToHexString(hash, 0, 10);
    }
}

/// <summary>Prüfung einer RSA-Signatur (PKCS#1 v1.5 mit SHA-256) in reinem C#.</summary>
public static class RsaSha256
{
    // DigestInfo für SHA-256 nach RFC 8017
    private static readonly byte[] DigestInfo = [0x30, 0x31, 0x30, 0x0d, 0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65, 0x03, 0x04, 0x02, 0x01, 0x05, 0x00, 0x04, 0x20];

    public static bool Verify(string publicKeyPem, byte[] data, byte[] signature)
    {
        BigInteger n, e;
        try { (n, e) = ReadPublicKey(publicKeyPem); }
        catch (Exception ex) when (ex is AsnContentException or FormatException or CryptographicException) { return false; }
        var k = (int)((n.GetBitLength() + 7) / 8);
        if (signature.Length != k) return false;
        var s = new BigInteger(signature, isUnsigned: true, isBigEndian: true);
        if (s >= n) return false;
        var m = BigInteger.ModPow(s, e, n).ToByteArray(isUnsigned: true, isBigEndian: true);
        var em = new byte[k];
        if (m.Length > k) return false;
        m.CopyTo(em, k - m.Length);

        var hash = SHA256.HashData(data);
        var t = DigestInfo.Concat(hash).ToArray();
        if (k < t.Length + 11) return false;
        var expected = new byte[k];
        expected[1] = 0x01;
        for (var i = 2; i < k - t.Length - 1; i++) expected[i] = 0xFF;
        t.CopyTo(expected, k - t.Length);
        return CryptographicOperations.FixedTimeEquals(em, expected);
    }

    /// <summary>Liest Modulus und Exponent aus einem öffentlichen Schlüssel (SubjectPublicKeyInfo oder PKCS#1) im PEM-Format.</summary>
    public static (BigInteger N, BigInteger E) ReadPublicKey(string pem)
    {
        var pkcs1 = pem.Contains("BEGIN RSA PUBLIC KEY");
        var body = string.Concat(pem.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("-----")));
        var der = Convert.FromBase64String(body);
        if (!pkcs1)
        {
            var spki = new AsnReader(der, AsnEncodingRules.DER).ReadSequence();
            spki.ReadSequence(); // Algorithmus (rsaEncryption)
            der = spki.ReadBitString(out _);
        }
        var key = new AsnReader(der, AsnEncodingRules.DER).ReadSequence();
        var n = key.ReadInteger();
        var e = key.ReadInteger();
        return (n, e);
    }
}
