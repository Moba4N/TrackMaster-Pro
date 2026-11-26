using TrackMaster.Core;

namespace TrackMaster.Tests;

/// <summary>Alle Versionen speichern Pläne als *.mpr; ältere *.trackmaster.json werden beim Speichern umbenannt.</summary>
public class PlanFileExtensionTests
{
    [Theory]
    [InlineData(@"C:\Plaene\Anlage.mpr", @"C:\Plaene\Anlage.mpr")]
    [InlineData(@"C:\Plaene\Anlage.trackmaster.json", @"C:\Plaene\Anlage.mpr")]
    [InlineData(@"C:\Plaene\Anlage.json", @"C:\Plaene\Anlage.mpr")]
    [InlineData(@"C:\Plaene\Anlage", @"C:\Plaene\Anlage.mpr")]
    [InlineData(@"C:\Plaene\Anlage.MPR", @"C:\Plaene\Anlage.MPR")]
    public void Speichern_immer_als_mpr(string input, string expected)
        => Assert.Equal(expected, PlanSerializer.WithPlanExtension(input));

    [Theory]
    [InlineData("Anlage.mpr", "Anlage")]
    [InlineData("Anlage.trackmaster.json", "Anlage")]
    [InlineData("Anlage.json", "Anlage")]
    [InlineData("Anlage", "Anlage")]
    public void Name_ohne_Endung(string input, string expected) => Assert.Equal(expected, PlanSerializer.BaseName(input));

    [Fact]
    public void Endung_ist_mpr() => Assert.Equal(".mpr", PlanSerializer.FileExtension);
}
