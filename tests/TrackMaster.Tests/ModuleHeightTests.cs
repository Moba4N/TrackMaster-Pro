using TrackMaster.Core;
using Xunit;

namespace TrackMaster.Tests;

public class ModuleHeightTests
{
    private static PlanSession TwoModules()
    {
        var s = new PlanSession();
        s.NewPlan(new BoardSetup("H0", BoardShape.Module, 1000, 500, 500, 0));
        s.ApplyModules([new ModuleEdit(0, "A", 0, 0, 1000, 500), new ModuleEdit(-1, "B", 1000, 0, 1000, 500)]);
        return s;
    }

    [Fact]
    public void RaisingAModuleLiftsItsTracks()
    {
        var s = TwoModules();
        s.Drop("H0|mc|24188", 1300, 250, 0);
        var onB = s.Plan.Pieces[^1];
        s.Drop("H0|mc|24188", 300, 250, 0);
        var onA = s.Plan.Pieces[^1];
        s.SetModule(1, "B", 1000, 0, 1000, 500, 120);
        Assert.Equal(120, s.Plan.Modules[1].Height);
        Assert.Equal(120, onB.Height, 3);
        Assert.Equal(0, onA.Height, 3);
        // ohne Höhenangabe (z. B. aus älteren Dialogen) bleibt sie erhalten
        s.SetModule(1, "B", 1000, 0, 1000, 500);
        Assert.Equal(120, s.Plan.Modules[1].Height);
        Assert.Equal(120, onB.Height, 3);
        // neues Gleis auf dem gehobenen Modul liegt auf dessen Oberfläche
        s.Drop("H0|mc|24188", 1600, 400, 0);
        Assert.Equal(120, s.Plan.Pieces[^1].Height, 3);
        Assert.Equal(120, TerrainField.GroundAt(s.Plan, 1500, 250), 3);
        s.Undo(3);
        Assert.Equal(0, s.Plan.Modules[1].Height);
    }

    [Fact]
    public void HeightIsSavedAndLoaded()
    {
        var s = TwoModules();
        s.SetModule(0, "A", 0, 0, 1000, 500, -40.5);
        var back = PlanSerializer.FromJson(PlanSerializer.ToJson(s.Plan), out _, out _);
        Assert.Equal(-40.5, back.Modules[0].Height);
        Assert.Equal(0, back.Modules[1].Height);
    }

    [Fact]
    public void SceneShowsRaisedModule()
    {
        var s = TwoModules();
        s.SetModule(1, "B", 1000, 0, 1000, 500, 200);
        var model = new Scene3DModel(s.Plan);
        Assert.Contains(model.Meshes.Values.SelectMany(m => m.Points), v => Math.Abs(v.Y - 200) < 0.01);
    }

    [Fact]
    public void RaisedModuleIsAPlateOfItsOwnThickness()
    {
        var s = TwoModules();
        s.SetModule(1, "B", 1000, 0, 1000, 500, 200, 18);
        Assert.Equal(18, s.Plan.Modules[1].Thickness);
        // Höhe ändern lässt die Stärke stehen, Speichern und Laden behalten sie
        s.SetModule(1, "B", 1000, 0, 1000, 500, 220);
        Assert.Equal(18, s.Plan.Modules[1].Thickness);
        var back = PlanSerializer.FromJson(PlanSerializer.ToJson(s.Plan), out _, out _);
        Assert.Equal(18, back.Modules[1].Thickness);
        Assert.Equal(0, back.Modules[0].Thickness);
        var pts = new Scene3DModel(s.Plan).Meshes.Values.SelectMany(m => m.Points).ToList();
        Assert.Contains(pts, v => Math.Abs(v.Y - 202) < 0.01);
        // kein Klotz bis zum Boden: nichts unter dem Modul ausser dem flachen Rahmen
        Assert.DoesNotContain(pts, v => v.X > 1003 && v.Y < 180);
    }
}
