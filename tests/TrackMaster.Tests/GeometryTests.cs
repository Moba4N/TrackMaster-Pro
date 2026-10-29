using TrackMaster.Core;

namespace TrackMaster.Tests;

public class GeometryTests
{
    [Fact]
    public void Zwoelf_R2_Boegen_ergeben_einen_geschlossenen_Kreis()
    {
        var plan = LayoutPlan.Empty("H0");
        var first = PlanEditing.Attach(plan, "H0|mc|24230", null, (1000, 600));
        var end = Connectivity.Analyze(plan).Find(first.Id, 1);
        for (var i = 0; i < 11; i++)
        {
            var p = PlanEditing.Attach(plan, "H0|mc|24230", end, (0, 0));
            end = Connectivity.Analyze(plan).Find(p.Id, 1);
        }
        var c = Connectivity.Analyze(plan);
        Assert.Empty(c.Open);
        Assert.Equal(12, plan.Pieces.Count);
    }

    [Fact]
    public void Bogen_R2_30_Grad_endet_an_der_richtigen_Stelle()
    {
        var def = Catalog.Get("H0|mc|24230");
        var end = TrackGeometry.LocalLegs(def, false)[0].End;
        Assert.Equal(437.5 * Math.Sin(Math.PI / 6), end.X, 6);
        Assert.Equal(-437.5 * (1 - Math.Cos(Math.PI / 6)), end.Y, 6); // Linksbogen: nach oben
        Assert.Equal(-30, end.Heading, 6);
    }

    [Fact]
    public void Gespiegelter_Bogen_geht_nach_rechts()
    {
        var def = Catalog.Get("H0|mc|24230");
        var end = TrackGeometry.LocalLegs(def, true)[0].End;
        Assert.True(end.Y > 0);
        Assert.Equal(30, end.Heading, 6);
    }

    [Fact]
    public void Weiche_hat_drei_Anschluesse_und_Kreuzung_vier()
    {
        Assert.Equal(3, TrackGeometry.LocalConnectors(Catalog.Get("H0|mc|24611"), false).Count);
        Assert.Equal(4, TrackGeometry.LocalConnectors(Catalog.Get("H0|mc|24649"), false).Count);
        Assert.Single(TrackGeometry.LocalConnectors(Catalog.Get("H0|mc|24977"), false));
    }

    [Fact]
    public void Abstand_zum_Bogen_wird_richtig_berechnet()
    {
        var leg = new Leg(0, 0, 0, true, 0, 100, 90, 1); // Rechtsbogen, Mittelpunkt (0,100)
        var mid = leg.At(0.5);
        Assert.Equal(0, leg.DistanceTo(mid.X, mid.Y), 6);
        Assert.Equal(10, leg.DistanceTo(mid.X * 0.9, 100 + (mid.Y - 100) * 0.9), 6);
        Assert.True(leg.DistanceTo(-50, 100) > 40); // außerhalb des Bogens
    }

    [Fact]
    public void Deutsche_Zahlen_werden_gelesen()
    {
        Assert.Equal(1234.5, Fmt.Parse("1.234,5"));
        Assert.Equal(12.5, Fmt.Parse("12.5"));
        Assert.Null(Fmt.Parse("abc"));
        Assert.Equal("188,3", Fmt.Num(188.3));
    }
}
