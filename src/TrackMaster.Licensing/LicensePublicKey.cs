namespace TrackMaster.Licensing;

/// <summary>
/// Öffentlicher Schlüssel, mit dem TrackPlan Lizenzen prüft. Der private Schlüssel (trackmaster.private.pem)
/// bleibt beim Hersteller und gehört nie ins Repository. Neues Schlüsselpaar: Lizenzverwaltung › Neues Schlüsselpaar
/// oder <c>TrackMaster.LicenseTool keygen</c>, dann den öffentlichen Teil hier eintragen.
/// </summary>
public static class LicensePublicKey
{
    public const string Pem = """
-----BEGIN PUBLIC KEY-----
MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAvUl84j6JRwKW2aYBR3XV
4AILaImxYN2A6FsZ7FMlmrwgIznGF0sDmEI9nKRDFvOfoLa5qutv6kodZfbkE2X3
dWkA3MFQVfUJuMIcmVkd+yIaFateRLTPdyh8f74hBvoKfw9euKKrodrzFPveKlbw
tploy8cNQ/V2uDfDo4xr7P0D4nNOmAlZs8HB6oNm/9Obe1Vo5USUWnDkB/MQvTIM
lyqWcTaYOzMVob5zvSn16dAcrOZVGUBcj/YLkLRv/VIURc5wR2RgD4nKU27lFGmL
Vv03p87s9/MH/uG0qrSth0PcYA2rT4s3VWRDRhjBg+DEfjQZMNI6z1TdfD+ZD9o5
8XrCTmUJeHvRsTPsyc8x4nQtLTj6BFxq81npuZPkDKWwAx8l4SkeVL2qNfV0oT9B
qUQF2G6tZ5r3ApuAsjRHE94/ip43kFjADFUGucxnIqYUdkb/91iWps71OTZ/H5yp
lQ1+J/sREeZEgOgO0yBjt8RPiaMVy0Q7ycsLnGQCNl8BAgMBAAE=
-----END PUBLIC KEY-----
""";
}
