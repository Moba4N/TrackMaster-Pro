using TrackMaster.Core;

namespace TrackMaster.Tests;

/// <summary>YaMoRC-Decoder als einzelne Bausteine: Liste, Setzen, Adressbereiche, Zuordnung und Speichern.</summary>
public class YamorcTests
{
    [Fact]
    public void Jedes_Modell_hat_eine_Gruppe_und_eindeutige_Kennung()
    {
        Assert.Equal(Yamorc.Models.Count, Yamorc.Models.Select(m => m.Id).Distinct().Count());
        Assert.Equal(Yamorc.Models.Count, Yamorc.Groups.Sum(g => g.Models.Count));
        Assert.All(Yamorc.Models, m => Assert.False(string.IsNullOrWhiteSpace(m.Name)));
        Assert.Contains(Yamorc.Models, m => m.Id == "YD8116");
        Assert.Contains(Yamorc.Models, m => m.Role == ModuleRole.Melden);
    }

    [Fact]
    public void Bausteine_bekommen_aufeinanderfolgende_Startadressen()
    {
        var s = new PlanSession { IsPro = () => true };
        var a = s.AddModule("YD8116", 100, -200)!;
        var b = s.AddModule("YD8008", 400, -200)!;
        var r = s.AddModule("YD6016LN-CS", 700, -200)!;
        Assert.Equal(PowerSourceType.Baustein, PowerSupply.Type(a));
        Assert.Equal((1, 16), Yamorc.Range(a));
        Assert.Equal((17, 24), Yamorc.Range(b));
        // Rückmelder zählen eigene Kontakte
        Assert.Equal((1, 16), Yamorc.Range(r));
        Assert.Equal("YD8116 1", a.Name);
        Assert.Contains(a, PowerSupply.Modules(s.Plan));
        Assert.DoesNotContain(a, PowerSupply.Sources(s.Plan));
    }

    [Fact]
    public void Zentrale_von_YaMoRC_wird_als_Zentrale_gesetzt()
    {
        var s = new PlanSession { IsPro = () => true };
        s.SelectModule("YD7010");
        Assert.Equal(PowerSourceType.Zentrale, s.PowerType);
        var z = s.AddModule("YD7010", 0, -300)!;
        Assert.Equal(PowerSourceType.Zentrale, PowerSupply.Type(z));
        Assert.Contains("YD7010", PowerSupply.Label(z));
        PowerSupply.ApplyType(z, PowerSourceType.Booster, s.Plan.Gauge);
        Assert.Null(z.Model);
    }

    [Fact]
    public void Weichen_hängen_über_ihre_Adresse_am_Baustein()
    {
        var s = new PlanSession { IsPro = () => true };
        s.AssignAddresses(1, false);
        var items = Electrics.SwitchingItems(s.Plan);
        Assert.NotEmpty(items);
        var m = s.AddModule("YD8044", 0, -200)!;
        var assigned = Yamorc.Assigned(s.Plan, m);
        Assert.NotEmpty(assigned);
        Assert.All(assigned, x => Assert.InRange(x.Address, 1, 4));
        Assert.Equal("YD8044 1, Ausgang 1", Yamorc.Location(s.Plan, 1));
        Assert.Null(Yamorc.Location(s.Plan, 5));
        // Adressen außerhalb des Bausteins werden gemeldet
        if (items.Sum(i => i.AddressCount) > 4)
            Assert.Contains(Electrics.Analyze(s.Plan).Issues, i => i.Category == "Decoder" && i.Level == IssueLevel.Hinweis);
    }

    [Fact]
    public void Überlappende_Bereiche_ergeben_eine_Warnung()
    {
        var s = new PlanSession { IsPro = () => true };
        var a = s.AddModule("YD8116", 0, -200)!;
        var b = s.AddModule("YD8008", 300, -200)!;
        Assert.DoesNotContain(Electrics.Analyze(s.Plan).Issues, i => i.Category == "Decoder" && i.Level == IssueLevel.Warnung);
        s.SetModuleAddress(b.Id, 9);
        Assert.Contains(Electrics.Analyze(s.Plan).Issues, i => i.Category == "Decoder" && i.Level == IssueLevel.Warnung);
        Assert.Equal(1, a.Address);
    }

    [Fact]
    public void Modell_und_Startadresse_werden_gespeichert()
    {
        var s = new PlanSession { IsPro = () => true };
        var a = s.AddModule("YD8248", 0, -200)!;
        s.SetModuleAddress(a.Id, 101);
        var back = PlanSerializer.FromJson(PlanSerializer.ToJson(s.Plan), out _);
        var m = Assert.Single(PowerSupply.Modules(back));
        Assert.Equal("YD8248", m.Model);
        Assert.Equal(101, m.Address);
        Assert.Equal(PowerSourceType.Baustein, PowerSupply.Type(m));
        Assert.Equal(a.Name, m.Name);
    }
}
