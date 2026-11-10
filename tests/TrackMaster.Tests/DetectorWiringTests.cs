using TrackMaster.Core;

namespace TrackMaster.Tests;

/// <summary>Rückmelder automatisch mit dem Rückmeldemodul verkabeln: Module anlegen, Kabel verlegen, nachführen und entfernen.</summary>
public class DetectorWiringTests
{
    private static (PlanSession Session, List<PlacedPiece> Pieces) Line(int count)
    {
        var plan = new LayoutPlan { GaugeId = "H0", BoardWidth = 4000, BoardHeight = 1000 };
        var pieces = new List<PlacedPiece>();
        double x = 100, y = 500, h = 180;
        for (var i = 0; i < count; i++)
        {
            var p = plan.Add(Catalog.FlexStraightKey("H0", 200));
            PlanEditing.PlaceAt(p, 0, x, y, h);
            var end = p.WorldConnectors()[1];
            (x, y, h) = (end.X, end.Y, end.Heading);
            pieces.Add(p);
        }
        var s = new PlanSession { IsPro = () => true };
        s.Replace(plan, null);
        return (s, pieces);
    }

    private static List<SceneryItem> AutoWires(PlanSession s) => s.Plan.Scenery.Where(DetectorWiring.IsAuto).ToList();

    private static List<SceneryItem> FeedbackModules(PlanSession s)
        => PowerSupply.Modules(s.Plan).Where(m => Decoders.ModelOf(m)?.Role == ModuleRole.Melden).ToList();

    [Fact]
    public void Einschalten_legt_Modul_und_Kabel_zur_richtigen_Klemme_an()
    {
        var (s, pieces) = Line(3);
        foreach (var p in pieces) s.ToggleDetectorOn(p.Id);
        Assert.Empty(s.Plan.Scenery);

        s.SetDetectorWiring(true);
        var module = Assert.Single(FeedbackModules(s));
        Assert.Equal((1, 16), Decoders.Range(module));
        var wires = AutoWires(s);
        Assert.Equal(3, wires.Count);
        foreach (var d in s.Plan.Detectors)
        {
            var w = Assert.Single(wires, w => w.Tracks[0] == d.PieceId);
            Assert.Equal(WireUse.Rueckmelder, WireLayout.Use(w));
            var from = Electrics.DetectorPoint(s.Plan.Find(d.PieceId)!, s.Plan.Gauge.TrackWidth);
            var to = Decoders.Ports(module)[d.Address!.Value - 1];
            Assert.Equal(from, w.Points[0]);
            Assert.Equal(to, w.Points[^1]);
            // Kabel liegen rechtwinklig
            for (var i = 1; i < w.Points.Count; i++)
                Assert.True(Math.Abs(w.Points[i].X - w.Points[i - 1].X) < 0.5 || Math.Abs(w.Points[i].Y - w.Points[i - 1].Y) < 0.5);
            Assert.Contains("Melder", WireLayout.EndAnchor(s.Plan, w, true)!.Text);
        }
        // Rückgängig nimmt alles in einem Schritt zurück
        s.Undo();
        Assert.Empty(s.Plan.Scenery);
        Assert.False(s.Plan.DetectorWiring);
    }

    [Fact]
    public void Je_16_Kontakte_ein_Modul_und_neue_Melder_werden_sofort_verbunden()
    {
        var (s, pieces) = Line(2);
        s.SetDetectorWiring(true);
        Assert.Empty(s.Plan.Scenery);
        s.ToggleDetectorOn(pieces[0].Id);
        Assert.Single(AutoWires(s));
        Assert.Single(FeedbackModules(s));

        // Kontakt 20 liegt im zweiten Modul (17–32)
        var d = s.DetectorOn(pieces[0].Id)!;
        s.Edit(() => d.Address = 20);
        var mods = FeedbackModules(s);
        Assert.Equal(2, mods.Count);
        Assert.Contains(mods, m => Decoders.Range(m) == (17, 32));
        var w = Assert.Single(AutoWires(s));
        var second = mods.Single(m => Decoders.Range(m) == (17, 32));
        Assert.Equal(Decoders.Ports(second)[3], w.Points[^1]);

        // Kontakt entfernen: Kabel weg, Module bleiben
        s.Edit(() => d.Address = null);
        Assert.Empty(AutoWires(s));
        Assert.Equal(2, FeedbackModules(s).Count);
    }

    [Fact]
    public void Vorhandenes_Modul_wird_genutzt_und_Kabel_folgt_dem_Gleis()
    {
        var (s, pieces) = Line(1);
        var lenz = s.AddModule("LR101", 300, 900)!;
        Assert.Equal((1, 8), Decoders.Range(lenz));
        s.ToggleDetectorOn(pieces[0].Id);
        s.SetDetectorWiring(true);
        Assert.Single(FeedbackModules(s));
        var w = Assert.Single(AutoWires(s));
        Assert.Equal(Decoders.Ports(lenz)[0], w.Points[^1]);

        // Gleis verschieben: das Kabel geht mit
        s.Select([pieces[0].Id], add: false);
        s.MoveBy(0, -100);
        var moved = Assert.Single(AutoWires(s));
        Assert.Equal(Electrics.DetectorPoint(s.Plan.Find(pieces[0].Id)!, s.Plan.Gauge.TrackWidth), moved.Points[0]);
        Assert.Equal(Decoders.Ports(lenz)[0], moved.Points[^1]);

        // Rückmelder löschen: Kabel weg
        s.ToggleDetectorOn(pieces[0].Id);
        Assert.Empty(AutoWires(s));
    }

    [Fact]
    public void Ausschalten_entfernt_nur_die_automatischen_Kabel()
    {
        var (s, pieces) = Line(2);
        s.ToggleDetectorOn(pieces[0].Id);
        s.ToggleDetectorOn(pieces[1].Id);
        s.SetDetectorWiring(true);
        string? said = null;
        s.Message += t => said = t;
        var own = s.AddWire([(0, 0), (100, 0)], WireUse.Fahrstrom)!;
        Assert.Equal(2, AutoWires(s).Count);

        s.SetDetectorWiring(false);
        Assert.Empty(AutoWires(s));
        Assert.Contains(own, s.Plan.Scenery);
        Assert.Single(FeedbackModules(s));
        Assert.Contains("2 Rückmelder-Kabel entfernt", said);
    }

    [Fact]
    public void Einstellung_und_Kabel_bleiben_beim_Speichern_erhalten()
    {
        var (s, pieces) = Line(2);
        s.ToggleDetectorOn(pieces[0].Id);
        s.ToggleDetectorOn(pieces[1].Id);
        s.SetDetectorWiring(true);
        var back = PlanSerializer.FromJson(PlanSerializer.ToJson(s.Plan), out _);
        Assert.True(back.DetectorWiring);
        var wires = back.Scenery.Where(DetectorWiring.IsAuto).ToList();
        Assert.Equal(2, wires.Count);
        Assert.Equal(pieces.Select(p => p.Id).Order(), wires.Select(w => w.Tracks[0]).Order());
        // Abgleich nach dem Laden ändert nichts
        Assert.False(DetectorWiring.Sync(back).Any);
    }

    [Fact]
    public void Wechselstrom_nimmt_s88_AC_sonst_s88_DC()
    {
        var (s, pieces) = Line(1);
        s.ToggleDetectorOn(pieces[0].Id);
        s.SetCurrent(CurrentKind.Wechselstrom);
        s.SetDetectorWiring(true);
        Assert.Equal("60881", Assert.Single(FeedbackModules(s)).Model);
    }
    [Fact]
    public void Melder_heissen_fortlaufend_Melder_n()
    {
        var (s, pieces) = Line(3);
        foreach (var p in pieces) s.ToggleDetectorOn(p.Id);
        Assert.Equal(["Melder 1", "Melder 2", "Melder 3"], s.Plan.Detectors.Select(d => d.Name));
        s.ToggleDetectorOn(pieces[1].Id);
        s.ToggleDetectorOn(pieces[1].Id);
        Assert.Equal("Melder 4", s.DetectorOn(pieces[1].Id)!.Name);
    }

    [Fact]
    public void Melder_mit_Booster_speist_den_Abschnitt()
    {
        var (s, pieces) = Line(2);
        var zentrale = s.AddPowerSource(PowerSourceType.Zentrale, 100, 900);
        var booster = s.AddPowerSource(PowerSourceType.Booster, 600, 900);
        Assert.Contains(Electrics.Analyze(s.Plan).Issues, i => i.Category == "Einspeisung" && i.Level == IssueLevel.Fehler);

        // Neuer Melder bekommt den Booster als Quelle, der Abschnitt ist damit gespeist
        s.ToggleDetectorOn(pieces[0].Id);
        var d = s.DetectorOn(pieces[0].Id)!;
        Assert.Equal(booster.Name, d.Source);
        var issues = Electrics.Analyze(s.Plan).Issues;
        Assert.DoesNotContain(issues, i => i.Category == "Einspeisung" && i.Level == IssueLevel.Fehler);
        Assert.Single(PowerSupply.FeedersOf(s.Plan, booster));
        Assert.Contains("Fahrstrom über Melder 1", Electrics.WiringPlan(s.Plan));
        Assert.Empty(s.Plan.Feeders);

        // Quelle wählbar, auch „keine“
        s.SetDetectorSource(d.Id, zentrale.Name);
        Assert.Single(PowerSupply.FeedersOf(s.Plan, zentrale));
        s.SetDetectorSource(d.Id, null);
        Assert.Contains(Electrics.Analyze(s.Plan).Issues, i => i.Category == "Einspeisung" && i.Level == IssueLevel.Fehler);

        // Umbenennen der Quelle nimmt den Melder mit, Speichern behält die Wahl
        s.SetDetectorSource(d.Id, booster.Name);
        s.RenamePowerSource(booster.Id, "Booster Bahnhof");
        Assert.Equal("Booster Bahnhof", d.Source);
        var back = PlanSerializer.FromJson(PlanSerializer.ToJson(s.Plan), out _);
        Assert.Equal("Booster Bahnhof", back.Detectors.Single().Source);

        // Analog gibt es keinen Fahrstrom über Melder
        s.SetOperation(PowerMode.Analog);
        Assert.Empty(Electrics.DetectorFeeds(s.Plan));
    }
}
