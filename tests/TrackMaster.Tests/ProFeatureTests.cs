using TrackMaster.Core;

namespace TrackMaster.Tests;

public class ProFeatureTests
{
    [Fact]
    public void Flexgleis_wird_aus_dem_Schluessel_erzeugt()
    {
        var straight = Catalog.Get(Catalog.FlexStraightKey("H0", 437.26));
        Assert.Equal(PieceKind.Straight, straight.Kind);
        Assert.Equal(437.26, straight.Length, 3);
        Assert.True(straight.IsFlex);

        var curve = Catalog.Get(Catalog.FlexCurveKey("N", 520, 22.5));
        Assert.Equal(PieceKind.Curve, curve.Kind);
        Assert.Equal(520, curve.Radius);
        Assert.Equal(22.5, curve.Angle);
        Assert.Same(curve, Catalog.Get(Catalog.FlexCurveKey("N", 520, 22.5)));

        Assert.Null(Catalog.Find("H0|flex|L1"));
        Assert.Null(Catalog.Find("H0|flex|R600A400"));
        Assert.Null(Catalog.Find("XX|flex|L500"));
    }

    [Fact]
    public void Flexgleis_wird_in_der_Stueckliste_in_Stangen_gezaehlt()
    {
        var plan = new LayoutPlan { GaugeId = "H0" };
        plan.Add(Catalog.FlexStraightKey("H0", 800));
        plan.Add(Catalog.FlexCurveKey("H0", 600, 90));
        var rows = BillOfMaterials.Build(plan).Where(r => r.Def.IsFlex).ToList();
        Assert.Single(rows);
        // 800 + 942,5 mm mit 5 % Verschnitt = 1830 mm → 3 Stangen zu 914 mm
        Assert.Equal(3, rows[0].Count);
    }

    [Fact]
    public void Steigung_hebt_eine_Gleisfolge_gleichmaessig()
    {
        var plan = new LayoutPlan { GaugeId = "H0" };
        PlacedPiece? prev = null;
        for (var i = 0; i < 5; i++)
        {
            var p = plan.Add("H0|mc|24188");
            if (prev is null) PlanEditing.PlaceAt(p, 0, 100, 100, 0);
            else { var e = prev.WorldConnectors()[1]; PlanEditing.PlaceAt(p, 0, e.X, e.Y, e.Heading); }
            prev = p;
        }
        var result = PlanEditing.ApplyGradient(plan, plan.Pieces.Select(p => p.Id).ToHashSet(), 10, 2);
        Assert.Equal(5, result.Pieces);
        Assert.Equal(10 + 0.02 * 5 * 188.3, result.EndHeight, 1);
        Assert.Equal(2, plan.Statistics().MaxGrade * 100, 3);
        // Die Gleise bleiben trotz Höhe verbunden
        Assert.Equal(2, Connectivity.Analyze(plan).Open.Count);
    }

    [Fact]
    public void Gleiswendel_verbindet_nur_aufeinanderfolgende_Bogen()
    {
        var plan = new LayoutPlan { GaugeId = "H0", BoardWidth = 3000, BoardHeight = 3000 };
        var pieces = PlanEditing.BuildHelix(plan, "H0|mc|24230", 2, 100, rightHand: false, start: null, center: (1000, 1000));
        Assert.Equal(24, pieces.Count);
        Assert.Equal(200, pieces[^1].ConnectorHeights()[1], 1);
        // Übereinanderliegende Windungen dürfen sich nicht verbinden: genau zwei offene Enden
        var conn = Connectivity.Analyze(plan);
        Assert.Equal(2, conn.Open.Count);
        // 100 mm Höhenabstand reichen für H0 (80 mm), also keine Kollision
        Assert.DoesNotContain(PlanCheck.Run(plan), i => i.Level == IssueLevel.Fehler);
    }

    [Fact]
    public void Planpruefung_findet_ueberlappende_Gleise()
    {
        var plan = new LayoutPlan { GaugeId = "H0", BoardWidth = 2000, BoardHeight = 1000 };
        var a = plan.Add("H0|mc|24188");
        PlanEditing.PlaceAt(a, 0, 500, 500, 0);
        var b = plan.Add("H0|mc|24188");
        PlanEditing.PlaceAt(b, 0, 580, 505, 0);
        var issues = PlanCheck.Run(plan);
        Assert.Contains(issues, i => i.Level == IssueLevel.Fehler && i.PieceIds.Contains(a.Id) && i.PieceIds.Contains(b.Id));

        // Mit genug Höhenabstand ist es eine Überführung
        b.Height = 120;
        Assert.DoesNotContain(PlanCheck.Run(plan), i => i.Level == IssueLevel.Fehler && i.PieceIds.Contains(a.Id) && i.PieceIds.Contains(b.Id));
    }

    [Fact]
    public void Beispielplan_hat_keine_Fehler()
    {
        var plan = SamplePlan.Build();
        Assert.DoesNotContain(PlanCheck.Run(plan), i => i.Level == IssueLevel.Fehler);
        Assert.NotEmpty(plan.Scenery);
        Assert.NotEmpty(plan.Signals);
    }

    [Fact]
    public void Gleis_im_Gebaeude_wird_gemeldet()
    {
        var plan = SamplePlan.Build();
        var p = plan.Pieces[1];
        var m = p.Middle();
        plan.Scenery.Add(new SceneryItem { Id = plan.NextId++, Kind = SceneryKind.Building, Name = "Im Weg", X = m.X, Y = m.Y, Width = 120, Depth = 80, Height = 60 });
        Assert.Contains(PlanCheck.Run(plan), i => i.Category == "Gebäude" && i.PieceIds.Contains(p.Id));
    }

    [Fact]
    public void Trennstellen_teilen_Stromabschnitte()
    {
        var plan = SamplePlan.Build();
        Assert.Single(Electrics.Sections(plan));
        // Das Abstellgleis am ersten Prellbock abtrennen
        var spur = plan.Pieces.First(p => p.Def.Number == "24215");
        plan.Isolations.Add(new Isolation { Id = plan.NextId++, PieceId = spur.Id, Index = 0 });
        var sections = Electrics.Sections(plan);
        Assert.Equal(2, sections.Count);
        Assert.Contains(sections, s => s.PieceIds.Contains(spur.Id) && s.Feeders.Count == 0);
        Assert.Contains(Electrics.Analyze(plan).Issues, i => i.PieceIds.Contains(spur.Id));
    }

    [Fact]
    public void Kehrschleife_wird_im_Zweileiter_System_erkannt()
    {
        var plan = new LayoutPlan { GaugeId = "H0", BoardWidth = 3000, BoardHeight = 3000 };
        var sw = plan.Add("H0|geo|WL10");
        PlanEditing.PlaceAt(sw, 0, 1000, 2000, 0);
        var at = sw.WorldConnectors()[1];
        // Ballonschleife vom geraden Strang zurück in den Abzweig
        foreach (var (r, angle, clockwise) in new[] { (500.0, 40.79, true), (400, 150.155, false), (400, 150.155, false), (300, 69.52, true) })
        {
            var p = plan.Add(Catalog.FlexCurveKey("H0", r, angle));
            p.Flipped = clockwise;
            PlanEditing.PlaceAt(p, 0, at.X, at.Y, at.Heading);
            at = p.WorldConnectors()[1];
        }
        Assert.Single(Connectivity.Analyze(plan).Open);
        var loops = Electrics.ReverseLoops(plan);
        Assert.Single(loops);
        Assert.Contains(Electrics.Analyze(plan).Issues, i => i.Category.Contains("Kehrschleife"));

        // Ein Oval ist keine Kehrschleife, und Märklin-Mittelleiter kennt das Problem nicht
        Assert.Empty(Electrics.ReverseLoops(SamplePlan.Build()));
    }

    [Fact]
    public void Adressen_werden_fortlaufend_und_ohne_Konflikt_vergeben()
    {
        var plan = SamplePlan.Build();
        var n = Electrics.AssignAddresses(plan, 1, onlyMissing: false);
        Assert.Equal(2 + plan.Signals.Count, n);
        var items = Electrics.SwitchingItems(plan);
        Assert.All(items, i => Assert.NotNull(i.Address));
        Assert.Empty(Electrics.AddressConflicts(items));
        // Hauptsignale belegen zwei Adressen
        var used = items.Sum(i => i.AddressCount);
        Assert.Equal(used, items.Max(i => i.Address!.Value + i.AddressCount - 1));

        plan.Signals[0].Address = plan.Pieces.First(p => p.Def.Kind == PieceKind.Switch).Address;
        Assert.NotEmpty(Electrics.AddressConflicts(Electrics.SwitchingItems(plan)));
    }

    [Fact]
    public void Gelaende_Elektrik_und_Hoehen_werden_gespeichert()
    {
        var plan = SamplePlan.Build();
        plan.Pieces[3].Height = 42.5;
        plan.Pieces[3].Grade = 0.015;
        plan.Pieces[3].Tunnel = true;
        plan.Pieces[0].Address = 7;
        plan.Isolations.Add(new Isolation { Id = plan.NextId++, PieceId = plan.Pieces[20].Id, Index = 0 });
        var back = PlanSerializer.FromJson(PlanSerializer.ToJson(plan), out var skipped);
        Assert.Equal(0, skipped);
        Assert.Equal(plan.Scenery.Count, back.Scenery.Count);
        Assert.Equal(plan.Scenery[0].Points.Count, back.Scenery[0].Points.Count);
        Assert.Equal(plan.Signals.Select(s => (s.PieceId, s.Index, s.Type, s.Name)), back.Signals.Select(s => (s.PieceId, s.Index, s.Type, s.Name)));
        Assert.Equal(plan.Feeders.Select(f => f.PieceId), back.Feeders.Select(f => f.PieceId));
        Assert.Equal(plan.Isolations.Select(i => (i.PieceId, i.Index)), back.Isolations.Select(i => (i.PieceId, i.Index)));
        var p = back.Find(plan.Pieces[3].Id)!;
        Assert.Equal(42.5, p.Height);
        Assert.Equal(0.015, p.Grade);
        Assert.True(p.Tunnel);
        Assert.Equal(7, back.Find(plan.Pieces[0].Id)!.Address);
        // Neue Objekte bekommen keine schon vergebenen Nummern
        Assert.True(back.NextId > back.Scenery.Max(s => s.Id));
    }
}
