using TrackMaster.Core;

namespace TrackMaster.Tests;

/// <summary>Rahmenbau selbst zeichnen: Raster überall, Leisten rasten ein, mehrere Leisten zusammen wählen und verschieben.</summary>
public class FrameSelectionTests
{
    private static PlanSession ManualPlan()
    {
        var s = new PlanSession();
        s.ShowGrid = false;
        s.SnapEnabled = false;
        s.NewPlan(new BoardSetup("H0", BoardShape.Rechteck, 2000, 1000, 600, 0, Benchwork: BenchTemplate.Manual(s.Plan.Gauge)));
        return s;
    }

    [Fact]
    public void Neuer_selbst_gezeichneter_Rahmen_zeigt_Raster_ueberall_und_rastet_ein()
    {
        var s = ManualPlan();
        Assert.True(s.ShowGrid);
        Assert.True(s.SnapEnabled);
        Assert.True(s.GridEverywhere);

        var plain = new PlanSession();
        plain.NewPlan(new BoardSetup("H0", BoardShape.Rechteck, 2000, 1000, 600, 0));
        Assert.False(plain.GridEverywhere);
        plain.Tool = CanvasTool.Beam;
        Assert.True(plain.GridEverywhere);
    }

    [Fact]
    public void Freier_Leistenpunkt_rastet_am_Raster_ein()
    {
        var s = ManualPlan();
        // weit weg von Platte und Leisten: Raster
        Assert.Equal((2340.0, -120.0), BeamLayout.Snap(s.Plan, 2343.4, -117.2, 5, grid: 10));
        // ohne Raster wie bisher
        Assert.Equal((2343.4, -117.2), BeamLayout.Snap(s.Plan, 2343.4, -117.2, 5));
        // auf der Plattenkante: entlang der Kante am Raster
        Assert.Equal((800.0, 1000.0), BeamLayout.Snap(s.Plan, 801.7, 1003, 20, grid: 10));
        // vom Anfang aus fast senkrecht: Achse bleibt, das Ende kommt aufs Raster
        Assert.Equal((2504.0, 2510.0), BeamLayout.Snap(s.Plan, 2506, 2512, 10, from: (2504, 3000), grid: 10));
    }

    [Fact]
    public void Gezogene_Leiste_rastet_am_Ende_einer_anderen_Leiste_ein()
    {
        var s = ManualPlan();
        var a = s.AddBeam((0, 0), (0, 1000))!;
        var b = s.AddBeam((500, 300), (900, 300))!;
        s.SelectObject(b.Id);
        s.BeginMove(snapTolerance: 20);
        // Anfang von b fast auf das obere Ende von a ziehen
        s.MoveBy(-497, -296);
        s.EndMove(20);
        Assert.Equal((0.0, 0.0), b.Points[0]);
        Assert.Equal((400.0, 0.0), b.Points[1]);
        Assert.Equal((0.0, 0.0), a.Points[0]);
    }

    [Fact]
    public void Frei_gezogene_Leiste_rastet_am_Raster_ein()
    {
        var s = ManualPlan();
        var b = s.AddBeam((3000, 3000), (3400, 3000))!;
        s.SelectObject(b.Id);
        s.BeginMove(snapTolerance: 5);
        s.MoveBy(123.4, 56.7);
        s.EndMove(5);
        Assert.Equal((3120.0, 3060.0), b.Points[0]);
        Assert.Equal((3520.0, 3060.0), b.Points[1]);
    }

    [Fact]
    public void Ohne_Einrasten_bleibt_die_Verschiebung_frei()
    {
        var s = ManualPlan();
        s.SnapEnabled = false;
        var b = s.AddBeam((3000, 3000), (3400, 3000))!;
        s.SelectObject(b.Id);
        s.BeginMove(snapTolerance: 5);
        s.MoveBy(123.4, 56.7);
        s.EndMove(5);
        Assert.Equal(3123.4, b.Points[0].X, 6);
    }

    [Fact]
    public void Mehrere_Leisten_waehlen_verschieben_kopieren_und_loeschen()
    {
        var s = ManualPlan();
        var a = s.AddBeam((0, 0), (2000, 0))!;
        var b = s.AddBeam((0, 1000), (2000, 1000))!;
        var c = s.AddBeam((1000, 0), (1000, 1000))!;

        // Umschalt/Strg-Klick: hinzufügen
        Assert.True(s.ClickObject(a.Id, add: false));
        Assert.True(s.ClickObject(b.Id, add: true));
        Assert.Equal([a.Id, b.Id], s.SelectedObjects.Order());
        // Klick ohne Umschalt auf ein gewähltes Teil lässt die Auswahl stehen (zum Ziehen)
        Assert.True(s.ClickObject(a.Id, add: false));
        Assert.Equal(2, s.SelectedObjects.Count);

        s.BeginMove();
        s.MoveBy(0, 50);
        s.EndMove(10);
        Assert.Equal((0.0, 50.0), a.Points[0]);
        Assert.Equal((0.0, 1050.0), b.Points[0]);
        Assert.Equal((1000.0, 0.0), c.Points[0]);   // nicht gewählt

        // Pfeiltasten verschieben alle gewählten
        s.Nudge(10, 0, record: true);
        Assert.Equal((10.0, 50.0), a.Points[0]);
        Assert.Equal((10.0, 1050.0), b.Points[0]);

        // nochmal Umschalt-Klick: wieder heraus
        Assert.False(s.ClickObject(b.Id, add: true));
        Assert.Equal([a.Id], s.SelectedObjects);
        Assert.Equal(a.Id, s.SelectedObject);
        s.ClickObject(b.Id, add: true);

        // kopieren und einfügen: beide neuen Leisten sind gewählt
        var json = s.CopySelection();
        Assert.True(s.Paste(json));
        Assert.Equal(5, s.Plan.Scenery.Count(x => x.IsBeam));
        Assert.Equal(2, s.SelectedObjects.Count);
        Assert.DoesNotContain(a.Id, s.SelectedObjects);

        // löschen nimmt alle gewählten
        s.Delete();
        Assert.Equal(3, s.Plan.Scenery.Count(x => x.IsBeam));
        Assert.Empty(s.SelectedObjects);
        Assert.Null(s.SelectedObject);
        s.Undo();
        Assert.Equal(5, s.Plan.Scenery.Count(x => x.IsBeam));
    }

    [Fact]
    public void Rahmen_aufziehen_waehlt_Leisten_nur_ohne_Gleise()
    {
        var s = ManualPlan();
        var a = s.AddBeam((0, 0), (2000, 0))!;
        var b = s.AddBeam((0, 1000), (2000, 1000))!;
        s.SelectArea([], [a.Id, b.Id], add: false);
        Assert.Equal(2, s.SelectedObjects.Count);
        Assert.Empty(s.Selection);

        // liegen Gleise im Rahmen, werden nur die Gleise gewählt
        var piece = s.Plan.Pieces.FirstOrDefault()?.Id ?? AddPiece(s);
        s.SelectArea([piece], [a.Id, b.Id], add: false);
        Assert.Empty(s.SelectedObjects);
        Assert.Contains(piece, s.Selection);
    }

    [Fact]
    public void Umschalt_Klick_auf_anderes_Objekt_waehlt_es_allein()
    {
        var s = ManualPlan();
        var a = s.AddBeam((0, 0), (2000, 0))!;
        var tree = s.AddScenery(new SceneryItem { Kind = SceneryKind.Tree, X = 100, Y = 100, Width = 30, Height = 60 });
        s.ClickObject(a.Id, add: false);
        Assert.True(s.ClickObject(tree.Id, add: true));
        Assert.Equal([tree.Id], s.SelectedObjects);
    }

    private static int AddPiece(PlanSession s)
    {
        var key = s.CurrentSystem.Pieces.First(p => p.Kind == PieceKind.Straight).Key;
        s.Attach(key, (500, 500));
        return s.Plan.Pieces.Last().Id;
    }
}
