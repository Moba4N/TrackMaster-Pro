using TrackMaster.Core;

namespace TrackMaster.Tests;

/// <summary>Zwischenspeicher und Verzeichnisse, die große Pläne schneller machen, dürfen nichts am Ergebnis ändern.</summary>
public class OptimizationTests
{
    [Fact]
    public void Gleisgeometrie_wird_je_Lage_gemerkt_und_bleibt_richtig()
    {
        var curve = Catalog.Get("H0|mc|24230");
        var normal = TrackGeometry.LocalConnectors(curve, false);
        var flipped = TrackGeometry.LocalConnectors(curve, true);
        Assert.Same(normal, TrackGeometry.LocalConnectors(curve, false));
        Assert.Same(flipped, TrackGeometry.LocalConnectors(curve, true));
        // gespiegelt biegt der Bogen zur anderen Seite
        Assert.Equal(normal[1].X, flipped[1].X, 6);
        Assert.Equal(-normal[1].Y, flipped[1].Y, 6);
        Assert.Equal(curve.TrackLength, TrackGeometry.PathLength(curve, 0), 6);
    }

    [Fact]
    public void Katalogteil_folgt_dem_geaenderten_Schluessel()
    {
        var plan = new LayoutPlan { GaugeId = "H0" };
        var p = plan.Add("H0|mc|24188");
        Assert.Equal("24188", p.Def.Number);
        p.Key = "H0|mc|24230";
        Assert.Equal("24230", p.Def.Number);
    }

    [Fact]
    public void Anschluesse_je_Gleis_wie_vorher()
    {
        var plan = SamplePlan.Build();
        var conn = Connectivity.Analyze(plan);
        foreach (var p in plan.Pieces)
        {
            var expected = conn.All.Where(c => c.PieceId == p.Id).ToList();
            Assert.Equal(expected, conn.Of(p.Id));
            foreach (var c in expected) Assert.Same(c, conn.Find(p.Id, c.Index));
        }
        Assert.Empty(conn.Of(-5));
        Assert.Null(conn.Find(plan.Pieces[0].Id, 99));
    }

    [Fact]
    public void Gleisverzeichnis_und_Sitzung_finden_dieselben_Gleise()
    {
        var session = new PlanSession();
        var plan = session.Plan;
        var index = plan.PieceIndex();
        foreach (var p in plan.Pieces)
        {
            Assert.Same(p, index[p.Id]);
            Assert.Same(p, session.ViewPiece(p.Id));
        }
        Assert.Null(session.ViewPiece(-1));

        // nach dem Löschen ist das Gleis auch im Verzeichnis der Sitzung weg
        var first = plan.Pieces[0];
        session.Record();
        plan.RemovePieces(new HashSet<int> { first.Id });
        session.Commit();
        Assert.Null(session.ViewPiece(first.Id));
    }

    [Fact]
    public void Hoehenfeld_folgt_geaenderter_Flaeche()
    {
        var hill = new SceneryItem { Kind = SceneryKind.Terrain, Height = 100, Points = [(0, 0), (1000, 0), (1000, 1000), (0, 1000)] };
        var a = TerrainField.For(hill);
        Assert.Same(a, TerrainField.For(hill));
        var top = a.At(500, 500);

        hill.Height = 50;
        Assert.True(TerrainField.For(hill).At(500, 500) < top - 1);
        hill.Points[2] = (3000, 3000);
        Assert.NotSame(a, TerrainField.For(hill));
        Assert.True(TerrainField.For(hill).At(1500, 1500) > 0);
    }

    [Fact]
    public void Planpruefung_findet_Kollision_weiterhin()
    {
        var plan = new LayoutPlan { GaugeId = "H0", BoardWidth = 2000, BoardHeight = 1000 };
        var a = plan.Add(Catalog.FlexStraightKey("H0", 600));
        PlanEditing.PlaceAt(a, 0, 100, 500, 180);
        var b = plan.Add(Catalog.FlexStraightKey("H0", 600));
        PlanEditing.PlaceAt(b, 0, 100, 520, 180);
        Assert.Contains(PlanCheck.Run(plan), i => i.Category == "Kollision" && i.PieceIds.Contains(a.Id) && i.PieceIds.Contains(b.Id));

        // genug Abstand: keine Meldung
        PlanEditing.PlaceAt(b, 0, 100, 600, 180);
        Assert.DoesNotContain(PlanCheck.Run(plan), i => i.Category == "Kollision");
    }
}

/// <summary>Bedienung: Öffnen (Web, Android) verwirft geänderte Pläne nicht endgültig.</summary>
public class UsabilityTests
{
    [Fact]
    public void Oeffnen_holt_geaenderten_Plan_mit_Rueckgaengig_zurueck()
    {
        var s = new PlanSession();
        var before = s.Plan.Pieces.Count;
        s.Record();
        s.Plan.RemovePieces(new HashSet<int> { s.Plan.Pieces[0].Id });
        s.Commit();
        Assert.True(s.Dirty);

        var other = new LayoutPlan { Name = "Andere", GaugeId = "H0" };
        s.OpenJson(PlanSerializer.ToJson(other), "Andere");
        Assert.Equal("Andere", s.Plan.Name);
        Assert.True(s.History.CanUndo);
        s.Undo();
        Assert.Equal(before - 1, s.Plan.Pieces.Count);
    }

    [Fact]
    public void Oeffnen_ohne_Aenderungen_beginnt_mit_leerem_Verlauf()
    {
        var s = new PlanSession();
        s.OpenJson(PlanSerializer.ToJson(new LayoutPlan { Name = "Andere", GaugeId = "H0" }), "Andere");
        Assert.False(s.History.CanUndo);
    }
}
