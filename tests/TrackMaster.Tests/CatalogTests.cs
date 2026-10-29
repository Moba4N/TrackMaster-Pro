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
        {
            // Schmalspur H0m/H0e: Hersteller liefern nur Weichen und Flexgleis
            var kinds = g.Id is "H0m" or "H0e" ? new[] { PieceKind.Switch } : [PieceKind.Straight, PieceKind.Curve, PieceKind.Switch, PieceKind.Buffer];
            foreach (var kind in kinds)
                Assert.Contains(g.Systems.SelectMany(s => s.Pieces), p => p.Kind == kind);
        }
    }

    [Fact]
    public void Katalog_ist_aus_Herstellerdaten_vollstaendig()
    {
        Assert.True(Catalog.Gauges.SelectMany(g => g.Systems).Sum(s => s.Pieces.Count) > 900);
        foreach (var nr in new[] { "H0|mc|24071", "H0|mc|24206", "H0|mc|24649", "H0|geo|61140", "H0|piko|55226", "N|picc|9100", "N|rf-n|22202", "TT|tillig|83101", "G|lgb|10000", "H0m|tillig-h0m|85632" })
            Assert.True(Catalog.Find(nr)?.Verified, nr);
    }

    [Fact]
    public void Weichenstraenge_beginnen_an_Anschluss_0()
    {
        foreach (var d in Catalog.Gauges.SelectMany(g => g.Systems).SelectMany(s => s.Pieces).Where(p => p.Kind == PieceKind.Switch))
            foreach (var path in TrackGeometry.LocalPaths(d, false))
            {
                Assert.True(Math.Abs(path[0].X) < 0.5 && Math.Abs(path[0].Y) < 0.5, d.Key);
                Assert.True(Math.Abs(Angles.Normalize(path[0].Heading)) < 0.5, d.Key);
            }
    }

    [Fact]
    public void Maerklin_C_Weiche_passt_zum_Gegenbogen_R2()
    {
        // Abzweig der 24611 endet wie ein Bogen 24224 (R2, 24,3°), dahinter passt der Gegenbogen ins Parallelgleis
        var w = TrackGeometry.LocalConnectors(Catalog.Get("H0|mc|24611"), false)[2];
        var b = TrackGeometry.LocalConnectors(Catalog.Get("H0|mc|24224"), false)[1];
        Assert.Equal(b.X, w.X, 0);
        Assert.Equal(-Math.Abs(b.Y), w.Y, 0);
        Assert.Equal(-24.3, w.Heading, 1);
    }

    [Fact]
    public void Fruehere_Katalogeintraege_bleiben_ladbar_aber_verborgen()
    {
        var alt = Catalog.Get("H0|geo|WL10");
        Assert.False(alt.Verified);
        Assert.DoesNotContain(alt, Catalog.GetSystem("H0", "geo")!.Pieces);
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
                PieceKind.Switch => d.RouteCount + 1,
                PieceKind.Turntable or PieceKind.TransferTable => d.Ports!.Count,
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
        Assert.Equal(-24.3, c[2].Heading, 1);
        Assert.Equal(24.3, c[3].Heading, 1);
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
        var p = plan.Add("N|kato|20-222");
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
