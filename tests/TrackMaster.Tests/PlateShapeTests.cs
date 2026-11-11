using TrackMaster.Core;

namespace TrackMaster.Tests;

/// <summary>Platten in Sonderform (z. B. Dreieck zwischen versetzten Modulen und Diagonale) und „Alle Platten entfernen“.</summary>
public class PlateShapeTests
{
    private static PlanSession Plan()
    {
        var s = new PlanSession();
        s.NewPlan(new BoardSetup("H0", BoardShape.Rechteck, 2000, 1000, 600, 0, Benchwork: BenchTemplate.Find("lbein")!.Spec(s.Plan.Gauge)));
        return s;
    }

    [Fact]
    public void Dreieck_wird_als_Platte_aufgelegt_gespeichert_und_in_3D_gezeigt()
    {
        var s = Plan();
        var without = new Scene3DModel(s.Plan).TriangleCount;
        var p = s.AddPlate([(0, 1000), (2000, 1000), (0, 1500), (0, 1500)])!;   // doppelte Ecke fällt weg
        Assert.True(PlateLayout.IsShaped(p));
        Assert.Equal(3, p.Points.Count);
        Assert.Equal((0.0, 1000.0, 2000.0, 500.0), (p.X, p.Y, p.Width, p.Depth));
        Assert.Equal(0.5, PlateLayout.Area(p), 3);
        Assert.Equal(0, p.Height, 1);                       // bündig mit der Platte des Rahmens
        Assert.True(p.Contains(100, 1100));
        Assert.False(p.Contains(1900, 1450));               // außerhalb des Dreiecks, aber im Rechteck
        Assert.True(new Scene3DModel(s.Plan).TriangleCount > without);

        var back = PlanSerializer.FromJson(PlanSerializer.ToJson(s.Plan), out _);
        var plate = Assert.Single(back.Scenery, x => x.IsPlate);
        Assert.Equal(p.Points, plate.Points);
        Assert.Contains(Benchwork.Build(back, back.Benchwork!).Pieces, x => x.Kind == BenchKind.Platte);
    }

    [Fact]
    public void Sonderform_verschieben_und_Groesse_aendern()
    {
        var s = Plan();
        var p = s.AddPlate([(0, 0), (100, 0), (0, 100)])!;
        s.Nudge(10, 5, record: true);
        Assert.Equal((10.0, 5.0), p.Points[0]);
        Assert.Equal((10.0, 5.0), (p.X, p.Y));
        PlateLayout.SetBox(p, width: 200);
        Assert.Equal((210.0, 5.0), p.Points[1]);
        Assert.Equal(200, p.Width);
        s.FitPlate(p);
        Assert.False(PlateLayout.IsShaped(p));
    }

    [Fact]
    public void Zu_kleine_Form_wird_nicht_angelegt()
    {
        var s = Plan();
        Assert.Null(s.AddPlate([(0, 0), (100, 0)]));
        Assert.Null(s.AddPlate([(0, 0), (100, 0), (200, 0)]));
        Assert.DoesNotContain(s.Plan.Scenery, x => x.IsPlate);
    }

    [Fact]
    public void Alle_Platten_entfernen_zeigt_nur_den_Rahmen()
    {
        var s = Plan();
        s.AddPlate();
        s.AddPlate([(0, 1000), (2000, 1000), (0, 1500)]);
        Assert.False(new Scene3DModel(s.Plan).OpenFrame);
        s.RemoveAllPlates();
        Assert.DoesNotContain(s.Plan.Scenery, x => x.IsPlate);
        Assert.Equal(0, s.Plan.Benchwork!.TopThickness);
        var m = new Scene3DModel(s.Plan);
        Assert.True(m.OpenFrame);
        Assert.DoesNotContain(m.Meshes.Keys, k => k.Color == new Rgb(0x6E, 0x5A, 0x40));   // keine Platte des Plans
        Assert.DoesNotContain(Benchwork.Build(s.Plan, s.Plan.Benchwork).Pieces, x => x.Kind == BenchKind.Platte);
        Assert.Contains(Benchwork.Build(s.Plan, s.Plan.Benchwork).Pieces, x => x.Kind == BenchKind.Bein);   // Rahmen bleibt

        s.Undo();
        Assert.Equal(2, s.Plan.Scenery.Count(x => x.IsPlate));
        Assert.True(s.Plan.Benchwork!.TopThickness > 0);
    }

    [Fact]
    public void Module_mit_eigener_Plattenstaerke_verlieren_sie_beim_Entfernen()
    {
        var s = new PlanSession();
        s.NewPlan(new BoardSetup("H0", BoardShape.Module, 1000, 600, 600, 0, Benchwork: BenchTemplate.Find("lbein")!.Spec(s.Plan.Gauge)));
        s.Plan.Modules[0].Thickness = 19;
        s.RemoveAllPlates();
        Assert.Equal(0, s.Plan.Modules[0].Thickness);
        Assert.True(new Scene3DModel(s.Plan).OpenFrame);
    }
}
