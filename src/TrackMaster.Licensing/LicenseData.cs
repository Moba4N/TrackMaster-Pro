namespace TrackMaster.Licensing;

/// <summary>Inhalt einer Lizenz. Wird als JSON signiert.</summary>
public sealed record LicenseData
{
    public required string LicenseId { get; init; }
    public required string Licensee { get; init; }
    public string? Email { get; init; }

    /// <summary>Produkt, für das die Lizenz gilt; schützt davor, Lizenzen anderer Programme einzuspielen.</summary>
    public string Product { get; init; } = AppFolders.Name;

    /// <summary>Freigeschaltete Funktionspakete, siehe <see cref="Features"/>.</summary>
    public required IReadOnlyList<string> Features { get; init; }

    public DateTimeOffset IssuedAt { get; init; }

    /// <summary>Ablaufdatum; <c>null</c> = unbefristet.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Optionale Rechnerbindung (siehe <see cref="MachineFingerprint"/>); <c>null</c> = an keinen Rechner gebunden.</summary>
    public string? MachineId { get; init; }
}

/// <summary>Die Lizenzdatei auf der Platte: Nutzdaten und Signatur, beides Base64.</summary>
public sealed record LicenseFile(string Payload, string Signature);
