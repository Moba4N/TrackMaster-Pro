using TrackMaster.Core;

namespace TrackMaster.Tests;

/// <summary>Stromart, Polung, Anschlüsse am Gleis, selbst verlegte Kabel und der Sprung zu Befunden.</summary>
public class ElectricWiringTests
{
    /// <summary>Zwei Flexgeraden (Zweileiter) hintereinander; die zweite auf Wunsch andersherum gelegt.</summary>
    private static (LayoutPlan Plan, PlacedPiece A, PlacedPiece B) TwoStraights(bool secondReversed)
    {
        var plan = new LayoutPlan { GaugeId = "H0", BoardWidth = 2000, BoardHeight = 1000 };
        var a = plan.Add(Catalog.FlexStraightKey("H0", 400));
        PlanEditing.PlaceAt(a, 0, 100, 500, 180);
        var end = a.WorldConnectors()[1];
        var b = plan.Add(Catalog.FlexStraightKey("H0", 400));
        PlanEditing.PlaceAt(b, secondReversed ? 1 : 0, end.X, end.Y, end.Heading);
        Assert.DoesNotContain(Connectivity.Analyze(plan).Open, c => c.PieceId == a.Id && c.Index == 1);
        return (plan, a, b);
    }

    [Fact]
    public void Stromart_folgt_dem_Gleis_oder_der_Einstellung()
    {
        var sample = SamplePlan.Build();
        Assert.Equal(CurrentKind.Wechselstrom, Electrics.Current(sample));
        Assert.DoesNotContain(Electrics.Analyze(sample).Issues, i => i.Category == "Stromart");

        sample.Current = CurrentKind.Gleichstrom;
        Assert.Contains(Electrics.Analyze(sample).Issues, i => i.Category == "Stromart" && i.Level == IssueLevel.Warnung);

        var (dc, _, _) = TwoStraights(false);
        Assert.Equal(CurrentKind.Gleichstrom, Electrics.Current(dc));
        dc.Current = CurrentKind.Wechselstrom;
        Assert.Contains(Electrics.Analyze(dc).Issues, i => i.Category == "Stromart" && i.Level == IssueLevel.Fehler);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Polung_wird_im_Abschnitt_geprueft(bool secondReversed)
    {
        var (plan, a, b) = TwoStraights(secondReversed);
        plan.Feeders.Add(new Feeder { Id = plan.NextId++, PieceId = a.Id });
        var fb = new Feeder { Id = plan.NextId++, PieceId = b.Id };
        plan.Feeders.Add(fb);
        var sections = Electrics.Sections(plan);
        Assert.Single(sections);
        // Liegt das zweite Gleis andersherum, ist „normal“ dort die andere Schiene
        Assert.Equal(secondReversed, Electrics.PolarityConflicts(plan, sections).Count > 0);
        Assert.Equal(secondReversed, Electrics.Analyze(plan).Issues.Any(i => i.Category == "Polung"));
        fb.Reversed = !fb.Reversed;
        Assert.Equal(!secondReversed, Electrics.PolarityConflicts(plan, sections).Count > 0);
    }

    [Fact]
    public void Neue_Einspeisung_bekommt_die_passende_Polung()
    {
        var (plan, a, b) = TwoStraights(true);
        var s = new PlanSession();
        s.Replace(plan, null);
        s.AddFeederTo(a.Id);
        var f = s.AddFeederTo(b.Id, "Booster 2");
        Assert.True(f.Reversed);
        Assert.DoesNotContain(Electrics.Analyze(s.Plan).Issues, i => i.Category == "Polung");
        Assert.Contains("Booster 2", s.CircuitNames);
        Assert.Contains(Electrics.Analyze(s.Plan).Issues, i => i.Category == "Stromkreise");
    }

    [Fact]
    public void Anschluesse_am_Gleis_setzen_und_entfernen()
    {
        var (plan, a, b) = TwoStraights(false);
        var s = new PlanSession();
        s.Replace(plan, null);
        s.Select([a.Id], add: false);
        Assert.Same(s.Plan.Find(a.Id), s.ConnectionPiece);

        s.ToggleAllIsolations(a.Id);
        Assert.Single(s.PieceEnds(a.Id), e => e.Isolated);
        Assert.Contains(s.PieceEnds(b.Id), e => e.Isolated);
        Assert.Equal(2, Electrics.Sections(s.Plan).Count);
        s.ToggleAllIsolations(a.Id);
        Assert.Empty(s.Plan.Isolations);

        s.ToggleDetectorOn(a.Id);
        Assert.NotNull(s.DetectorOn(a.Id));
        s.ToggleDetectorOn(a.Id);
        Assert.Null(s.DetectorOn(a.Id));

        s.AddFeederTo(a.Id);
        Assert.Contains("Gleisanschluss", s.ConnectionSummary(a.Id));
        s.RemoveFeedersOn(a.Id);
        Assert.Empty(s.Plan.Feeders);
        // Die Auswahl bleibt beim Bearbeiten der Anschlüsse
        Assert.Contains(a.Id, s.Selection);
    }

    [Fact]
    public void Gleisanschluss_liegt_bei_Gleichstrom_an_einer_Schiene_und_bei_Wechselstrom_in_der_Mitte()
    {
        var (plan, a, _) = TwoStraights(false);
        var g = plan.Gauge.TrackWidth;
        var m = a.Middle();
        double Off((double X, double Y) q) => Math.Sqrt((q.X - m.X) * (q.X - m.X) + (q.Y - m.Y) * (q.Y - m.Y));
        var dc = Electrics.FeederPoint(a, g, CurrentKind.Gleichstrom);
        var dcReversed = Electrics.FeederPoint(a, g, CurrentKind.Gleichstrom, reversed: true);
        Assert.Equal(g / 2, Off(dc), 6);
        Assert.Equal(g / 2, Off(dcReversed), 6);
        Assert.Equal(g, Math.Sqrt((dc.X - dcReversed.X) * (dc.X - dcReversed.X) + (dc.Y - dcReversed.Y) * (dc.Y - dcReversed.Y)), 6);
        Assert.Equal(0, Off(Electrics.FeederPoint(a, g, CurrentKind.Wechselstrom)), 6);
    }

    [Fact]
    public void Kabel_wird_verlegt_gemessen_gespeichert_und_im_Kabelplan_gefuehrt()
    {
        var (plan, a, _) = TwoStraights(false);
        var s = new PlanSession();
        s.Replace(plan, null);
        var f = s.AddFeederTo(a.Id);
        var start = Electrics.FeederPoint(s.Plan, s.Plan.Find(a.Id)!, f);
        var wire = s.AddWire([start, (start.X, start.Y + 300), (start.X + 400, start.Y + 300)], WireUse.Fahrstrom)!;
        Assert.NotNull(wire);
        Assert.Equal(700, WireLayout.Length(wire), 3);
        Assert.Equal("#D32F2F", WireLayout.ColorOf(wire, CurrentKind.Gleichstrom));
        Assert.Contains("Gleisanschluss", WireLayout.EndAnchor(s.Plan, wire, false)!.Text);
        Assert.Null(WireLayout.EndAnchor(s.Plan, wire, true));

        var text = Electrics.WiringPlan(s.Plan);
        Assert.Contains("Kabel 1", text);
        Assert.Contains("Gleichstrom", text);
        Assert.Contains(BillOfMaterials.Accessories(s.Plan), r => r.Name.StartsWith("Kabel rot") && r.Count == 1);

        s.SetWireUse(wire.Id, WireUse.Rueckleiter);
        Assert.Equal("#1E5BD8", WireLayout.ColorOf(wire, CurrentKind.Gleichstrom));

        s.Plan.Current = CurrentKind.Gleichstrom;
        s.Plan.Feeders[0].Reversed = true;
        var copy = PlanSerializer.FromJson(PlanSerializer.ToJson(s.Plan), out _);
        Assert.Equal(CurrentKind.Gleichstrom, copy.Current);
        Assert.True(copy.Feeders[0].Reversed);
        var w2 = Assert.Single(copy.Scenery, x => x.IsWire);
        Assert.Equal(3, w2.Points.Count);
        Assert.Equal(WireUse.Rueckleiter, WireLayout.Use(w2));
        Assert.Equal(wire.Color, w2.Color);
        Assert.Equal(wire.Depth, w2.Depth);

        // Ein Kabel ohne Anschluss an beiden Enden ist ein Hinweis
        s.AddWire([(1500, 900), (1700, 900)]);
        Assert.Contains(Electrics.Analyze(s.Plan).Issues, i => i.Category == "Kabel" && i.ObjectId is not null);
    }

    [Fact]
    public void Befund_blendet_die_Ebene_ein_und_waehlt_die_Gleise()
    {
        var plan = SamplePlan.Build();
        plan.Levels.Add(new PlanLevel { Name = "Oben", Height = 100, Visible = false });
        var piece = plan.Pieces[3];
        piece.Level = 1;
        var s = new PlanSession();
        s.Replace(plan, null);
        var issue = new PlanIssue(IssueLevel.Fehler, "Test", "Test", [piece.Id], piece.X, piece.Y);
        s.Issues = [issue];
        var (x0, y0, x1, y1) = s.FocusIssue(issue);
        Assert.True(s.Plan.Levels[1].Visible);
        Assert.Equal([piece.Id], s.Selection);
        Assert.Equal([piece.Id], s.FocusPieces);
        Assert.True(x1 - x0 >= s.Plan.Gauge.TrackWidth * 25 - 1e-6 && y1 - y0 >= s.Plan.Gauge.TrackWidth * 25 - 1e-6);
        s.Issues = [];
        Assert.Empty(s.FocusPieces);
    }

    [Fact]
    public void Booster_braucht_gemeinsame_Masse_ueber_Verteiler()
    {
        var (plan, a, b) = TwoStraights(false);
        var s = new PlanSession();
        s.Replace(plan, null);
        var central = s.AddPowerSource(PowerSourceType.Zentrale, 200, 800);
        var booster = s.AddPowerSource(PowerSourceType.Booster, 600, 800);
        s.AddFeederTo(a.Id, central.Name);
        s.AddFeederTo(b.Id, booster.Name);
        var g1 = PowerSupply.GroundTerminal(central)!.Value;
        var g2 = PowerSupply.GroundTerminal(booster)!.Value;
        Assert.Equal(2, Electrics.Analyze(s.Plan).Issues.Count(i => i.Text.Contains("Masse (0)")));

        // Masse jeweils an ein eigenes Kabelende: angeschlossen, aber nicht verbunden
        s.AddWire([g1, (g1.X, g1.Y + 100)], WireUse.Rueckleiter);
        s.AddWire([g2, (g2.X, g2.Y + 100)], WireUse.Rueckleiter);
        Assert.Contains(Electrics.Analyze(s.Plan).Issues, i => i.Text.Contains("gemeinsame Masse") && i.ObjectId == booster.Id);

        // brauner Verteiler verbindet beide
        s.DistributorColor = "#795548";
        var dist = s.AddPowerSource(PowerSourceType.Verteiler, 400, 1100);
        Assert.Empty(PowerSupply.Sources(s.Plan).Where(x => x.Id == dist.Id));
        var clamps = PowerSupply.ClampPoints(dist);
        s.AddWire([(g1.X, g1.Y + 100), clamps[0]], WireUse.Rueckleiter);
        s.AddWire([(g2.X, g2.Y + 100), clamps[^1]], WireUse.Rueckleiter);
        var issues = Electrics.Analyze(s.Plan).Issues;
        Assert.DoesNotContain(issues, i => i.Text.Contains("gemeinsame Masse") || i.Text.Contains("Masse (0)"));
        Assert.Contains("Masse angeschlossen", Electrics.WiringPlan(s.Plan));

        // rotes Kabel am braunen Verteiler gibt einen Hinweis
        s.AddWire([clamps[3], (clamps[3].X, clamps[3].Y + 80)], WireUse.Fahrstrom);
        Assert.Contains(Electrics.Analyze(s.Plan).Issues, i => i.Level == IssueLevel.Hinweis && i.Text.Contains(dist.Name));

        // Farbe bleibt beim Speichern erhalten
        s.SetDistributorColor(dist.Id, "#F2C200");
        var loaded = PlanSerializer.FromJson(PlanSerializer.ToJson(s.Plan), out _);
        var back = Assert.Single(PowerSupply.Distributors(loaded));
        Assert.Equal("#F2C200", PowerSupply.DistributorColor(back));
    }

    [Fact]
    public void Gleisanschluss_legt_Zentrale_bzw_Trafo_als_Symbol_an()
    {
        Assert.DoesNotContain(Electrics.Analyze(SamplePlan.Build()).Issues, i => i.Category == "Stromversorgung");

        var (plan, a, b) = TwoStraights(false);
        var s = new PlanSession();
        s.Replace(plan, null);
        var f = s.AddFeederTo(a.Id);
        var central = Assert.Single(PowerSupply.Sources(s.Plan));
        Assert.Equal(PowerSourceType.Zentrale, PowerSupply.Type(central));
        Assert.Equal(central.Name, f.Circuit);
        Assert.Same(central, PowerSupply.SourceOf(s.Plan, f));
        Assert.Contains(WireLayout.Anchors(s.Plan), x => x.Text.StartsWith(central.Name));
        // ohne Masse meldet die Prüfung die Zentrale, mit braunem Kabel zum Gleisanschluss nicht mehr
        var issue = Assert.Single(Electrics.Analyze(s.Plan).Issues, i => i.Category == "Stromversorgung");
        Assert.Contains("Masse", issue.Text);
        var gt = PowerSupply.GroundTerminal(central)!.Value;
        Assert.NotNull(s.AddWire([gt, Electrics.FeederPoint(s.Plan, a, f)], WireUse.Rueckleiter));
        Assert.DoesNotContain(Electrics.Analyze(s.Plan).Issues, i => i.Category == "Stromversorgung");

        // Umbenennen nimmt die Gleisanschlüsse mit
        s.RenamePowerSource(central.Id, "CS3");
        Assert.Equal("CS3", s.Plan.Feeders[0].Circuit);

        // Analogbetrieb: eine Zentrale passt nicht, neue Anschlüsse in einem neuen Stromkreis bekommen einen Trafo
        s.SetOperation(PowerMode.Analog);
        Assert.Contains(Electrics.Analyze(s.Plan).Issues, i => i.Category == "Stromversorgung" && i.ObjectId == central.Id);
        var f2 = s.AddFeederTo(b.Id, s.NewCircuitName());
        Assert.Equal(PowerSourceType.Fahrtrafo, PowerSupply.Type(PowerSupply.SourceOf(s.Plan, f2)!));
        Assert.Contains("Stromversorgung (Analog)", Electrics.WiringPlan(s.Plan));

        // Zubehörtrafo darf kein Gleis speisen
        var light = s.AddPowerSource(PowerSourceType.Zubehoertrafo, 100, 900);
        s.SetFeederCircuit(f2.Id, light.Name);
        Assert.Contains(Electrics.Analyze(s.Plan).Issues, i => i.Category == "Stromversorgung" && i.Level == IssueLevel.Fehler);

        var copy = PlanSerializer.FromJson(PlanSerializer.ToJson(s.Plan), out _);
        Assert.Equal(PowerMode.Analog, copy.Operation);
        Assert.Equal(3, PowerSupply.Sources(copy).Count());
        Assert.Contains(PowerSupply.Sources(copy), x => PowerSupply.Type(x) == PowerSourceType.Zubehoertrafo && x.Width > 0);
    }

    [Fact]
    public void Markierte_Elektrik_blendet_Hoehen_und_Artikelnummern_aus()
    {
        var s = new PlanSession { ShowHeights = true, ShowLabels = true };
        Assert.True(s.HeightsVisible && s.LabelsVisible);
        s.ShowSections = true;
        Assert.False(s.HeightsVisible || s.LabelsVisible);
        s.ElectricLabelsOnly = false;
        Assert.True(s.HeightsVisible && s.LabelsVisible);
    }
}
