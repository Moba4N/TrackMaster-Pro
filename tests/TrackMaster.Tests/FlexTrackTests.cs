using TrackMaster.Core;

namespace TrackMaster.Tests;

public class FlexTrackTests
{
    [Theory]
    [InlineData(0, 0, 0, 800, 0, 0)]        // gerade
    [InlineData(0, 0, 0, 600, 300, 0)]      // Versatz (S-Bogen)
    [InlineData(0, 0, 0, 500, 500, 90)]     // Viertelkreis
    [InlineData(0, 0, 30, 900, -200, -20)]  // schräg
    [InlineData(0, 0, 0, 0, 600, 180)]      // Kehre
    public void Verbinden_trifft_Ziel_mit_Richtung(double x0, double y0, double h0, double x1, double y1, double h1)
    {
        var segs = FlexTrack.Connect(new Pose(x0, y0, h0), new Pose(x1, y1, h1));
        Assert.NotEmpty(segs);
        var end = FlexTrack.EndPose(new Pose(x0, y0, h0), segs);
        Assert.Equal(x1, end.X, 1);
        Assert.Equal(y1, end.Y, 1);
        Assert.True(Math.Abs(Angles.Normalize(end.Heading - h1)) < 0.05, $"Richtung {end.Heading}");
    }

    [Fact]
    public void Biegen_endet_im_Punkt()
    {
        var start = new Pose(100, 100, 0);
        var segs = FlexTrack.Bend(start, 600, 300);
        var end = FlexTrack.EndPose(start, segs);
        Assert.Equal(600, end.X, 3);
        Assert.Equal(300, end.Y, 3);
        Assert.Single(segs);
        Assert.Equal(1, segs[0].Turn);
    }

    [Fact]
    public void Geformtes_Flexgleis_hat_gueltigen_Schluessel()
    {
        var segs = FlexTrack.Connect(new Pose(0, 0, 0), new Pose(600, 300, 0));
        var (key, _) = FlexTrack.Key("H0", "flex-mk", segs);
        var def = Catalog.Get(key);
        Assert.True(def.IsFlex);
        Assert.Equal(FlexTrack.Length(segs), def.TrackLength, 1);
        var c = TrackGeometry.LocalConnectors(def, false);
        Assert.Equal(600, c[1].X, 1);
        Assert.Equal(300, c[1].Y, 1);
    }

    [Fact]
    public void Luecke_schliessen_verbindet_beide_Gleisenden()
    {
        var s = new PlanSession();
        s.NewPlan(new BoardSetup("H0", BoardShape.Rechteck, 3000, 2000, 600, 0));
        s.Attach("H0|mc|24188", (500, 500));
        var a = s.Plan.Pieces[0];
        s.Active = null;
        s.Attach("H0|mc|24188", (1400, 900));
        var b = s.Plan.Pieces[1];
        b.Rotation = 20;
        s.Commit();
        s.Select([a.Id, b.Id], add: false);
        s.Active = null;
        s.ConnectEnds(s.CurrentFlex);
        Assert.Equal(3, s.Plan.Pieces.Count);
        var flex = s.Plan.Pieces[2];
        Assert.True(flex.Def.IsFlex);
        Assert.All(s.Connections.All.Where(c => c.PieceId == flex.Id), c => Assert.False(c.IsOpen));
    }

    [Fact]
    public void Flexgleis_formen_laesst_das_andere_Ende_liegen()
    {
        var s = new PlanSession();
        s.NewPlan(new BoardSetup("H0", BoardShape.Rechteck, 3000, 2000, 600, 0));
        var stock = s.CurrentFlex;
        var p = s.PlaceFlex(stock, new Pose(200, 200, 0), [Segment.Line(500)], null, null)!;
        var fixedEnd = p.WorldConnectors()[0];
        Assert.True(s.BeginReshape(p.Id, 1));
        var segs = s.Reshape(700, 450, null);
        s.EndReshape(segs);
        var piece = s.Plan.Find(p.Id)!;
        var c = piece.WorldConnectors();
        Assert.Equal(fixedEnd.X, c[0].X, 3);
        Assert.Equal(fixedEnd.Y, c[0].Y, 3);
        Assert.Equal(700, c[1].X, 1);
        Assert.Equal(450, c[1].Y, 1);
        // Rückgängig stellt die gerade Form wieder her
        s.Undo();
        Assert.Equal(700, s.Plan.Find(p.Id)!.WorldConnectors()[1].X, 1);
    }

    [Fact]
    public void Roco_Fleischmann_ohne_Bettung_hat_beide_Flexgleise()
    {
        var gauge = Catalog.GetGauge("N");
        var flex = Catalog.Flexes(gauge, Catalog.GetSystem("N", "rf-n"));
        Assert.Contains(flex, f => f.Number == "22200" && f.Length == 730);
        Assert.Contains(flex, f => f.Number == "22201" && f.Length == 730);
        var stock = flex.Single(f => f.Number == "22201");
        Assert.Equal("22201", Catalog.Get(Catalog.FlexStraightKey("N", 300, stock.SystemId)).Number);
    }

    [Fact]
    public void Flexgleis_ziehen_vom_Gleisende_schliesst_tangential_an()
    {
        var s = new PlanSession();
        s.NewPlan(new BoardSetup("H0", BoardShape.Rechteck, 3000, 2000, 600, 0));
        var first = s.PlaceFlex(s.CurrentFlex, new Pose(200, 300, 0), [Segment.Line(400)], null, null)!;
        var end = s.Connections.Open.Single(c => c.PieceId == first.Id && c.Index == 1);
        Assert.Same(end, s.FlexTarget(605, 302, 10));
        var draft = s.BeginFlexDraw(605, 302, end);
        draft.Update(1000, 600, null);
        var flex = s.FinishFlexDraw(draft)!;
        Assert.False(s.Connections.Find(first.Id, 1)!.IsOpen);
        Assert.Equal(1000, flex.WorldConnectors()[1].X, 1);
        Assert.Equal(600, flex.WorldConnectors()[1].Y, 1);
    }

    [Fact]
    public void Flexgleis_frei_begonnen_rastet_am_Zielgleis_ein()
    {
        var s = new PlanSession();
        s.NewPlan(new BoardSetup("H0", BoardShape.Rechteck, 3000, 2000, 600, 0));
        var target = s.PlaceFlex(s.CurrentFlex, new Pose(1200, 800, 0), [Segment.Line(400)], null, null)!;
        var draft = s.BeginFlexDraw(500, 300, null);
        var end = s.FlexTarget(1201, 799, 10)!;
        Assert.Equal(target.Id, end.PieceId);
        draft.Update(1201, 799, end);
        s.FinishFlexDraw(draft);
        Assert.False(s.Connections.Find(target.Id, 0)!.IsOpen);
    }
}
