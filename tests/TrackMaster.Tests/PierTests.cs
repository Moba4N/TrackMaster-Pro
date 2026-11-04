using TrackMaster.Core;
using Xunit;

namespace TrackMaster.Tests;

public class PierTests
{
    private static LayoutPlan Raised()
    {
        var s = Sessions.H0();
        var p = s.Add("H0|mc|24188");
        p.Height = 120;
        return s.Plan;
    }

    [Fact]
    public void Stuetzen_sind_standardmaessig_ausgeblendet()
    {
        var model = new Scene3DModel(Raised(), scenery: false);
        Assert.False(model.Meshes.TryGetValue((Scene3DModel.PierColor, SurfaceKind.Matte), out var m) && !m.IsEmpty);
    }

    [Fact]
    public void Eingeblendete_Stuetzen_stehen_rechts_und_links_neben_dem_Gleis()
    {
        var plan = Raised();
        var piece = plan.Pieces[0];
        var g = plan.Gauge.TrackWidth;
        var model = new Scene3DModel(plan, scenery: false, piers: true);
        var pts = model.Meshes[(Scene3DModel.PierColor, SurfaceKind.Matte)].Points;
        Assert.NotEmpty(pts);
        var mid = piece.WorldLegs()[0].At(0.5);
        double nx = -Math.Sin(mid.HeadingRad), ny = Math.Cos(mid.HeadingRad);
        // Punkte, die bis zum Boden reichen (die Stützen): seitlicher Abstand zur Gleismitte
        var feet = pts.Where(q => q.Y < 1).Select(q => (q.X - mid.X) * nx + (q.Z - mid.Y) * ny).ToList();
        Assert.Contains(feet, d => d > 1.4 * g);
        Assert.Contains(feet, d => d < -1.4 * g);
        Assert.DoesNotContain(feet, d => Math.Abs(d) < 1.4 * g);
    }
}
