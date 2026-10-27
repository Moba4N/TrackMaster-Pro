using TrackMaster.Core;

namespace TrackMaster.Tests;

public class TerrainTests
{
    private static readonly List<(double X, double Y)> Square = [(0, 0), (600, 0), (600, 400), (0, 400)];

    [Fact]
    public void Huegel_steigt_vom_Rand_zur_vollen_Hoehe()
    {
        var f = new TerrainField(Square, 120);
        Assert.Equal(0, f.At(-50, 200));
        Assert.True(f.At(5, 200) < 10);
        Assert.Equal(120, f.At(300, 200), 1);
    }

    [Fact]
    public void Hoehenlinien_sind_geschlossen_und_liegen_innen()
    {
        var f = new TerrainField(Square, 120);
        var layers = f.Layers();
        Assert.Equal(TerrainField.ContourStep(120), layers[0].Level);
        Assert.True(layers.Count >= 4);
        foreach (var (level, loops) in layers)
        {
            Assert.NotEmpty(loops);
            foreach (var loop in loops)
                Assert.All(loop, p => Assert.True(Geometry2D.PointInPolygon(Square, p.X, p.Y)));
        }
    }

    [Fact]
    public void Baeume_und_Gebaeude_stehen_auf_dem_Huegel()
    {
        var plan = SamplePlan.Build();
        var hill = plan.Scenery.First(s => s.Kind == SceneryKind.Terrain);
        var (cx, cy) = hill.Center();
        Assert.True(TerrainField.GroundAt(plan, cx, cy) > 0);
        Assert.Equal(0, TerrainField.GroundAt(plan, -100, -100));
    }

    [Fact]
    public void Gleise_liegen_in_3D_ueber_der_Platte()
    {
        var plan = SamplePlan.Build();
        var model = new Scene3DModel(plan);
        var rails = model.Meshes[(Scene3DModel.RailColor, SurfaceKind.Rail)];
        Assert.True(rails.Points.Min(p => p.Y) > 0);
    }
}
