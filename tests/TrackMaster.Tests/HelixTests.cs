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

public class HelixGradientTests
{
    [Theory]
    [InlineData(HelixShape.Oval)]
    [InlineData(HelixShape.Rund)]
    public void Steigung_nachtraeglich_aendern_passt_beide_Gleise_an(HelixShape shape)
    {
        var plan = new LayoutPlan { GaugeId = "H0", BoardWidth = 4000, BoardHeight = 4000 };
        var spec = new HelixSpec("H0|mc|24230", 2, 100, false, shape, "H0|mc|24188", 2, 2, 77.5, "H0|mc|24330");
        var (inner, outer) = Helix.Build(plan, spec, null, (1500, 1500));
        var ids = inner.Concat(outer).Select(p => p.Id).ToHashSet();
        var r = PlanEditing.ApplyGradient(plan, ids, 0, 4);
        Assert.Equal(ids.Count, r.Pieces);
        var innerEnd = inner[^1].ConnectorHeights()[1];
        var outerEnd = outer[^1].ConnectorHeights()[1];
        // Inneres Gleis mit 4 %, äußeres endet auf derselben Höhe
        Assert.Equal(Helix.TurnLength(spec, spec.Curve.Radius) * 2 * 0.04, innerEnd, 0);
        Assert.Equal(innerEnd, outerEnd, 1);
        Assert.Equal(0, outer[0].ConnectorHeights()[0], 1);
        Assert.Equal(4, Connectivity.Analyze(plan).Open.Count);
        Assert.DoesNotContain(PlanCheck.Run(plan), i => i.Level == IssueLevel.Fehler);
        // Auch rückwärts: das äußere Gleis beginnt oben wie das innere
        PlanEditing.ApplyGradient(plan, ids, 0, 4, reverse: true);
        // Das innere (steilere) Gleis bekommt die eingegebenen 4 %
        Assert.Equal(-0.04, inner[0].EffectiveGrade, 4);
        Assert.Equal(inner[^1].ConnectorHeights()[1], outer[^1].ConnectorHeights()[1], 1);
        Assert.Equal(inner[0].ConnectorHeights()[0], outer[0].ConnectorHeights()[0], 1);
    }
}

public class HelixRibTests
{
    [Fact]
    public void Randaufmass_laesst_sich_waehlen_und_bestimmt_die_Trassenbreite()
    {
        var gauge = Catalog.GetGauge("H0");
        var spec = new HelixSpec("H0|mc|24230", 2, 100, false, Edge: 15);
        var m = Helix.Material(spec, gauge);
        Assert.Equal(45 + 2 * 15, m.TrackWidth, 1);
        Assert.Equal(m.TrackWidth, Helix.TrackBoardWidth(spec, gauge), 1);
        // 15 mm Rand lässt neben der Gewindestange (12 mm vom Rand) nur 3 mm frei
        Assert.Contains(m.Notes, n => n.StartsWith("Achtung: Zwischen Gleisbett"));
    }

    [Theory]
    [InlineData(HelixShape.Rund)]
    [InlineData(HelixShape.Oval)]
    public void Spantenbau_liefert_Spanten_mit_Schlitzen_je_Ebene(HelixShape shape)
    {
        var gauge = Catalog.GetGauge("H0");
        var spec = new HelixSpec("H0|mc|24230", 2, 100, false, shape, shape == HelixShape.Oval ? "H0|mc|24188" : null, shape == HelixShape.Oval ? 2 : 0,
            Construction: HelixConstruction.Spanten);
        var m = Helix.Material(spec, gauge);
        Assert.DoesNotContain(m.Parts, p => p.Name.StartsWith("Gewindestange"));
        Assert.Contains(m.Parts, p => p.Name.StartsWith("Spant"));
        var ribs = m.Ribs!;
        Assert.NotEmpty(ribs);
        // Erster Spant am Wendelanfang: Schlitze bei 0, 100 und 200 mm (drei Ebenen bei zwei Windungen)
        Assert.Equal([0, 100, 200], ribs[0].SlotBottoms);
        // Abstand der Spanten entlang des inneren Gleises höchstens 250 mm
        var turn = Helix.TurnLength(spec, spec.Curve.Radius);
        Assert.True(turn / ribs.Count <= Helix.RibSpacing(gauge) + 0.5);
        // Schlitze steigen gleichmäßig: zwischen zwei Ebenen genau die Höhe je Windung
        Assert.All(ribs, r => Assert.All(r.SlotBottoms.Zip(r.SlotBottoms.Skip(1)), p => Assert.Equal(100, p.Second - p.First, 1)));
        Assert.All(ribs, r => Assert.True(r.Height > r.SlotBottoms[^1] + r.SlotHeight));
        Assert.Contains("Spant;Lage", m.ToCsv());
        Assert.Contains("Aufmaße der Spanten", m.ToText());
    }
}
