using TrackMaster.Core;

namespace TrackMaster.Tests;

public class BoardTests
{
    private static double PolygonArea(IReadOnlyList<(double X, double Y)> p)
        => Math.Abs(p.Select((q, i) => q.X * p[(i + 1) % p.Count].Y - p[(i + 1) % p.Count].X * q.Y).Sum()) / 2;

    [Theory]
    [InlineData(BoardShape.Rechteck, 0)]
    [InlineData(BoardShape.L, 0)]
    [InlineData(BoardShape.L, 1)]
    [InlineData(BoardShape.U, 2)]
    [InlineData(BoardShape.T, 3)]
    [InlineData(BoardShape.Z, 1)]
    [InlineData(BoardShape.Ring, 0)]
    public void Umriss_und_Flaechen_passen_zusammen(BoardShape shape, int turn)
    {
        var rects = BoardGeometry.Rects(shape, 3000, 2000, 600, turn);
        var outlines = BoardGeometry.Outlines(shape, 3000, 2000, 600, turn);
        var rectArea = rects.Sum(r => (r.X1 - r.X0) * (r.Y1 - r.Y0));
        var outlineArea = PolygonArea(outlines[0]) - outlines.Skip(1).Sum(PolygonArea);
        Assert.Equal(outlineArea, rectArea, 3);
        // Alles liegt in den Außenmaßen und füllt sie aus
        Assert.All(rects, r => Assert.True(r.X0 >= -1e-9 && r.Y0 >= -1e-9 && r.X1 <= 3000 + 1e-9 && r.Y1 <= 2000 + 1e-9));
        Assert.Equal(3000, rects.Max(r => r.X1), 6);
        Assert.Equal(2000, rects.Max(r => r.Y1), 6);
    }

    [Fact]
    public void U_Form_hat_offene_Mitte()
    {
        var plan = new LayoutPlan { BoardShape = BoardShape.U, BoardWidth = 3000, BoardHeight = 2000, BoardDepth = 600 };
        Assert.True(BoardGeometry.Contains(plan, 300, 300));
        Assert.True(BoardGeometry.Contains(plan, 1500, 1800));
        Assert.False(BoardGeometry.Contains(plan, 1500, 500));
        plan.BoardTurn = 2;
        Assert.True(BoardGeometry.Contains(plan, 1500, 200));
        Assert.False(BoardGeometry.Contains(plan, 1500, 1500));
    }

    [Fact]
    public void Planpruefung_meldet_Gleis_in_der_offenen_Mitte()
    {
        var plan = new LayoutPlan { GaugeId = "H0", BoardShape = BoardShape.Ring, BoardWidth = 3000, BoardHeight = 2000, BoardDepth = 500 };
        var p = plan.Add("H0|mc|24188");
        p.X = 1400; p.Y = 1000;
        Assert.Contains(PlanCheck.Run(plan), i => i.Category == "Plattenrand");
        p.Y = 250;
        Assert.DoesNotContain(PlanCheck.Run(plan), i => i.Category == "Plattenrand");
    }

    [Fact]
    public void Form_wird_gespeichert_und_geladen()
    {
        var plan = new LayoutPlan { GaugeId = "N", BoardShape = BoardShape.L, BoardWidth = 2400, BoardHeight = 1600, BoardDepth = 450, BoardTurn = 3 };
        var back = PlanSerializer.FromJson(PlanSerializer.ToJson(plan), out _);
        Assert.Equal(BoardShape.L, back.BoardShape);
        Assert.Equal(450, back.BoardDepth);
        Assert.Equal(3, back.BoardTurn);
        var rect = PlanSerializer.FromJson(PlanSerializer.ToJson(new LayoutPlan()), out _);
        Assert.Equal(BoardShape.Rechteck, rect.BoardShape);
    }

    [Fact]
    public void Neuer_Plan_uebernimmt_Spur_und_Form()
    {
        var s = new PlanSession();
        s.NewPlan(new BoardSetup("TT", BoardShape.U, 3000, 2000, 5000, 1));
        Assert.Equal("TT", s.Plan.GaugeId);
        Assert.Equal(BoardShape.U, s.Plan.BoardShape);
        Assert.Equal(1, s.Plan.BoardTurn);
        Assert.True(s.Plan.BoardDepth < 1000);
    }

    [Fact]
    public void Beliebige_Masse_beim_Eintippen_stuerzen_nicht_ab()
    {
        // Werte, wie sie beim Tippen Ziffer für Ziffer entstehen (auch knapp über 100 mm und 0)
        var values = new double[] { 0, 1, 3, 30, 99, 100, 101, 102, 150, 300, 2000, 3000, 50000 };
        foreach (var shape in BoardGeometry.All)
            foreach (var w in values)
                foreach (var h in values)
                    foreach (var d in new double[] { 0, 6, 60, 600, 6000, double.NaN })
                        for (var t = 0; t < 4; t++)
                        {
                            BoardGeometry.Rects(shape, w, h, d, t, 50, 0);
                            BoardGeometry.Outlines(shape, w, h, d, t, 0, 5000);
                        }
    }

    [Fact]
    public void Tausenderpunkt_wird_gelesen()
    {
        Assert.Equal(3000, Fmt.Parse("3.000"));
        Assert.Equal(12500, Fmt.Parse("12.500"));
        Assert.Equal(1.5, Fmt.Parse("1.5"));
    }

    [Fact]
    public void U_Form_mit_kuerzerem_Schenkel()
    {
        var plan = new LayoutPlan { BoardShape = BoardShape.U, BoardWidth = 3000, BoardHeight = 2000, BoardDepth = 600, BoardLegRight = 1200 };
        Assert.True(BoardGeometry.Contains(plan, 2700, 1000));   // rechter Schenkel unten
        Assert.False(BoardGeometry.Contains(plan, 2700, 500));   // oberhalb des kurzen Schenkels frei
        Assert.True(BoardGeometry.Contains(plan, 300, 200));     // linker Schenkel voll
        var outline = BoardGeometry.Outlines(plan)[0];
        Assert.Equal(BoardGeometry.Area(plan) * 1e6, PolygonArea(outline), 3);
        var back = PlanSerializer.FromJson(PlanSerializer.ToJson(plan), out _);
        Assert.Equal(1200, back.BoardLegRight);
        Assert.Equal(0, back.BoardLegLeft);
    }
}
