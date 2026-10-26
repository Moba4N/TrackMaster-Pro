using System.Text.Json;
using TrackMaster.Core;

namespace TrackMaster.Tests;

public class AppearanceTests
{
    [Fact]
    public void Designs_haben_eindeutige_Kennungen_und_Logo_Gruen_ist_Standard()
    {
        Assert.Equal(Design.All.Count, Design.All.Select(d => d.Id).Distinct().Count());
        Assert.Same(Design.TrackPlan, new AppearanceSettings().Palette);
        Assert.Same(Design.TrackPlan, Design.Find("gibt-es-nicht"));
        Assert.Same(Design.Blueprint, Design.Find("blaupause"));
    }

    [Fact]
    public void Unbekannte_Schrift_faellt_auf_Standard_zurueck_und_Groesse_wird_begrenzt()
    {
        var s = new AppearanceSettings("kontrast", "unbekannt", 3.0);
        Assert.Equal("standard", s.FontChoice.Id);
        Assert.Equal(1.4, s.Scale);
        Assert.Equal(0.8, new AppearanceSettings(FontScale: 0.1).Scale);
    }

    [Fact]
    public void Einstellungen_ueberstehen_Speichern_und_Laden()
    {
        var s = new AppearanceSettings("werkstatt", "georgia", 1.12);
        var back = JsonSerializer.Deserialize<AppearanceSettings>(JsonSerializer.Serialize(s));
        Assert.Equal(s, back);
    }
}
