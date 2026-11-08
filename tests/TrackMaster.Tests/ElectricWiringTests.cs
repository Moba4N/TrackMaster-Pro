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
        Assert.Contains("Einspeisung", s.ConnectionSummary(a.Id));
        s.RemoveFeedersOn(a.Id);
        Assert.Empty(s.Plan.Feeders);
        // Die Auswahl bleibt beim Bearbeiten der Anschlüsse
        Assert.Contains(a.Id, s.Selection);
    }

    [Fact]
    public void Kabel_wird_verlegt_gemessen_gespeichert_und_im_Kabelplan_gefuehrt()
    {
        var (plan, a, _) = TwoStraights(false);
        var s = new PlanSession();
        s.Replace(plan, null);
        var f = s.AddFeederTo(a.Id);
        var start = Electrics.FeederPoint(s.Plan.Find(a.Id)!, s.Plan.Gauge.TrackWidth);
        var wire = s.AddWire([start, (start.X, start.Y + 300), (start.X + 400, start.Y + 300)], WireUse.Fahrstrom)!;
        Assert.NotNull(wire);
        Assert.Equal(700, WireLayout.Length(wire), 3);
        Assert.Equal("#D32F2F", WireLayout.ColorOf(wire, CurrentKind.Gleichstrom));
        Assert.Contains("Einspeisung", WireLayout.EndAnchor(s.Plan, wire, false)!.Text);
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
