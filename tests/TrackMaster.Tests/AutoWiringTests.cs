using TrackMaster.Core;

namespace TrackMaster.Tests;

/// <summary>Automatisch verkabeln: Strom und Masse je Abschnitt, Booster bei großen Anlagen, Kehrschleifenmodule bei Gleichstrom.</summary>
public class AutoWiringTests
{
    private static PlanSession Session(LayoutPlan plan)
    {
        var s = new PlanSession { IsPro = () => true };
        s.Replace(plan, null);
        return s;
    }

    private static IReadOnlyList<PlanIssue> Errors(LayoutPlan plan)
        => Electrics.Analyze(plan).Issues.Where(i => i.Level == IssueLevel.Fehler || i.Category == "Masse").ToList();

    /// <summary>Gleichstrom-Ballonschleife (Kehrschleife) am Abzweig einer Weiche, Anschluss 0 bleibt offen.</summary>
    private static LayoutPlan Balloon()
    {
        var plan = new LayoutPlan { GaugeId = "H0", BoardWidth = 3000, BoardHeight = 3000 };
        var sw = plan.Add("H0|geo|61140");
        PlanEditing.PlaceAt(sw, 0, 1000, 2000, 0);
        var at = sw.WorldConnectors()[1];
        foreach (var (r, angle, clockwise) in new[] { (500.0, 39.109, true), (400, 149.74, false), (400, 149.74, false), (300, 57.873, true) })
        {
            var p = plan.Add(Catalog.FlexCurveKey("H0", r, angle));
            p.Flipped = clockwise;
            PlanEditing.PlaceAt(p, 0, at.X, at.Y, at.Heading);
            at = p.WorldConnectors()[1];
        }
        return plan;
    }

    [Fact]
    public void Kleine_Anlage_die_Zentrale_speist_alles_mit_Masse()
    {
        var plan = SamplePlan.Build();
        plan.Feeders.Clear();
        plan.Scenery.RemoveAll(x => x.IsWire);
        var s = Session(plan);
        Assert.Contains(Errors(s.Plan), i => i.Category == "Einspeisung");

        s.AutoWire();
        Assert.Empty(Errors(s.Plan));
        var zentrale = Assert.Single(PowerSupply.Sources(s.Plan), x => PowerSupply.Type(x) == PowerSourceType.Zentrale);
        Assert.DoesNotContain(PowerSupply.Sources(s.Plan), x => PowerSupply.Type(x) == PowerSourceType.Booster);
        Assert.NotEmpty(PowerSupply.FeedersOf(s.Plan, zentrale));
        Assert.NotEmpty(PowerSupply.GroundsOf(s.Plan, zentrale));
        // jede Einspeisung hängt an einem Kabel, das an der Quelle beginnt
        Assert.All(s.Plan.Feeders, f => Assert.Contains(s.Plan.Scenery, w => w.Id == f.WireId));

        // ein zweiter Lauf ersetzt, statt zu verdoppeln; Rückgängig in einem Schritt
        var feeders = s.Plan.Feeders.Count;
        var wires = s.Plan.Scenery.Count(x => x.IsWire);
        s.AutoWire();
        Assert.Equal(feeders, s.Plan.Feeders.Count);
        Assert.Equal(wires, s.Plan.Scenery.Count(x => x.IsWire));
        s.Undo();
        Assert.Equal(feeders, s.Plan.Feeders.Count);
    }

    [Fact]
    public void Mehrere_Ebenen_bekommen_Booster_die_Zentrale_schaltet_nur()
    {
        var plan = SamplePlan.Build();
        plan.Feeders.Clear();
        plan.Scenery.RemoveAll(x => x.IsWire);
        plan.Levels.Add(new PlanLevel { Name = "Ebene 2", Height = 100 });
        // eine Trennstelle teilt die Anlage, der abgetrennte Abschnitt kommt auf Ebene 2
        var spur = plan.Pieces.First(p => p.Def.Number == "24215");
        plan.Isolations.Add(new Isolation { Id = plan.NextId++, PieceId = spur.Id, Index = 0 });
        var upper = Electrics.Sections(plan, Connectivity.Analyze(plan)).Single(x => x.PieceIds.Contains(spur.Id));
        foreach (var id in upper.PieceIds) plan.Find(id)!.Level = 1;
        var s = Session(plan);

        s.AutoWire();
        var boosters = PowerSupply.Sources(s.Plan).Where(x => PowerSupply.Type(x) == PowerSourceType.Booster).ToList();
        Assert.Equal(2, boosters.Count);
        var zentrale = PowerSupply.Sources(s.Plan).Single(x => PowerSupply.Type(x) == PowerSourceType.Zentrale);
        Assert.Empty(PowerSupply.FeedersOf(s.Plan, zentrale));
        Assert.All(boosters, b => Assert.NotEmpty(PowerSupply.FeedersOf(s.Plan, b)));
        Assert.Empty(Errors(s.Plan));
    }

    [Fact]
    public void Ebenen_ohne_Trennstelle_bleiben_ein_Abschnitt_mit_einem_Booster()
    {
        var plan = SamplePlan.Build();
        plan.Feeders.Clear();
        plan.Scenery.RemoveAll(x => x.IsWire);
        plan.Levels.Add(new PlanLevel { Name = "Ebene 2", Height = 100 });
        plan.Pieces.First(p => p.Def.Number == "24215").Level = 1;
        var s = Session(plan);

        s.AutoWire();
        Assert.DoesNotContain(PowerSupply.Sources(s.Plan), x => PowerSupply.Type(x) == PowerSourceType.Booster);
        Assert.Empty(Errors(s.Plan));
    }

    [Fact]
    public void Gleichstrom_Kehrschleife_bekommt_Kehrschleifenmodul_KS1()
    {
        var s = Session(Balloon());
        Assert.Equal(CurrentKind.Gleichstrom, Electrics.Current(s.Plan));
        Assert.Single(Electrics.ReverseLoops(s.Plan));

        s.AutoWire();
        var ks = Assert.Single(PowerSupply.Modules(s.Plan), m => m.Model == "Kehrschleifenmodul");
        Assert.Equal("KS1", ks.Name);
        Assert.NotEmpty(s.Plan.Isolations);
        Assert.Empty(Electrics.ReverseLoops(s.Plan));
        // Schleife über die Ausgänge des Moduls gespeist, Modul oben an der Zentrale
        var ports = Decoders.Ports(ks);
        var loopFeeds = s.Plan.Feeders.Where(f => s.Plan.Scenery.First(w => w.Id == f.WireId).Points[0] is var p0 && ports.Contains(p0)).ToList();
        Assert.Equal(2, loopFeeds.Count);
        var ft = Decoders.FeedTerminals(ks)!.Value;
        Assert.Contains(s.Plan.Scenery, w => w.IsWire && w.Points.Count > 1 && w.Points[^1] == ft.Track);
        Assert.Contains(s.Plan.Scenery, w => w.IsWire && w.Points.Count > 1 && w.Points[^1] == ft.Ground);
        Assert.Empty(Errors(s.Plan));

        // vom Benutzer gesetzte Trennstellen werden genutzt, nicht verdoppelt
        var isolations = s.Plan.Isolations.Count;
        s.AutoWire();
        Assert.Equal(isolations, s.Plan.Isolations.Count);
        Assert.Single(PowerSupply.Modules(s.Plan), m => m.Model == "Kehrschleifenmodul");
    }

    [Fact]
    public void Abschnitt_mit_Melder_wird_ueber_den_Besetztmelder_gespeist()
    {
        var plan = SamplePlan.Build();
        plan.Feeders.Clear();
        plan.Scenery.RemoveAll(x => x.IsWire);
        var spur = plan.Pieces.First(p => p.Def.Number == "24215");
        plan.Isolations.Add(new Isolation { Id = plan.NextId++, PieceId = spur.Id, Index = 0 });
        var s = Session(plan);
        s.ToggleDetectorOn(spur.Id);

        s.AutoWire();
        Assert.True(s.Plan.DetectorWiring);
        var bm = Assert.Single(PowerSupply.Modules(s.Plan), m => m.Model == "Besetztmelder");
        Assert.DoesNotContain(s.Plan.Feeders, f => f.PieceId == spur.Id);
        Assert.NotNull(s.DetectorOn(spur.Id)!.Source);
        Assert.Contains(s.Plan.Scenery, w => DetectorWiring.IsAutoFeed(w) && w.Points[0] == Decoders.FeedTerminals(bm)!.Value.Track);
        Assert.Empty(Errors(s.Plan));
    }
}
