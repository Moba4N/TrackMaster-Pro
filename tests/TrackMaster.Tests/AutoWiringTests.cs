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
        // zu jeder Bahnstrom-Einspeisung führt auch ein Massekabel ans selbe Gleis
        Assert.All(s.Plan.Feeders.Where(f => !f.Ground), f => Assert.Contains(s.Plan.Feeders, g => g.Ground && g.PieceId == f.PieceId && g.WireId is not null));

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
    public void Zweileiter_Abschnitt_mit_Melder_wird_ueber_den_Besetztmelder_gespeist()
    {
        // Zweileiter-Strecke aus Flexgleis, Trennstelle vor dem letzten Stück
        var plan = new LayoutPlan { GaugeId = "H0", BoardWidth = 4000, BoardHeight = 1000 };
        double x = 100, y = 500, h = 180;
        for (var i = 0; i < 4; i++)
        {
            var p = plan.Add(Catalog.FlexStraightKey("H0", 400));
            PlanEditing.PlaceAt(p, 0, x, y, h);
            var end = p.WorldConnectors()[1];
            (x, y, h) = (end.X, end.Y, end.Heading);
        }
        var spur = plan.Pieces[^1];
        plan.Isolations.Add(new Isolation { Id = plan.NextId++, PieceId = spur.Id, Index = 0 });
        var s = Session(plan);
        Assert.False(AutoWiring.DetectSystem(s.Plan).ThreeRail);
        s.ToggleDetectorOn(spur.Id);

        s.AutoWire();
        Assert.True(s.Plan.DetectorWiring);
        var bm = Assert.Single(PowerSupply.Modules(s.Plan), m => m.Model == "Besetztmelder");
        Assert.DoesNotContain(s.Plan.Feeders, f => f.PieceId == spur.Id && !f.Ground);
        // Masse des Melderabschnitts per Kabel von der Quelle ans Gleis
        var g = Assert.Single(s.Plan.Feeders, f => f.PieceId == spur.Id && f.Ground);
        Assert.Contains(s.Plan.Scenery, w => w.Id == g.WireId);
        Assert.NotNull(s.DetectorOn(spur.Id)!.Source);
        Assert.Contains(s.Plan.Scenery, w => DetectorWiring.IsAutoFeed(w) && w.Points[0] == Decoders.FeedTerminals(bm)!.Value.Track);
        Assert.Empty(Errors(s.Plan));
    }

    [Fact]
    public void Gleissystem_bestimmt_die_Verkabelung_Mittelleiter()
    {
        var plan = SamplePlan.Build();
        plan.Feeders.Clear();
        plan.Scenery.RemoveAll(x => x.IsWire);
        plan.Current = CurrentKind.Gleichstrom; // falsch eingestellt
        var s = Session(plan);
        string? msg = null;
        s.Message += m => msg = m;

        s.AutoWire();
        var (name, threeRail) = AutoWiring.DetectSystem(s.Plan);
        Assert.True(threeRail);
        Assert.Equal(CurrentKind.Auto, s.Plan.Current);
        Assert.Equal(CurrentKind.Wechselstrom, Electrics.Current(s.Plan));
        Assert.Contains(name, msg);
        Assert.Contains("Mittelleiter", msg);
        // Mittelleiter: keine Polung, keine Kehrschleifenmodule
        Assert.All(s.Plan.Feeders.Where(f => !f.Ground), f => Assert.False(f.Reversed));
        Assert.DoesNotContain(PowerSupply.Modules(s.Plan), m => m.Model == "Kehrschleifenmodul");
        Assert.Empty(Errors(s.Plan));
    }

    [Fact]
    public void Mittelleiter_Melderabschnitt_wird_normal_gespeist()
    {
        var plan = SamplePlan.Build();
        plan.Feeders.Clear();
        plan.Scenery.RemoveAll(x => x.IsWire);
        var spur = plan.Pieces.First(p => p.Def.Number == "24215");
        plan.Isolations.Add(new Isolation { Id = plan.NextId++, PieceId = spur.Id, Index = 0 });
        var s = Session(plan);
        s.ToggleDetectorOn(spur.Id);

        s.AutoWire();
        var section = Electrics.Sections(s.Plan, Connectivity.Analyze(s.Plan)).Single(x => x.PieceIds.Contains(spur.Id));
        Assert.Contains(s.Plan.Feeders, f => !f.Ground && section.PieceIds.Contains(f.PieceId) && f.Id > 0);
        Assert.Equal("", s.DetectorOn(spur.Id)!.Source);
        Assert.Empty(Errors(s.Plan));
    }

    [Fact]
    public void Melder_werden_je_Abschnitt_selbst_gesetzt()
    {
        var plan = SamplePlan.Build();
        plan.Feeders.Clear();
        plan.Scenery.RemoveAll(x => x.IsWire);
        plan.Detectors.Clear();
        var spur = plan.Pieces.First(p => p.Def.Number == "24215");
        plan.Isolations.Add(new Isolation { Id = plan.NextId++, PieceId = spur.Id, Index = 0 });
        var s = Session(plan);
        var sections = Electrics.Sections(s.Plan, Connectivity.Analyze(s.Plan));
        Assert.Equal(2, sections.Count);

        s.AutoWire();
        Assert.Equal(2, s.Plan.Detectors.Count);
        Assert.All(sections, sec => Assert.Single(s.Plan.Detectors, d => sec.PieceIds.Contains(d.PieceId)));
        Assert.All(s.Plan.Detectors, d => Assert.True(d.Auto));
        Assert.Equal(["Melder 1", "Melder 2"], s.Plan.Detectors.Select(d => d.Name).Order());
        Assert.Empty(Errors(s.Plan));

        // erneuter Lauf ersetzt, gespeichert bleibt die Kennung
        s.AutoWire();
        Assert.Equal(2, s.Plan.Detectors.Count);
        var loaded = PlanSerializer.FromJson(PlanSerializer.ToJson(s.Plan), out _);
        Assert.All(loaded.Detectors, d => Assert.True(d.Auto));
    }

    [Fact]
    public void Ohne_Trennstellen_keine_Melder()
    {
        var plan = SamplePlan.Build();
        plan.Detectors.Clear();
        var s = Session(plan);
        s.AutoWire();
        Assert.Empty(s.Plan.Detectors);
    }

    [Fact]
    public void Viele_Trennstellen_auf_einer_Ebene_bleiben_an_der_Zentrale()
    {
        // lange Anlage auf einer Ebene, jedes zweite Gleis getrennt
        var plan = new LayoutPlan { GaugeId = "H0", BoardWidth = 40000, BoardHeight = 1000 };
        double x = 100, y = 500, h = 180;
        for (var i = 0; i < 40; i++)
        {
            var p = plan.Add(Catalog.FlexStraightKey("H0", 900));
            PlanEditing.PlaceAt(p, 0, x, y, h);
            var end = p.WorldConnectors()[1];
            (x, y, h) = (end.X, end.Y, end.Heading);
            if (i % 2 == 1) plan.Isolations.Add(new Isolation { Id = plan.NextId++, PieceId = p.Id, Index = 0 });
        }
        var s = Session(plan);
        Assert.True(Electrics.Sections(s.Plan, Connectivity.Analyze(s.Plan)).Count >= 20);

        s.AutoWire();
        Assert.DoesNotContain(PowerSupply.Sources(s.Plan), x => PowerSupply.Type(x) == PowerSourceType.Booster);
        Assert.Empty(Errors(s.Plan));
    }

    [Fact]
    public void Verkabelung_entfernen_nimmt_alles_Automatische_heraus()
    {
        // Gleichstrom-Kehrschleife: Trennstellen, KS-Modul, Kabel, Einspeisungen
        var s = Session(Balloon());
        var userIso = s.Plan.Isolations.Count;
        s.AutoWire();
        Assert.NotEmpty(s.Plan.Feeders);
        Assert.Contains(s.Plan.Isolations, i => i.Auto);
        Assert.True(PlanSerializer.FromJson(PlanSerializer.ToJson(s.Plan), out _).Isolations.Any(i => i.Auto));

        s.RemoveAutoWiring();
        Assert.Empty(s.Plan.Feeders);
        Assert.DoesNotContain(s.Plan.Scenery, x => x.IsWire || x.IsPower);
        Assert.Equal(userIso, s.Plan.Isolations.Count);
        Assert.Empty(s.Plan.Detectors);

        // Rückgängig holt alles in einem Schritt zurück
        s.Undo();
        Assert.NotEmpty(s.Plan.Feeders);
        Assert.Single(PowerSupply.Modules(s.Plan), m => m.Model == "Kehrschleifenmodul");
    }

    [Fact]
    public void Verkabelung_entfernen_laesst_Melder_und_Kabel_von_Hand_stehen()
    {
        var plan = SamplePlan.Build();
        plan.Isolations.Add(new Isolation { Id = plan.NextId++, PieceId = plan.Pieces.First(p => p.Def.Number == "24215").Id, Index = 0 });
        var s = Session(plan);
        var manualFeeders = s.Plan.Feeders.Count;
        var manualWires = s.Plan.Scenery.Count(x => x.IsWire);
        var manualDetectors = s.Plan.Detectors.Count;
        s.AutoWire();
        Assert.Contains(s.Plan.Detectors, d => d.Auto);

        s.RemoveAutoWiring();
        Assert.DoesNotContain(s.Plan.Detectors, d => d.Auto);
        Assert.Equal(manualDetectors, s.Plan.Detectors.Count);
        Assert.Equal(manualFeeders, s.Plan.Feeders.Count);
        Assert.False(s.Plan.DetectorWiring);
        Assert.Single(s.Plan.Isolations);
        Assert.True(s.Plan.Scenery.Count(x => x.IsWire) <= manualWires);
    }

    [Fact]
    public void Verbindungen_zum_Gleis_entfernen_nimmt_alle_Anschluesse_heraus()
    {
        var plan = SamplePlan.Build();
        plan.Isolations.Add(new Isolation { Id = plan.NextId++, PieceId = plan.Pieces.First(p => p.Def.Number == "24215").Id, Index = 0 });
        var s = Session(plan);
        Assert.NotEmpty(s.Plan.Feeders); // von Hand gelegte Einspeisung im Beispielplan
        s.AutoWire();
        var sources = PowerSupply.Sources(s.Plan).Count();
        var detectors = s.Plan.Detectors.Count;

        s.RemoveTrackConnections();
        Assert.Empty(s.Plan.Feeders);
        // auch das Massekabel des Beispielplans (ohne eigene Einspeisung, Ende auf dem Gleis) ist weg
        Assert.DoesNotContain(s.Plan.Scenery, w => w.IsWire && (WireUse)w.Variant is WireUse.Fahrstrom or WireUse.Rueckleiter && !DetectorWiring.IsGenerated(w));
        Assert.Equal(sources, PowerSupply.Sources(s.Plan).Count());
        Assert.Equal(detectors, s.Plan.Detectors.Count);
        Assert.Single(s.Plan.Isolations);

        s.Undo();
        Assert.NotEmpty(s.Plan.Feeders);
    }
}
