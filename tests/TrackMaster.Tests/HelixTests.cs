using TrackMaster.Core;
using Xunit;

namespace TrackMaster.Tests;

public class HelixTests
{
    private static LayoutPlan Plan() => new() { GaugeId = "H0", BoardWidth = 4000, BoardHeight = 4000 };

    [Fact]
    public void Ovale_Wendel_schliesst_nach_einer_Windung_ueber_dem_Anfang()
    {
        var plan = Plan();
        var spec = new HelixSpec("H0|mc|24230", 2, 100, false, HelixShape.Oval, "H0|mc|24188", 2);
        var (inner, outer) = Helix.Build(plan, spec, null, (1500, 1500));
        Assert.Empty(outer);
        Assert.Equal(2 * (12 + 4), inner.Count);
        var start = inner[0].WorldConnectors()[0];
        var afterTurn = inner[15].WorldConnectors()[1];
        Assert.Equal(start.X, afterTurn.X, 1);
        Assert.Equal(start.Y, afterTurn.Y, 1);
        Assert.Equal(100, inner[15].ConnectorHeights()[1], 1);
        Assert.Equal(2, Connectivity.Analyze(plan).Open.Count);
        Assert.DoesNotContain(PlanCheck.Run(plan), i => i.Level == IssueLevel.Fehler);
    }

    [Theory]
    [InlineData(HelixShape.Rund, true)]
    [InlineData(HelixShape.Rund, false)]
    [InlineData(HelixShape.Oval, true)]
    [InlineData(HelixShape.Oval, false)]
    public void Zweigleisige_Wendel_hat_zwei_getrennte_Gleise_ohne_Kollision(HelixShape shape, bool flex)
    {
        var plan = Plan();
        var outerKey = flex ? null : "H0|mc|24330";
        var spec = new HelixSpec("H0|mc|24230", 2, 100, true, shape, "H0|mc|24188", 1, 2, 77.5, outerKey);
        var (inner, outer) = Helix.Build(plan, spec, null, (1500, 1500));
        Assert.NotEmpty(outer);
        Assert.Equal(4, Connectivity.Analyze(plan).Open.Count);
        Assert.DoesNotContain(PlanCheck.Run(plan), i => i.Level == IssueLevel.Fehler);
        // Beide Gleise enden auf gleicher Höhe, das äußere 77,5 mm weiter außen
        Assert.Equal(inner[^1].ConnectorHeights()[1], outer[^1].ConnectorHeights()[1], 1);
        var a = inner[^1].WorldConnectors()[1];
        var b = outer[^1].WorldConnectors()[1];
        Assert.Equal(77.5, Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2)), 1);
        if (flex) Assert.True(outer[0].Def.IsFlex);
    }

    [Fact]
    public void Holzliste_enthaelt_Trasse_Stuetzen_und_Aufmasse()
    {
        var gauge = Catalog.GetGauge("H0");
        var spec = new HelixSpec("H0|mc|24230", 3, 100, false, HelixShape.Oval, "H0|mc|24188", 2, 2, 77.5, "H0|mc|24330");
        var m = Helix.Material(spec, gauge);
        // Ring: innen R 437,5 − 22,5 − 25 = 390, außen R 515 + 22,5 + 25 = 562,5
        Assert.Equal(390, m.InnerBoardRadius, 1);
        Assert.Equal(562.5, m.OuterBoardRadius, 1);
        var seg = m.Parts.Single(p => p.Name.StartsWith("Trassensegment"));
        Assert.Equal(12, seg.Count);                         // 4 Segmente je Kreis × 3 Windungen
        Assert.Equal(6, m.Parts.Single(p => p.Name == "Gerades Trassenbrett").Count);
        Assert.Contains(m.Parts, p => p.Name.StartsWith("Gewindestange"));
        Assert.Contains(m.Parts, p => p.Name == "Grundplatte");
        Assert.True(m.Sheets >= 1);
        Assert.Contains("Randaufmaß", m.ToText());
        Assert.Contains("Anzahl;Teil", m.ToCsv());
    }

    [Fact]
    public void Ovale_Wendel_braucht_Boegen_die_den_Halbkreis_teilen()
    {
        var plan = Plan();
        var curve = Catalog.Gauges.First(g => g.Id == "H0").Systems.SelectMany(s => s.Pieces)
            .FirstOrDefault(p => p.Kind == PieceKind.Curve && Helix.Fits(p, HelixShape.Rund) && !Helix.Fits(p, HelixShape.Oval));
        if (curve is null) return;
        Assert.Throws<InvalidOperationException>(() => Helix.Build(plan, new HelixSpec(curve.Key, 1, 100, false, HelixShape.Oval, "H0|mc|24188", 1), null, (1000, 1000)));
    }
}
