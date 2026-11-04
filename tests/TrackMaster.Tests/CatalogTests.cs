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
    public void Unbekannte_Teile_werden_beim_Laden_uebersprungen()
    {
        var plan = LayoutPlan.Empty("H0");
        plan.Add("H0|geo|61110");
        var json = PlanSerializer.ToJson(plan).Replace("H0|geo|61110", "H0|geo|WL10");
        var back = PlanSerializer.FromJson(json, out var skipped);
        Assert.Equal(1, skipped);
        Assert.Empty(back.Pieces);
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

public class FleischmannNTests
{
    [Fact]
    public void Gleise_ohne_Bettung_haben_die_Laengen_aus_dem_Herstellerkatalog()
    {
        var sys = Catalog.GetSystem("N", "rf-n");
        double Len(string nr) => sys.Pieces.First(p => p.Number == nr).Length;
        Assert.Equal(12.2, Len("22207"));
        Assert.Equal(33.6, Len("22206"));
        Assert.Equal(50, Len("22205"));
        Assert.Equal(54.2, Len("22204"));
        Assert.Equal(104.2, Len("22203"));
        Assert.Equal(312.6, Len("22202"));
        Assert.Equal(730.6, sys.FlexLength);
        Assert.Equal(730.6, sys.MoreFlex.Single(f => f.Number == "22201").Length);
        // Bögen: Winkel laut Tabelle
        foreach (var (nr, a) in new[] { ("22232", 6.0), ("22231", 24), ("22221", 30), ("22234", 6), ("22233", 24), ("22222", 30), ("22223", 30),
                     ("22235", 15), ("22224", 30), ("22225", 15), ("22226", 15), ("22227", 15), ("22228", 12) })
            Assert.Equal(a, sys.Pieces.First(p => p.Number == nr).Angle);
    }
}

public class MinitrixTests
{
    [Fact]
    public void Gleise_haben_die_Masse_aus_dem_Minitrix_Prospekt()
    {
        var sys = Catalog.GetSystem("N", "minitrix");
        TrackPieceDef P(string nr) => sys.Pieces.First(p => p.Number == nr);
        foreach (var (nr, len) in new[] { ("14902", 312.6), ("14904", 104.2), ("14905", 76.3), ("14906", 54.2), ("14907", 50.0),
                     ("14909", 33.6), ("14908", 27.9), ("14903", 17.2), ("14999", 59.0), ("14975", 120.0) })
            Assert.Equal(len, P(nr).Length);
        Assert.Equal(730, sys.FlexLength);
        foreach (var (nr, r, a) in new[] { ("14912", 194.6, 30.0), ("14914", 194.6, 24), ("14916", 194.6, 6), ("14922", 228.2, 30), ("14924", 228.2, 24),
                     ("14926", 228.2, 6), ("14917", 329.0, 15), ("14927", 362.6, 15), ("14918", 492.6, 15), ("14928", 526.2, 15) })
        {
            Assert.Equal(r, P(nr).Radius);
            Assert.Equal(a, P(nr).Angle);
        }
    }

    [Fact]
    public void Zwei_Weichen_15_Grad_ergeben_den_Gleisabstand()
    {
        var w = Catalog.GetSystem("N", "minitrix").Pieces.First(p => p.Number == "14938");
        var branch = w.Paths![1];
        var (r, a, tail) = (branch[0].Radius, branch[0].Angle * Math.PI / 180, branch[1].Length);
        var offset = r * (1 - Math.Cos(a)) + tail * Math.Sin(a);
        Assert.Equal(33.6, 2 * offset, 1);
    }
}

public class PiccoloTests
{
    [Fact]
    public void Elektromagnetische_Weichen_stehen_mit_eigener_Nummer_in_der_Bibliothek()
    {
        var sys = Catalog.GetSystem("N", "picc");
        TrackPieceDef P(string nr) => sys.Pieces.First(p => p.Number == nr);
        foreach (var (hand, elektro) in new[] { ("9170", "9172"), ("9171", "9173"), ("9178", "9180"), ("9179", "9181"), ("9157", "9158"),
                     ("9174", "9176"), ("9175", "9177"), ("9168", "9182"), ("9169", "9183"), ("9184", "9186"), ("9185", "9187") })
        {
            var (h, e) = (P(hand), P(elektro));
            Assert.Equal(h.Kind, e.Kind);
            Assert.Equal(h.Length, e.Length);
            Assert.Equal(h.Radius, e.Radius);
            Assert.Equal(h.Angle, e.Angle);
            Assert.Equal(h.RightHand, e.RightHand);
            Assert.Contains("elektromagnetisch", e.Name);
        }
        // Gleise aus der Übersicht
        foreach (var (nr, len) in new[] { ("9100", 222.0), ("9101", 111), ("9102", 57.5), ("9103", 55.5), ("9108", 111), ("9112", 111), ("9114", 111), ("9115", 55.5) })
            Assert.Equal(len, P(nr).Length);
        foreach (var (nr, r, a) in new[] { ("9120", 192.0, 45.0), ("9122", 192, 15), ("9123", 192, 7.5), ("9125", 225.6, 45), ("9127", 225.6, 15),
                     ("9128", 225.6, 7.5), ("9130", 396.4, 30), ("9131", 396.4, 15), ("9135", 430, 30), ("9136", 430, 15) })
            Assert.Equal((r, a), (P(nr).Radius, P(nr).Angle));
        Assert.Equal(777, sys.FlexLength);
        // DKW 9164–9167 öffnen als gleiche Form
        Assert.Equal("9184", Catalog.Get("N|picc|9164").Number);
        Assert.Equal("9187", Catalog.Get("N|picc|9167").Number);
    }
}
