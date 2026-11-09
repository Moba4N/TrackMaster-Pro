using TrackMaster.Core;

namespace TrackMaster.Tests;

/// <summary>Einspeisung und Masse entstehen über „Kabel von hier verlegen“ an Zentrale bzw. Booster.</summary>
public class FeedFromSourceTests
{
    private static (PlanSession S, PlacedPiece A, PlacedPiece B, SceneryItem Src) Setup()
    {
        var plan = new LayoutPlan { GaugeId = "H0", BoardWidth = 2000, BoardHeight = 1000 };
        var a = plan.Add(Catalog.FlexStraightKey("H0", 400));
        PlanEditing.PlaceAt(a, 0, 100, 500, 180);
        var end = a.WorldConnectors()[1];
        var b = plan.Add(Catalog.FlexStraightKey("H0", 400));
        PlanEditing.PlaceAt(b, 0, end.X, end.Y, end.Heading);
        var s = new PlanSession();
        s.Replace(plan, null);
        var src = s.AddPowerSource(PowerSourceType.Zentrale, 300, 150);
        return (s, s.Plan.Find(a.Id)!, s.Plan.Find(b.Id)!, src);
    }

    /// <summary>Kabel von der Klemme der Quelle zu einem Punkt auf der Schiene.</summary>
    private static SceneryItem Lay(PlanSession s, SceneryItem src, PlacedPiece p, bool ground, bool otherRail = false, double at = 0.5)
    {
        var start = s.WireStart(src.Id, ground)!.Value;
        var rail = Electrics.FeederPoint(p, s.Plan.Gauge.TrackWidth, CurrentKind.Gleichstrom, reversed: otherRail, at: at);
        return s.AddWire([start, (start.X, rail.Y), rail], WireUse.Fahrstrom)!;
    }

    [Fact]
    public void Kabel_von_der_Zentrale_auf_das_Gleis_legt_eine_Einspeisung_an()
    {
        var (s, a, _, src) = Setup();
        Assert.Empty(s.Plan.Feeders);
        var wire = Lay(s, src, a, ground: false);
        var f = Assert.Single(s.Plan.Feeders);
        Assert.Equal(a.Id, f.PieceId);
        Assert.False(f.Ground);
        Assert.Equal(src.Name, f.Circuit);
        Assert.Equal(wire.Id, f.WireId);
        // Kabelende liegt genau am Anschlusspunkt
        var fp = Electrics.FeederPoint(s.Plan, a, f);
        Assert.Equal(fp.X, wire.Points[^1].X, 3);
        Assert.Equal(fp.Y, wire.Points[^1].Y, 3);
        Assert.Single(PowerSupply.FeedersOf(s.Plan, src));
        Assert.Empty(PowerSupply.GroundsOf(s.Plan, src));
    }

    [Fact]
    public void Masse_Kabel_legt_einen_Masseanschluss_an_und_Elektrik_pruefen_ist_zufrieden()
    {
        var (s, a, _, src) = Setup();
        Lay(s, src, a, ground: false);
        // Gleichstrom ohne Masse am Gleis: Fehler
        Assert.Contains(Electrics.Analyze(s.Plan).Issues, i => i.Category == "Masse" && i.Level == IssueLevel.Fehler);
        Lay(s, src, a, ground: true, otherRail: true);
        Assert.Contains(s.Plan.Feeders, f => f.Ground && f.Circuit == src.Name);
        Assert.Single(PowerSupply.GroundsOf(s.Plan, src));
        var issues = Electrics.Analyze(s.Plan).Issues;
        Assert.DoesNotContain(issues, i => i.Category == "Masse");
        Assert.DoesNotContain(issues, i => i.Category == "Einspeisung");
        Assert.DoesNotContain(issues, i => i.Category == "Polung");
    }

    [Fact]
    public void Bahnstrom_und_Masse_an_derselben_Schiene_ist_ein_Kurzschluss()
    {
        var (s, a, _, src) = Setup();
        Lay(s, src, a, ground: false);
        Lay(s, src, a, ground: true, at: 0.3);
        Assert.Contains(Electrics.Analyze(s.Plan).Issues, i => i.Category == "Polung" && i.Level == IssueLevel.Fehler);
    }

    [Fact]
    public void Masse_Kabel_auf_dem_Bahnstrom_Kabel_ist_ein_Kurzschluss()
    {
        var (s, a, _, src) = Setup();
        var wire = Lay(s, src, a, ground: false);
        var start = s.WireStart(src.Id, true)!.Value;
        var end = wire.Points[^1];
        s.AddWire([start, (start.X, end.Y), end], WireUse.Rueckleiter);
        Assert.Single(s.Plan.Feeders);
        Assert.Contains(Electrics.Analyze(s.Plan).Issues, i => i.Category == "Stromversorgung" && i.Level == IssueLevel.Fehler && i.Text.Contains("Kurzschluss"));
    }

    [Fact]
    public void Ein_Gleis_kann_mehrere_Einspeisungen_haben()
    {
        var (s, a, b, src) = Setup();
        Lay(s, src, a, ground: false, at: 0.2);
        Lay(s, src, a, ground: false, at: 0.8);
        Lay(s, src, b, ground: false);
        Assert.Equal(2, s.Plan.Feeders.Count(f => f.PieceId == a.Id));
        Assert.Equal(3, PowerSupply.FeedersOf(s.Plan, src).Count());
        var at = s.Plan.Feeders.Where(f => f.PieceId == a.Id).Select(f => f.At).OrderBy(x => x).ToList();
        Assert.True(at[1] - at[0] > 0.4);
        Assert.Contains("Einspeisungen", s.ConnectionSummary(a.Id));
    }

    [Fact]
    public void Einspeisung_verschwindet_mit_dem_Kabel_und_kommt_mit_Rueckgaengig_wieder()
    {
        var (s, a, _, src) = Setup();
        var wire = Lay(s, src, a, ground: false);
        Assert.Single(s.Plan.Feeders);
        s.RemoveObject(wire.Id);
        Assert.Empty(s.Plan.Feeders);
        s.Undo();
        Assert.Single(s.Plan.Feeders);
        s.Undo();
        Assert.Empty(s.Plan.Feeders);
    }

    [Fact]
    public void Masse_Einspeisung_und_Kabelbezug_werden_gespeichert()
    {
        var (s, a, _, src) = Setup();
        Lay(s, src, a, ground: false, at: 0.25);
        Lay(s, src, a, ground: true, otherRail: true);
        var json = PlanSerializer.ToJson(s.Plan);
        var back = PlanSerializer.FromJson(json, out _);
        Assert.Equal(2, back.Feeders.Count);
        var g = Assert.Single(back.Feeders, f => f.Ground);
        var f = Assert.Single(back.Feeders, f => !f.Ground);
        Assert.Equal(0.25, f.At, 2);
        Assert.NotNull(f.WireId);
        Assert.Contains(back.Scenery, w => w.IsWire && w.Id == f.WireId);
        Assert.Contains(back.Scenery, w => w.IsWire && w.Id == g.WireId);
        // nach dem Laden hängt die Einspeisung weiter am Kabel
        back.RemoveObject(f.WireId!.Value);
        Assert.Single(back.Feeders);
    }

    [Fact]
    public void Kabel_ohne_Gleis_am_Ende_legt_keine_Einspeisung_an()
    {
        var (s, _, _, src) = Setup();
        var start = s.WireStart(src.Id, false)!.Value;
        s.AddWire([start, (start.X + 200, start.Y)], WireUse.Fahrstrom);
        Assert.Empty(s.Plan.Feeders);
    }

    [Fact]
    public void Kabelpunkt_rastet_auf_dem_Gleis_ein()
    {
        var (s, a, _, _) = Setup();
        var rail = Electrics.FeederPoint(a, s.Plan.Gauge.TrackWidth, CurrentKind.Gleichstrom);
        var (_, _, anchor) = s.SnapWire(rail.X, rail.Y, 5);
        Assert.NotNull(anchor);
        Assert.Contains($"Nr. {a.Id}", anchor!.Text);
        Assert.Null(s.SnapWire(rail.X, rail.Y + 200, 5).Anchor);
    }

    [Fact]
    public void Alle_Ebenen_aus_und_wieder_einblenden()
    {
        var s = new PlanSession();
        s.AddLevel();
        s.AddLevel();
        Assert.True(s.Plan.AllLevelsVisible);
        s.SetAllLevelsVisible(false);
        Assert.All(s.Plan.Levels, l => Assert.False(l.Visible));
        s.SetAllLevelsVisible(true);
        Assert.True(s.Plan.AllLevelsVisible);
    }
}
