using TrackMaster.Core;
using Xunit;

namespace TrackMaster.Tests;

public class ModuleCutTests
{
    private static PlanSession TwoModules()
    {
        var s = new PlanSession();
        s.NewPlan(new BoardSetup("H0", BoardShape.Module, 1000, 500, 500, 0));
        s.ApplyModules([new ModuleEdit(0, "A", 0, 0, 1000, 500), new ModuleEdit(-1, "B", 1000, 0, 1000, 500)]);
        s.Plan.Pieces.Clear();
        return s;
    }

    private static PlacedPiece Put(PlanSession s, string key, double x, double y, double heading)
    {
        var p = s.Plan.Add(key);
        PlanEditing.PlaceAt(p, 0, x, y, heading);
        s.Commit();
        return p;
    }

    [Fact]
    public void StraightAcrossJointIsCutAtTheEdge()
    {
        var s = TwoModules();
        var p = Put(s, "H0|mc|24188", 950, 250, 0);
        var a = Put(s, "H0|mc|24188", 950 - p.Def.TrackLength, 250, 0);
        var b = Put(s, "H0|mc|24188", 950 + p.Def.TrackLength, 250, 0);
        s.Plan.Feeders.Add(new Feeder { Id = s.Plan.NextId++, PieceId = p.Id });
        Assert.Single(ModuleLayout.PiecesAcrossJoints(s.Plan));

        s.CutAtModuleJoints();

        Assert.Empty(ModuleLayout.PiecesAcrossJoints(s.Plan));
        Assert.DoesNotContain(s.Plan.Pieces, q => q.Id == p.Id);
        var parts = s.Plan.Pieces.Where(q => q.Id != a.Id && q.Id != b.Id).OrderBy(q => q.X).ToList();
        Assert.Equal(2, parts.Count);
        Assert.Equal(50, parts[0].Def.TrackLength, 1);
        Assert.Equal(p.Def.TrackLength - 50, parts[1].Def.TrackLength, 1);
        Assert.Equal(1000, parts[1].X, 2);
        // alles bleibt verbunden, die Einspeisung wandert mit
        Assert.Equal(2, s.Connections.Open.Count);
        Assert.Equal(parts[0].Id, s.Plan.Feeders.Single().PieceId);
        s.Undo(1);
        Assert.Contains(s.Plan.Pieces, q => q.Id == p.Id);
    }

    [Fact]
    public void CurveAcrossJointKeepsShapeAndHeight()
    {
        var s = TwoModules();
        var p = Put(s, "H0|mc|24330", 960, 250, -15);
        p.Grade = 0.02;
        p.Height = 10;
        var end = p.WorldConnectors()[1];
        var cuts = ModuleLayout.JointCuts(s.Plan, p);
        Assert.Single(cuts);

        s.CutAtModuleJoints();

        var parts = s.Plan.Pieces.OrderBy(q => q.X).ToList();
        Assert.Equal(2, parts.Count);
        Assert.Equal(p.Def.TrackLength, parts.Sum(q => q.Def.TrackLength), 1);
        var last = parts[1].WorldConnectors()[1];
        Assert.Equal(end.X, last.X, 1);
        Assert.Equal(end.Y, last.Y, 1);
        Assert.Equal(1000, parts[1].X, 2);
        Assert.Equal(10 + 0.02 * cuts[0], parts[1].Height, 1);
    }

    [Fact]
    public void SwitchesAreNotCut()
    {
        var s = TwoModules();
        var w = Put(s, "H0|mc|24611", 950, 250, 0);
        s.CutAtModuleJoints();
        Assert.Contains(s.Plan.Pieces, q => q.Id == w.Id);
        Assert.Empty(ModuleLayout.JointCuts(s.Plan, w));
    }
}
