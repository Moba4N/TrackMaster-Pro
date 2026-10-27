using TrackMaster.Core;

namespace TrackMaster.Tests;

public class BuildingKitTests
{
    [Theory]
    [InlineData("H0", "H0")]
    [InlineData("H0m", "H0")]
    [InlineData("N", "N")]
    [InlineData("Z", "Z")]
    [InlineData("G", "G")]
    public void Baugroessen_mit_eigenen_Bausaetzen_bekommen_Originalmasse(string gauge, string scale)
    {
        var kits = BuildingKits.For(Catalog.GetGauge(gauge));
        Assert.NotEmpty(kits);
        Assert.All(kits, k => { Assert.Equal(scale, k.Scale); Assert.False(k.Converted); });
        Assert.Equal(BuildingKits.All.Where(k => k.Scale == scale), kits);
    }

    [Theory]
    [InlineData("TT", "N", 160.0 / 120)]
    [InlineData("0", "H0", 87.0 / 45)]
    [InlineData("1", "G", 22.5 / 32)]
    public void Andere_Baugroessen_werden_massstaeblich_umgerechnet(string gauge, string source, double factor)
    {
        var kits = BuildingKits.For(Catalog.GetGauge(gauge));
        var originals = BuildingKits.All.Where(k => k.Scale == source).ToList();
        Assert.Equal(originals.Count, kits.Count);
        for (var i = 0; i < kits.Count; i++)
        {
            Assert.True(kits[i].Converted);
            Assert.Equal(Math.Round(originals[i].Length * factor), kits[i].Length);
            Assert.Equal(Math.Round(originals[i].Height * factor), kits[i].Height);
        }
    }

    [Fact]
    public void Jede_Vorlage_hat_Kategorie_und_eindeutige_Artikelnummer_je_Baugroesse()
    {
        Assert.All(BuildingKits.All, k =>
        {
            Assert.Contains(k.Category, BuildingKits.Categories);
            Assert.True(k.Length > 0 && k.Width > 0 && k.Height > 0);
        });
        Assert.Equal(BuildingKits.All.Count, BuildingKits.All.Select(k => k.Scale + k.Number).Distinct().Count());
    }

    [Fact]
    public void Vorlage_setzt_Gebaeude_mit_Grundmass_und_Hoehe()
    {
        var s = new PlanSession();
        var kit = BuildingKits.For(Catalog.GetGauge("H0")).First(k => k.Number == "110099");
        var b = s.AddBuilding(kit, 500.4, 300);
        Assert.Equal(SceneryKind.Building, b.Kind);
        Assert.Equal((240.0, 125.0, 135.0), (b.Width, b.Depth, b.Height));
        Assert.Equal((500.0, 300.0), (b.X, b.Y));
        Assert.Contains(b, s.Plan.Scenery);
    }
}
