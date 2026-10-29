using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TrackMaster.Licensing.Management;

/// <summary>Ein geladener privater Schlüssel.</summary>
public sealed class LoadedKey
{
    private LoadedKey(string path, string privatePem, string publicPem)
    {
        Path = path;
        PrivateKeyPem = privatePem;
        PublicKeyPem = publicPem;
        MatchesEmbedded = KeyTools.SamePublicKey(publicPem, LicensePublicKey.Pem);
        Fingerprint = KeyTools.Fingerprint(publicPem);
    }

    public string Path { get; }
    public string PrivateKeyPem { get; }
    public string PublicKeyPem { get; }

    /// <summary>Wahr, wenn TrackPlan Lizenzen dieses Schlüssels mit dem eingebauten öffentlichen Schlüssel anerkennt.</summary>
    public bool MatchesEmbedded { get; }
    public string Fingerprint { get; }

    public static LoadedKey FromFile(string path)
    {
        if (!File.Exists(path))
            throw new InvalidDataException($"Die Schlüsseldatei {path} wurde nicht gefunden.");
        var pem = File.ReadAllText(path);
        if (!pem.Contains("PRIVATE KEY", StringComparison.Ordinal))
            throw new InvalidDataException("Die Datei enthält keinen privaten Schlüssel. Bitte trackmaster.private.pem wählen, nicht den öffentlichen Schlüssel.");
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            rsa.ExportParameters(includePrivateParameters: true);
            return new LoadedKey(path, pem, rsa.ExportSubjectPublicKeyInfoPem());
        }
        catch (Exception e) when (e is ArgumentException or CryptographicException)
        {
            throw new InvalidDataException($"Der Schlüssel kann nicht gelesen werden: {e.Message}", e);
        }
    }

    /// <summary>Sucht trackmaster.private.pem neben dem Programm, in Dokumente\TrackMaster Pro Lizenzen, Dokumente und Downloads.</summary>
    public static string? Locate(string? remembered)
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            remembered,
            System.IO.Path.Combine(AppContext.BaseDirectory, KeyTools.PrivateFileName),
            System.IO.Path.Combine(AppFolders.LicensesInDocuments, KeyTools.PrivateFileName),
            System.IO.Path.Combine(docs, KeyTools.PrivateFileName),
            System.IO.Path.Combine(home, "Downloads", KeyTools.PrivateFileName),
        };
        return candidates.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
    }
}

public static class KeyTools
{
    public const string PrivateFileName = "trackmaster.private.pem";
    public const string PublicFileName = "trackmaster.public.pem";

    public static bool SamePublicKey(string a, string b)
    {
        try { return Spki(a).AsSpan().SequenceEqual(Spki(b)); }
        catch (Exception e) when (e is ArgumentException or CryptographicException) { return false; }
    }

    /// <summary>Kurzer Fingerabdruck zum Vergleichen, z. B. „3F2A 91C0 …“.</summary>
    public static string Fingerprint(string publicPem)
    {
        var hex = Convert.ToHexString(SHA256.HashData(Spki(publicPem)), 0, 8);
        return string.Join(' ', Enumerable.Range(0, hex.Length / 4).Select(i => hex.Substring(i * 4, 4)));
    }

    /// <summary>Erzeugt ein neues Schlüsselpaar im Ordner. Vorhandene Dateien werden nie überschrieben.</summary>
    public static (string PrivatePath, string PublicPath) CreateKeyPair(string folder, int keySize = 3072)
    {
        Directory.CreateDirectory(folder);
        var privatePath = Path.Combine(folder, PrivateFileName);
        var publicPath = Path.Combine(folder, PublicFileName);
        if (File.Exists(privatePath) || File.Exists(publicPath))
            throw new IOException($"Im Ordner {folder} liegt bereits ein Schlüssel. Bitte einen leeren Ordner wählen.");
        var (privatePem, publicPem) = LicenseCodec.CreateKeyPair(keySize);
        File.WriteAllText(privatePath, privatePem);
        File.WriteAllText(publicPath, publicPem);
        return (privatePath, publicPath);
    }

    private static byte[] Spki(string publicPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicPem);
        return rsa.ExportSubjectPublicKeyInfo();
    }
}

/// <summary>Eingaben für eine neue Lizenz.</summary>
public sealed record LicenseRequest
{
    public string Licensee { get; init; } = "";
    public string? Email { get; init; }
    /// <summary>Letzter gültiger Tag; <c>null</c> = unbefristet.</summary>
    public DateTime? ExpiresOn { get; init; }
    /// <summary>Rechnerkennung aus TrackPlan › Datei › Lizenz; leer = an keinen Rechner gebunden.</summary>
    public string? MachineId { get; init; }
}

/// <summary>Prüft Eingaben und erzeugt daraus eine signierte Pro-Lizenz.</summary>
public static partial class LicenseIssuer
{
    public static IReadOnlyList<string> Validate(LicenseRequest request, DateTimeOffset now)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Licensee))
            errors.Add("Bitte den Lizenznehmer angeben.");
        if (!string.IsNullOrWhiteSpace(request.Email) && !EmailPattern().IsMatch(request.Email.Trim()))
            errors.Add("Die E-Mail-Adresse sieht nicht gültig aus.");
        if (request.ExpiresOn is { } date && date.Date < now.LocalDateTime.Date)
            errors.Add("Das Ablaufdatum liegt in der Vergangenheit.");
        if (!string.IsNullOrWhiteSpace(request.MachineId) && !MachinePattern().IsMatch(NormalizeMachineId(request.MachineId)!))
            errors.Add("Die Rechnerkennung darf nur Buchstaben, Ziffern und Bindestriche enthalten (mindestens 8 Zeichen).");
        return errors;
    }

    public static string? NormalizeMachineId(string? machineId)
        => string.IsNullOrWhiteSpace(machineId) ? null : new string(machineId.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

    public static LicenseData CreateData(LicenseRequest request, DateTimeOffset now) => new()
    {
        LicenseId = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
        Licensee = request.Licensee.Trim(),
        Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
        Features = [Features.Basis, Features.Pro],
        IssuedAt = now,
        // gültig bis zum Ende des angegebenen Tages
        ExpiresAt = request.ExpiresOn is { } d ? new DateTimeOffset(d.Date.AddDays(1).AddSeconds(-1)) : null,
        MachineId = NormalizeMachineId(request.MachineId),
    };

    /// <summary>Prüft, erstellt und signiert. Wirft <see cref="ArgumentException"/> bei ungültigen Eingaben.</summary>
    public static (LicenseData Data, string SignedText) Issue(LicenseRequest request, string privateKeyPem, DateTimeOffset now)
    {
        var errors = Validate(request, now);
        if (errors.Count > 0) throw new ArgumentException(string.Join(Environment.NewLine, errors));
        var data = CreateData(request, now);
        return (data, LicenseCodec.Sign(data, privateKeyPem));
    }

    /// <summary>Dateiname für einen Kunden (ohne Zeichen, die Windows nicht erlaubt).</summary>
    public static string FileNameFor(string licensee)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']).ToHashSet();
        var name = new string(licensee.Trim().Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim('.', ' ');
        return (name.Length == 0 ? "Kunde" : name) + ".lic";
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex("^[A-Z0-9-]{8,128}$")]
    private static partial Regex MachinePattern();
}

/// <summary>Eine ausgestellte Lizenz samt signiertem Text, damit sie jederzeit erneut gespeichert werden kann.</summary>
public sealed record IssuedLicense
{
    public required string Id { get; init; }
    public required string Licensee { get; init; }
    public string? Email { get; init; }
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? MachineId { get; init; }
    public string? FilePath { get; init; }
    public required string SignedText { get; init; }

    public static IssuedLicense From(LicenseData data, string signedText, string? filePath) => new()
    {
        Id = data.LicenseId, Licensee = data.Licensee, Email = data.Email, IssuedAt = data.IssuedAt,
        ExpiresAt = data.ExpiresAt, MachineId = data.MachineId, FilePath = filePath, SignedText = signedText,
    };
}

/// <summary>Verzeichnis der ausgestellten Lizenzen (<c>lizenzen.json</c>) und gemerkte Einstellungen der Lizenzverwaltung.</summary>
public sealed class LicenseRegister
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;
    private readonly List<IssuedLicense> _entries = [];

    private LicenseRegister(string path) => _path = path;

    public static string DefaultFolder => Path.Combine(AppFolders.Data, "Lizenzverwaltung");

    /// <summary>Neueste zuerst.</summary>
    public IReadOnlyList<IssuedLicense> Entries => _entries;
    public string? LoadWarning { get; private set; }

    public static LicenseRegister Load(string path)
    {
        var register = new LicenseRegister(path);
        if (!File.Exists(path)) return register;
        try
        {
            if (JsonSerializer.Deserialize<List<IssuedLicense>>(File.ReadAllText(path), Options) is { } stored)
                register._entries.AddRange(stored.OrderByDescending(e => e.IssuedAt));
        }
        catch (JsonException)
        {
            var backup = path + ".beschaedigt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Move(path, backup);
            register.LoadWarning = $"Das Verzeichnis der ausgestellten Lizenzen war beschädigt. Die alte Datei liegt unter {backup}.";
        }
        return register;
    }

    public void Add(IssuedLicense entry)
    {
        _entries.RemoveAll(e => e.Id == entry.Id);
        _entries.Insert(0, entry);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_entries, Options));
    }

    public IReadOnlyList<IssuedLicense> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return _entries;
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return _entries.Where(e =>
        {
            var text = string.Join(' ', new[] { e.Licensee, e.Email, e.Id, e.MachineId }.Where(s => s is not null));
            return words.All(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
        }).ToList();
    }
}
