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
