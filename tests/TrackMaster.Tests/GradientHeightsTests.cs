using TrackMaster.Core;
using Xunit;

namespace TrackMaster.Tests;

public class GradientHeightsTests
{
    private static (PlanSession S, List<PlacedPiece> Chain) Line(int n)
    {
        var s = new PlanSession();
        s.NewPlan(new BoardSetup("H0", BoardShape.Rechteck, 3000, 1000, 500, 0));
        s.Plan.Pieces.Clear();
        var chain = new List<PlacedPiece>();
        double x = 200;
        for (var i = 0; i < n; i++)
        {
            var p = s.Plan.Add("H0|mc|24188");
            PlanEditing.PlaceAt(p, 0, x, 500, 0);
            x += p.Def.TrackLength;
            chain.Add(p);
        }
        s.Commit();
        return (s, chain);
    }

    [Fact]
    public void EndHeightGivesTheGradient()
    {
        var (s, chain) = Line(5);
        s.Select(chain.Select(p => p.Id).ToList(), add: false);
        var len = chain.Sum(p => p.Def.TrackLength);
        Assert.Equal(len, s.GradientLength(false)!.Value, 1);
        // Messen ändert nichts
        Assert.All(chain, p => Assert.Equal(0, p.Height));

        s.ApplyGradientHeights(10, 30, false);
        var heights = chain.SelectMany(p => p.ConnectorHeights()).ToList();
        Assert.Equal(10, heights.Min(), 2);
        Assert.Equal(30, heights.Max(), 2);
        Assert.All(chain, p => Assert.Equal(20 / len, p.Grade, 6));
        s.Undo();
        Assert.All(s.Plan.Pieces, p => Assert.Equal(0, p.Height));
    }

    [Fact]
    public void JointHeightTiltsBothNeighbours()
    {
        var (s, chain) = Line(3);
        var c = s.Connections.Find(chain[0].Id, 1)!;
        s.SetJointHeight(c.PieceId, c.Index, 12);
        Assert.Equal(0, chain[0].ConnectorHeights()[0], 3);
        Assert.Equal(12, chain[0].ConnectorHeights()[1], 3);
        Assert.Equal(12, chain[1].ConnectorHeights()[0], 3);
        Assert.Equal(0, chain[1].ConnectorHeights()[1], 3);
        // weiterhin verbunden, keine neuen offenen Enden
        Assert.Equal(2, s.Connections.Open.Count);
        Assert.Equal(12, s.Connections.Find(chain[0].Id, 1)!.Z, 3);
    }

    [Fact]
    public void JointHeightOnSwitchLiftsTheWholeSwitch()
    {
        var s = new PlanSession();
        s.NewPlan(new BoardSetup("H0", BoardShape.Rechteck, 3000, 1000, 500, 0));
        s.Plan.Pieces.Clear();
        var w = s.Plan.Add("H0|mc|24611");
        PlanEditing.PlaceAt(w, 0, 500, 500, 0);
        s.Commit();
        s.SetJointHeight(w.Id, 1, 25);
        Assert.All(w.ConnectorHeights(), h => Assert.Equal(25, h, 3));
    }
}
