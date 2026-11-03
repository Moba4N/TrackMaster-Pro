using System.Text.Json;
using TrackMaster.Licensing;

namespace TrackMaster.Tests;

public class LicenseActivationTests
{
    private static readonly (string Private, string Public) Keys = LicenseCodec.CreateKeyPair(2048);
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static LicenseData Data(string? machine = null) => new()
    {
        LicenseId = "TMP-AKT",
        Licensee = "Testkunde",
        Product = "TrackMaster Pro",
        Features = [Features.Pro],
        IssuedAt = Start,
        MachineId = machine,
    };

    /// <summary>Gerät mit Speicher im Arbeitsspeicher, einstellbarer Uhr und vorgegebenen Serverantworten.</summary>
    private sealed class Device
    {
        public string? Stored;
        public DateTimeOffset Now = Start;
        public Func<IReadOnlyDictionary<string, string>, string> Server = _ => throw new HttpRequestException("offline");
        public readonly List<IReadOnlyDictionary<string, string>> Requests = [];
        public readonly LicenseActivation Activation;

        public Device(string? url = "https://example.invalid/lizenz/aktivierung.php", string id = "GERAET-A")
        {
            Activation = new LicenseActivation(url, id, "Windows", "Testrechner", () => Stored, s => Stored = s, () => Now,
                (_, form) => { Requests.Add(form); return Task.FromResult(Server(form)); });
        }
    }

    private static string Reply(string status, int devices = 1, int max = 2, string? message = null)
        => JsonSerializer.Serialize(new { status, devices, max, message });

    [Fact]
    public void Ohne_Server_oder_mit_Rechnerbindung_ist_keine_Aktivierung_noetig()
    {
        Assert.Equal(ActivationState.NotRequired, new Device(url: null).Activation.State(Data()));
        Assert.Equal(ActivationState.NotRequired, new Device().Activation.State(Data(machine: "AAAA")));
        Assert.False(new Device().Activation.DueForSync(Data(machine: "AAAA")));
    }

    [Fact]
    public async Task Ohne_Internet_laeuft_Pro_bis_zum_Ende_der_Schonfrist()
    {
        var d = new Device();
        Assert.Equal(ActivationState.Grace, d.Activation.State(Data()));
        var (state, message) = await d.Activation.SyncAsync(Data(), "lizenz");
        Assert.Equal(ActivationState.Grace, state);
        Assert.Contains("Keine Verbindung", message);
        d.Now = Start.AddDays(LicenseActivation.GraceDays - 1);
        Assert.True(d.Activation.Allows(Data()));
        d.Now = Start.AddDays(LicenseActivation.GraceDays + 1);
        Assert.False(d.Activation.Allows(Data()));
        Assert.True(d.Activation.DueForSync(Data()));

        // Später mit Internet: Aktivierung klappt und gilt dauerhaft
        d.Server = _ => Reply("active");
        Assert.Equal(ActivationState.Active, (await d.Activation.SyncAsync(Data(), "lizenz")).State);
        Assert.True(d.Activation.Allows(Data()));
    }

    [Fact]
    public async Task Aktiviertes_Geraet_bleibt_offline_aktiv_und_prueft_woechentlich()
    {
        var d = new Device { Server = _ => Reply("active", 1, 2) };
        var (state, message) = await d.Activation.SyncAsync(Data(), "lizenz", "1.5.0");
        Assert.Equal(ActivationState.Active, state);
        Assert.Contains("1 von 2", message);
        var form = Assert.Single(d.Requests);
        Assert.Equal("activate", form["action"]);
        Assert.Equal("TMP-AKT", form["licenseId"]);
        Assert.Equal("GERAET-A", form["device"]);
        Assert.Equal("1.5.0", form["version"]);
        Assert.False(d.Activation.DueForSync(Data()));

        d.Server = _ => throw new HttpRequestException("offline");
        d.Now = Start.AddDays(100);
        Assert.True(d.Activation.DueForSync(Data()));
        Assert.Equal(ActivationState.Active, (await d.Activation.SyncAsync(Data(), "lizenz")).State);
        Assert.Contains("1 von 2", d.Activation.Describe(Data()));
    }

    [Fact]
    public async Task Drittes_Geraet_wird_abgelehnt_und_bleibt_Basis()
    {
        var d = new Device(id: "GERAET-C") { Server = _ => Reply("limit", 2, 2, "Schon auf 2 Geräten aktiviert.") };
        var (state, message) = await d.Activation.SyncAsync(Data(), "lizenz");
        Assert.Equal(ActivationState.Refused, state);
        Assert.Equal("Schon auf 2 Geräten aktiviert.", message);
        Assert.False(d.Activation.Allows(Data()));
        Assert.Equal("Schon auf 2 Geräten aktiviert.", d.Activation.Describe(Data()));

        // Ablehnung lässt sich nicht durch Offline-Sein umgehen
        d.Server = _ => throw new HttpRequestException("offline");
        Assert.Equal(ActivationState.Refused, (await d.Activation.SyncAsync(Data(), "lizenz")).State);

        // Ein Platz wird frei, dann klappt es
        d.Server = _ => Reply("active", 2, 2);
        Assert.Equal(ActivationState.Active, (await d.Activation.SyncAsync(Data(), "lizenz")).State);
    }

    [Fact]
    public async Task Gesperrte_Lizenz_und_Serverfehler()
    {
        var d = new Device { Server = _ => Reply("error", message: "Datendatei nicht beschreibbar") };
        var (state, message) = await d.Activation.SyncAsync(Data(), "lizenz");
        Assert.Equal(ActivationState.Grace, state);
        Assert.Equal("Datendatei nicht beschreibbar", message);

        d.Server = _ => Reply("blocked", message: null);
        Assert.Equal(ActivationState.Refused, (await d.Activation.SyncAsync(Data(), "lizenz")).State);
        Assert.Contains("gesperrt", d.Activation.Describe(Data()));

        d.Server = _ => "kein JSON";
        Assert.Equal(ActivationState.Refused, (await d.Activation.SyncAsync(Data(), "lizenz")).State);
    }

    [Fact]
    public async Task Freigeben_macht_den_Platz_frei_und_schaltet_auf_Basis()
    {
        var d = new Device { Server = _ => Reply("active", 2, 2) };
        await d.Activation.SyncAsync(Data(), "lizenz");
        d.Server = f => f["action"] == "release" ? Reply("released", 1, 2) : Reply("active", 2, 2);
        var (ok, message) = await d.Activation.ReleaseAsync(Data(), "lizenz");
        Assert.True(ok);
        Assert.Contains("1 von 2", message);
        Assert.False(d.Activation.Allows(Data()));
        Assert.True(d.Activation.DueForSync(Data()));

        d.Server = _ => throw new HttpRequestException("offline");
        var (ok2, _) = await d.Activation.ReleaseAsync(Data(), "lizenz");
        Assert.False(ok2);
    }

    [Fact]
    public async Task Andere_Lizenz_oder_anderes_Geraet_beginnt_neu()
    {
        var d = new Device { Server = _ => Reply("active") };
        await d.Activation.SyncAsync(Data(), "lizenz");
        Assert.Equal(ActivationState.Active, d.Activation.State(Data()));
        Assert.Equal(ActivationState.Grace, d.Activation.State(Data() with { LicenseId = "TMP-NEU" }));

        // Kopierter Speicherstand auf einem anderen Gerät gilt dort nicht
        var other = new Device(id: "GERAET-B") { Stored = JsonSerializer.Serialize(new { licenseId = "TMP-AKT", deviceId = "GERAET-A", activated = true, firstSeen = Start }) };
        Assert.Equal(ActivationState.Grace, other.Activation.State(Data()));
    }

    [Fact]
    public async Task LicenseManager_schaltet_Pro_nur_bei_erlaubter_Aktivierung_frei()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(dir.FullName, "trackmaster.lic");
            var text = LicenseCodec.Sign(Data(), Keys.Private);
            File.WriteAllText(path, text);
            var d = new Device(id: "GERAET-C") { Server = _ => Reply("limit", 2, 2, "Alle Plätze belegt.") };
            var manager = new LicenseManager(path, Keys.Public, developerMode: false, () => d.Now, activation: d.Activation);
            Assert.True(manager.IsPro);
            Assert.True(manager.ActivationDue);

            Assert.Equal("Alle Plätze belegt.", await manager.SyncActivationAsync("1.5.0"));
            Assert.Equal(text, d.Requests[0]["license"]);
            Assert.False(manager.IsPro);
            Assert.Equal("Basis", manager.EditionName);
            Assert.Contains("nicht aktiviert", manager.Summary);

            d.Server = _ => Reply("active", 2, 2);
            await manager.SyncActivationAsync();
            Assert.True(manager.IsPro);
            Assert.False(manager.ActivationDue);
        }
        finally
        {
            dir.Delete(true);
        }
    }
}
