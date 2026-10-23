using TrackMaster.Core;

namespace TrackMaster.Tests;

public class CatalogTests
{
    [Fact]
    public void Artikelnummern_sind_je_System_eindeutig()
    {
        foreach (var g in Catalog.Gauges)
            foreach (var s in g.Systems)
            {
                var doppelt = s.Pieces.GroupBy(p => p.Number).Where(x => x.Count() > 1).Select(x => x.Key).ToList();
                Assert.True(doppelt.Count == 0, $"{g.Id} {s.Name}: {string.Join(", ", doppelt)}");
            }
    }

    [Fact]
    public void Jede_Spur_hat_Geraden_Boegen_Weichen_und_Prellbock()
    {
        foreach (var g in Catalog.Gauges)
            foreach (var kind in new[] { PieceKind.Straight, PieceKind.Curve, PieceKind.Switch, PieceKind.Buffer })
                Assert.Contains(g.Systems.SelectMany(s => s.Pieces), p => p.Kind == kind);
    }

    [Fact]
    public void Jedes_Teil_hat_gueltige_Geometrie()
    {
        foreach (var d in Catalog.Gauges.SelectMany(g => g.Systems).SelectMany(s => s.Pieces))
        {
            Assert.True(d.TrackLength > 0, d.Key);
            var connectors = TrackGeometry.LocalConnectors(d, false);
            var expected = d.Kind switch
            {
                PieceKind.Buffer => 1,
                PieceKind.Crossing => 4,
                PieceKind.Switch => (d.Paths?.Count ?? 2) + 1,
                _ => 2,
            };
            Assert.Equal(expected, connectors.Count);
            Assert.All(connectors, c => Assert.True(double.IsFinite(c.X) && double.IsFinite(c.Y), d.Key));
        }
    }

    [Fact]
    public void Dreiwegweiche_hat_Abzweige_nach_beiden_Seiten()
    {
        var d = Catalog.Get("H0|mc|24630");
        var c = TrackGeometry.LocalConnectors(d, false);
        Assert.Equal(4, c.Count);
        Assert.Equal(0, c[1].Y, 6);
        Assert.True(c[2].Y < 0 && c[3].Y > 0);
        Assert.Equal(-22.5, c[2].Heading, 6);
        Assert.Equal(22.5, c[3].Heading, 6);
        Assert.Equal(2, d.Drives);
    }

    [Fact]
    public void Bogenweiche_biegt_mit_beiden_Straengen_zur_selben_Seite()
    {
        var c = TrackGeometry.LocalConnectors(Catalog.Get("H0|mc|24671"), false);
        Assert.True(c[1].Y < 0 && c[2].Y < 0);
        Assert.Equal(-30, c[1].Heading, 6);
        Assert.Equal(-30, c[2].Heading, 6);
        var gespiegelt = TrackGeometry.LocalConnectors(Catalog.Get("H0|mc|24671"), true);
        Assert.True(gespiegelt[1].Y > 0 && gespiegelt[2].Y > 0);
    }

    [Fact]
    public void Abzweig_mit_Gerade_rechnet_Hoehe_entlang_des_ganzen_Strangs()
    {
        var plan = LayoutPlan.Empty("N");
        var p = plan.Add("N|peco-n|Y");
        p.Grade = 0.02;
        var d = p.Def;
        var len = TrackGeometry.PathLength(d, 1);
        Assert.Equal(0.02 * len, p.ConnectorHeights()[2], 6);
    }

    [Fact]
    public void Alter_Schluessel_der_DKW_wird_noch_gefunden()
    {
        Assert.Equal("24624", Catalog.Get("H0|mc|24620").Number);
    }

    [Fact]
    public void Trix_C_hat_die_Masse_des_Maerklin_C_Gleises()
    {
        Assert.Equal(Catalog.Get("H0|mc|24188").Length, Catalog.Get("H0|tc|62188").Length);
        Assert.False(Catalog.GetSystem("H0", "tc")!.ThreeRail);
    }

    [Fact]
    public void Mitfahren_ueber_eine_Dreiwegweiche()
    {
        var plan = LayoutPlan.Empty("H0");
        var w = PlanEditing.Attach(plan, "H0|mc|24630", null, (1000, 600));
        PlanEditing.Attach(plan, "H0|mc|24188", Connectivity.Analyze(plan).Find(w.Id, 1), (0, 0));
        var path = RidePath.Build(plan, w.Id);
        Assert.False(path.IsEmpty);
        Assert.True(path.Length > 188);
    }
}

public class FlexTests
{
    [Fact]
    public void Jedes_Gleissystem_hat_ein_Flexgleis()
    {
        foreach (var g in Catalog.Gauges)
            foreach (var s in g.Systems)
            {
                var stock = Catalog.Flex(g, s);
                Assert.True(stock.Length > 0, $"{g.Id} {s.Name}");
                var def = Catalog.Get(Catalog.FlexStraightKey(g.Id, 300, stock.SystemId));
                Assert.True(def.IsFlex);
                Assert.Equal(300, def.Length);
            }
    }

    [Fact]
    public void Flexgleis_des_Herstellers_traegt_dessen_Artikelnummer()
    {
        var stock = Catalog.Flex(Catalog.GetGauge("H0"), Catalog.GetSystem("H0", "mk"));
        Assert.Equal("2205", stock.Number);
        Assert.Equal(900, stock.Length);
        var def = Catalog.Get(Catalog.FlexCurveKey("H0", 600, 20, stock.SystemId));
        Assert.Equal("2205", def.Number);
        Assert.Equal(PieceKind.Curve, def.Kind);
    }

    [Fact]
    public void Allgemeines_Flexgleis_aus_alten_Plaenen_gilt_weiter()
    {
        var def = Catalog.Get("H0|flex|L500");
        Assert.True(def.IsFlex);
        Assert.Equal(500, def.Length);
    }

    [Fact]
    public void Stueckliste_trennt_Flexgleise_nach_Hersteller()
    {
        var plan = LayoutPlan.Empty("H0");
        plan.Add(Catalog.FlexStraightKey("H0", 1000, "flex-mk"));
        plan.Add(Catalog.FlexStraightKey("H0", 500));
        var rows = BillOfMaterials.Build(plan).Where(r => r.Def.IsFlex).ToList();
        Assert.Equal(2, rows.Count);
        var k = rows.Single(r => r.Def.Number == "2205");
        Assert.Equal(2, k.Count); // 1050 mm mit Verschnitt → 2 Stücke zu 900 mm
    }
}
