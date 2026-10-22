using TrackMaster.Licensing;
using TrackMaster.Licensing.Management;

namespace TrackMaster.Tests;

public class LicenseTests
{
    private static readonly (string Private, string Public) Keys = LicenseCodec.CreateKeyPair(2048);
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static LicenseData Data(string product = "TrackMaster Pro", DateTimeOffset? expires = null, string? machine = null) => new()
    {
        LicenseId = "TMP-TEST",
        Licensee = "Testkunde",
        Product = product,
        Features = [Features.Pro],
        IssuedAt = Now,
        ExpiresAt = expires,
        MachineId = machine,
    };

    [Fact]
    public void Signierte_Lizenz_ist_gueltig()
    {
        var text = LicenseCodec.Sign(Data(), Keys.Private);
        var result = LicenseCodec.Verify(text, Keys.Public, Now, null);
        Assert.Equal(LicenseStatus.Valid, result.Status);
        Assert.Equal("Testkunde", result.License!.Licensee);
    }

    [Fact]
    public void Veraenderte_Lizenz_wird_abgelehnt()
    {
        var text = LicenseCodec.Sign(Data(), Keys.Private);
        var other = LicenseCodec.Sign(Data() with { Licensee = "Jemand anders" }, Keys.Private);
        // Nutzdaten der einen Lizenz mit der Signatur der anderen
        var a = System.Text.Json.JsonDocument.Parse(text).RootElement;
        var b = System.Text.Json.JsonDocument.Parse(other).RootElement;
        var forged = $$"""{"payload":"{{b.GetProperty("payload").GetString()}}","signature":"{{a.GetProperty("signature").GetString()}}"}""";
        Assert.Equal(LicenseStatus.InvalidSignature, LicenseCodec.Verify(forged, Keys.Public, Now, null).Status);
        Assert.Equal(LicenseStatus.Malformed, LicenseCodec.Verify("kein JSON", Keys.Public, Now, null).Status);
        // Mit fremdem Schlüssel signiert
        var foreign = LicenseCodec.CreateKeyPair(2048);
        Assert.Equal(LicenseStatus.InvalidSignature, LicenseCodec.Verify(LicenseCodec.Sign(Data(), foreign.PrivateKeyPem), Keys.Public, Now, null).Status);
    }

    [Fact]
    public void Ablauf_Rechner_und_Produkt_werden_geprueft()
    {
        Assert.Equal(LicenseStatus.Expired, LicenseCodec.Verify(LicenseCodec.Sign(Data(expires: Now.AddDays(-1)), Keys.Private), Keys.Public, Now, null).Status);
        Assert.Equal(LicenseStatus.Valid, LicenseCodec.Verify(LicenseCodec.Sign(Data(expires: Now.AddDays(1)), Keys.Private), Keys.Public, Now, null).Status);
        Assert.Equal(LicenseStatus.WrongMachine, LicenseCodec.Verify(LicenseCodec.Sign(Data(machine: "AAAA"), Keys.Private), Keys.Public, Now, "BBBB").Status);
        Assert.Equal(LicenseStatus.Valid, LicenseCodec.Verify(LicenseCodec.Sign(Data(machine: "AAAA"), Keys.Private), Keys.Public, Now, "aaaa").Status);
        Assert.Equal(LicenseStatus.WrongProduct, LicenseCodec.Verify(LicenseCodec.Sign(Data(product: "Moba4N"), Keys.Private), Keys.Public, Now, null).Status);
    }

    [Fact]
    public void Ohne_Lizenz_laeuft_die_Basisversion()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(dir.FullName, "trackmaster.lic");
            var manager = new LicenseManager(path, Keys.Public, developerMode: false, () => Now);
            Assert.False(manager.IsPro);
            Assert.Equal("Basis", manager.EditionName);
            Assert.True(manager.IsFeatureEnabled(Features.Basis));
            Assert.False(manager.IsFeatureEnabled(Features.Pro));

            Assert.True(new LicenseManager(path, Keys.Public, developerMode: true, () => Now).IsPro);

            Assert.Equal(LicenseStatus.Valid, manager.Install(LicenseCodec.Sign(Data(), Keys.Private)).Status);
            Assert.True(manager.IsPro);
            Assert.True(File.Exists(path));
            Assert.True(new LicenseManager(path, Keys.Public, developerMode: false, () => Now).IsPro);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void Lizenzverwaltung_stellt_gueltige_Lizenzen_aus()
    {
        var request = new LicenseRequest { Licensee = "Erika Muster", Email = "erika@example.org" };
        Assert.Empty(LicenseIssuer.Validate(request, Now));
        var (data, text) = LicenseIssuer.Issue(request, Keys.Private, Now);
        Assert.Equal("TrackMaster Pro", data.Product);
        Assert.Contains(Features.Pro, data.Features);
        Assert.Equal(LicenseStatus.Valid, LicenseCodec.Verify(text, Keys.Public, Now, null).Status);
        Assert.NotEmpty(LicenseIssuer.Validate(new LicenseRequest { Licensee = " " }, Now));
    }

    [Fact]
    public void Eingebauter_Schluessel_ist_lesbar()
    {
        using var rsa = System.Security.Cryptography.RSA.Create();
        rsa.ImportFromPem(LicensePublicKey.Pem);
        Assert.True(rsa.KeySize >= 2048);
    }
}
