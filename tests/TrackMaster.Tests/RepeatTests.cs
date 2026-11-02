using TrackMaster.Core;
using Xunit;

namespace TrackMaster.Tests;

public class RepeatTests
{
    private static PlanSession NewSession()
    {
        var s = new PlanSession();
        s.NewPlan(new BoardSetup("H0", BoardShape.Rechteck, 3000, 2000, 600, 0));
        return s;
    }

    [Fact]
    public void RepeatAttachesSameTrackAtActiveEnd()
    {
        var s = NewSession();
        s.Attach("H0|mc|24188", (500, 500));
        Assert.True(s.RepeatLast((0, 0)));
        Assert.True(s.RepeatLast((0, 0)));
        Assert.Equal(3, s.Plan.Pieces.Count);
        Assert.All(s.Plan.Pieces, p => Assert.Equal("H0|mc|24188", p.Def.Key));
        // alle hintereinander verbunden: nur die beiden äußeren Enden sind offen
        Assert.Equal(2, s.Connections.Open.Count);
    }

    [Fact]
    public void RepeatKeepsMirroredCurve()
    {
        var s = NewSession();
        var curve = s.Plan.Gauge.Systems.SelectMany(x => x.Pieces).First(p => p.Kind == PieceKind.Curve && p.Key.StartsWith("H0|mc|"));
        s.Attach(curve.Key, (1500, 1000));
        s.Flip();
        Assert.True(s.Plan.Pieces[0].Flipped);
        Assert.True(s.RepeatLast((0, 0)));
        Assert.True(s.Plan.Pieces[1].Flipped);
    }

    [Fact]
    public void RepeatRepeatsFlexTrack()
    {
        var s = NewSession();
        var flex = s.PlaceFlex(s.CurrentFlex, new Pose(200, 300, 0), [Segment.Line(500)], null, null)!;
        Assert.True(s.RepeatLast((0, 0)));
        Assert.Equal(2, s.Plan.Pieces.Count);
        Assert.Equal(flex.Def.Key, s.Plan.Pieces[1].Def.Key);
    }

    [Fact]
    public void RepeatWithoutPreviousTrackDoesNothing()
    {
        var s = NewSession();
        Assert.False(s.RepeatLast((0, 0)));
        Assert.Empty(s.Plan.Pieces);
    }
}
