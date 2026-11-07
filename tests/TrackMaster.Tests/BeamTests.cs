using TrackMaster.Core;

namespace TrackMaster.Tests;

/// <summary>Frei gezogene Leisten des Unterbaus: Diagonale unter versetzten Modulen und Streben dazu.</summary>
public class BeamTests
{
    [Fact]
    public void Strebe_von_der_Plattenkante_rastet_rechtwinklig_auf_der_Diagonale_ein()
    {
        var s = new PlanSession();
        s.Plan.BoardWidth = 2000;
        s.Plan.BoardHeight = 1000;
        var diagonal = s.AddBeam((0, 1000), (2000, 1500))!;
        Assert.Equal(SceneryKind.Beam, diagonal.Kind);
        // Anfang auf der Unterkante der Platte, Ende in der Nähe der Diagonale
        var from = BeamLayout.Snap(s.Plan, 801, 1003, 20);
        Assert.Equal((801.0, 1000.0), from);
        var to = BeamLayout.Snap(s.Plan, 795, 1190, 20, from);
        Assert.Equal(801, to.X, 1);
        Assert.Equal(1000 + 500 * 801 / 2000.0, to.Y, 1);
        var strut = s.AddBeam(from, to)!;
        Assert.Equal(200.25, BeamLayout.Length(strut), 0);
        Assert.Equal(90, BeamLayout.Angle(strut), 1);
    }

    [Fact]
    public void Leisten_ueberstehen_Speichern_und_stehen_in_Zuschnitt_und_3D()
    {
        var s = new PlanSession();
        var b = s.AddBeam((0, 0), (1200, 500))!;
        s.Edit(() => { b.Width = 22; b.Depth = 95; b.Height = -40; PlanSession.SetBeamLength(b, 1000); });
        Assert.Equal(1000, BeamLayout.Length(b), 0);
        var back = PlanSerializer.FromJson(PlanSerializer.ToJson(s.Plan), out _, out _);
        var beam = Assert.Single(back.Scenery, x => x.IsBeam);
        Assert.Equal((22.0, 95.0, -40.0), (beam.Width, beam.Depth, beam.Height));
        Assert.Equal(1000, BeamLayout.Length(beam), 0);

        back.Benchwork = BenchworkSpec.Defaults(back.Gauge);
        var plan = Benchwork.Build(back, back.Benchwork);
        var cut = Assert.Single(plan.Cuts, c => c.Use == "Leiste");
        Assert.Equal(("22 × 95", 1000.0, 1), (cut.Section, cut.Length, cut.Count));
        Assert.Contains(plan.Materials, m => m.Size.StartsWith("22 × 95"));

        var without = new Scene3DModel(PlanSerializer.FromJson(PlanSerializer.ToJson(new PlanSession().Plan), out _)).TriangleCount;
        var with = new Scene3DModel(PlanSerializer.FromJson(PlanSerializer.ToJson(s.Plan), out _)).TriangleCount;
        Assert.True(with > without);
    }

    [Fact]
    public void Zu_kurze_Leiste_wird_nicht_angelegt()
    {
        var s = new PlanSession();
        Assert.Null(s.AddBeam((0, 0), (3, 0)));
        Assert.DoesNotContain(s.Plan.Scenery, x => x.IsBeam);
    }
}

public class NewPlanBenchworkTests
{
    [Fact]
    public void Alle_Vorlagen_sind_in_jeder_Spur_gueltig()
    {
        foreach (var g in Catalog.Gauges)
            foreach (var t in BenchTemplate.All)
                Assert.True(Benchwork.Validate(t.Spec(g)).Count == 0, $"{g.Id} {t.Id}");
    }

    [Fact]
    public void Neuer_Plan_direkt_mit_Vorlage_oder_selbst_gezeichnet()
    {
        var s = new PlanSession();
        s.NewPlan(new BoardSetup("H0", BoardShape.Rechteck, 2000, 1000, 600, 0));
        Assert.Null(s.Plan.Benchwork);

        s.NewPlan(new BoardSetup("H0", BoardShape.Rechteck, 2000, 1000, 600, 0, Benchwork: BenchTemplate.Find("lbein")!.Spec(s.Plan.Gauge)));
        Assert.Equal(BenchLegStyle.LBein, s.Plan.Benchwork!.LegStyle);
        Assert.True(Benchwork.Build(s.Plan, s.Plan.Benchwork).LegCount > 0);

        s.NewPlan(new BoardSetup("H0", BoardShape.Rechteck, 2000, 1000, 600, 0, Benchwork: BenchTemplate.Manual(s.Plan.Gauge)));
        Assert.True(s.Plan.Benchwork!.Manual);
        s.AddBeam((0, 0), (2000, 0));
        s.AddBeam((0, 1000), (2000, 1000));
        var b = Benchwork.Build(s.Plan, s.Plan.Benchwork);
        Assert.Equal(0, b.LegCount);
        Assert.DoesNotContain(b.Pieces, p => p.Kind is BenchKind.Zarge or BenchKind.Querriegel or BenchKind.Bein);
        Assert.Equal(2, b.Pieces.Count(p => p.Kind == BenchKind.Leiste));
        Assert.DoesNotContain(b.Pieces, p => p.Kind == BenchKind.Platte);   // Deckplatte erst mit „Platte auflegen“
        Assert.All(b.Materials, m => Assert.True(m.Quantity > 0));
        Assert.Contains(b.Materials, m => m.Item == "Holzschrauben");
        // bleibt beim Speichern erhalten
        var back = PlanSerializer.FromJson(PlanSerializer.ToJson(s.Plan), out _);
        Assert.True(back.Benchwork!.Manual);
    }
}

public class OpenFrameAndNudgeTests
{
    [Fact]
    public void Offener_Rahmen_zeigt_in_3D_keine_Platte_und_kein_Gelaende()
    {
        var s = new PlanSession();
        s.NewPlan(new BoardSetup("H0", BoardShape.Rechteck, 2000, 1000, 600, 0, Benchwork: BenchTemplate.Find("offen")!.Spec(s.Plan.Gauge)));
        s.AddScenery(new SceneryItem { Kind = SceneryKind.Terrain, Points = [(100, 100), (500, 100), (500, 500)], Height = 80 });
        var m = new Scene3DModel(s.Plan, scenery: true);
        Assert.True(m.OpenFrame);
        Assert.False(m.Scenery);
        Assert.DoesNotContain(m.Meshes.Keys, k => k.Color == new Rgb(0x6E, 0x5A, 0x40));

        s.NewPlan(new BoardSetup("H0", BoardShape.Rechteck, 2000, 1000, 600, 0, Benchwork: BenchTemplate.Find("platte")!.Spec(s.Plan.Gauge)));
        var full = new Scene3DModel(s.Plan, scenery: true);
        Assert.False(full.OpenFrame);
        Assert.Contains(full.Meshes.Keys, k => k.Color == new Rgb(0x6E, 0x5A, 0x40));
    }

    [Fact]
    public void Pfeiltasten_verschieben_die_gewaehlte_Leiste()
    {
        var s = new PlanSession();
        var b = s.AddBeam((0, 0), (1000, 0))!;
        Assert.Equal(b.Id, s.SelectedObject);
        Assert.True(s.CanNudge);
        s.Nudge(0, 10, record: true);
        s.Nudge(-1, 0, record: true);
        Assert.Equal((-1.0, 10.0), b.Points[0]);
        Assert.Equal((999.0, 10.0), b.Points[1]);
        s.Undo();
        Assert.Equal((0.0, 10.0), s.Plan.Scenery.Single(x => x.IsBeam).Points[0]);
    }
}

public class PlateTests
{
    [Fact]
    public void Platte_auflegen_nimmt_die_Masse_der_Leisten_und_liegt_darauf()
    {
        var s = new PlanSession();
        s.NewPlan(new BoardSetup("H0", BoardShape.Rechteck, 2000, 1000, 600, 0, Benchwork: BenchTemplate.Manual(s.Plan.Gauge)));
        s.AddBeam((100, 100), (1300, 100));
        s.AddBeam((100, 700), (1300, 700));
        var top = s.Plan.Scenery.First(x => x.IsBeam).Height;
        var p = s.AddPlate();
        Assert.True(p.IsPlate);
        Assert.Equal(p.Id, s.SelectedObject);
        // außen über die Leistenbreite (18 mm Zarge → 9 mm je Seite)
        Assert.Equal(100, p.X, 0);
        Assert.Equal(91, p.Y, 0);
        Assert.Equal(1200, p.Width, 0);
        Assert.Equal(618, p.Depth, 0);
        Assert.Equal(top + PlateLayout.Thickness(p), p.Height, 3);

        // Pfeiltasten verschieben, Maße in mm ändern, an den Rahmen zurücksetzen
        s.Nudge(10, -1, record: true);
        Assert.Equal((110.0, 90.0), (p.X, Math.Round(p.Y)));
        p.Width = 1234.5;
        s.FitPlate(p);
        Assert.Equal(1200, p.Width, 0);
        Assert.Equal(100, p.X, 0);

        // Unterbau: Platte in der Materialliste, 3D zeigt sie
        var b = Benchwork.Build(s.Plan, s.Plan.Benchwork!);
        Assert.Single(b.Pieces, x => x.Kind == BenchKind.Platte);
        Assert.Contains(b.Materials, m => m.Item == "Sperrholzplatte");
        var m3 = new Scene3DModel(s.Plan);
        Assert.True(m3.OpenFrame);
        Assert.Contains(m3.Meshes.Keys, k => k.Color == new Rgb(0xC8, 0xA8, 0x78));

        // Speichern und Laden
        var back = PlanSerializer.FromJson(PlanSerializer.ToJson(s.Plan), out _);
        var q = back.Scenery.Single(x => x.IsPlate);
        Assert.Equal((p.X, p.Y, p.Width, p.Depth, p.Variant, p.Height), (q.X, q.Y, q.Width, q.Depth, q.Variant, q.Height));
    }

    [Fact]
    public void Platte_auf_offenem_Rahmen_ersetzt_keine_Rahmenteile_und_liegt_auf_dem_Rahmen()
    {
        var s = new PlanSession();
        s.NewPlan(new BoardSetup("H0", BoardShape.Rechteck, 2000, 1000, 600, 0, Benchwork: BenchTemplate.Find("offen")!.Spec(s.Plan.Gauge)));
        var p = s.AddPlate();
        Assert.Equal((0.0, 0.0, 2000.0, 1000.0), (p.X, p.Y, p.Width, p.Depth));
        Assert.Equal(10, p.Variant);
        Assert.Equal(10, p.Height, 3);   // Rahmen-Oberkante 0 + 10 mm
        var b = Benchwork.Build(s.Plan, s.Plan.Benchwork!);
        Assert.Contains(b.Pieces, x => x.Kind == BenchKind.Zarge);
        Assert.Single(b.Pieces, x => x.Kind == BenchKind.Platte);
    }
}
