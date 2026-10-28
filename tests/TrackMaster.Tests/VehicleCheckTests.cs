using TrackMaster.Core;

namespace TrackMaster.Tests;

public class VehicleCheckTests
{
    private static PlanSession TwoCurves(bool reverse)
    {
        var s = new PlanSession();
        s.NewPlan(BoardSetup.Default("H0"));
        s.Quick(PieceKind.Curve, null, (1000, 600));
        s.Quick(PieceKind.Curve, null, (1000, 600));
        if (reverse) s.Flip();
        return s;
    }

    [Fact]
    public void Vorgabelaenge_folgt_dem_Massstab()
    {
        Assert.Equal(303, VehicleDefaults.Length(Catalog.GetGauge("H0")));
        Assert.Equal(165, VehicleDefaults.Length(Catalog.GetGauge("N")));
        Assert.Equal(172, VehicleDefaults.Length(Catalog.GetGauge("H0m")));
    }

    [Fact]
    public void Ausschwenken_waechst_mit_kleinerem_Radius()
    {
        var (i1, o1) = VehicleDefaults.Swing(360, 303);
        var (i2, o2) = VehicleDefaults.Swing(600, 303);
        Assert.True(i1 > i2 && o1 > o2);
        Assert.InRange(i1, 10, 20);
        Assert.InRange(o1, 10, 25);
    }

    [Fact]
    public void Enger_Radius_wird_gemeldet_und_haengt_von_der_Fahrzeuglaenge_ab()
    {
        var s = TwoCurves(false);
        var r = s.Plan.Pieces[0].Def.Radius;
        s.Plan.VehicleLength = Math.Ceiling(r / VehicleDefaults.RadiusFactor) + 20;
        Assert.Contains(PlanCheck.Run(s.Plan), i => i.Category == "Radius");
        s.Plan.VehicleLength = Math.Floor(r / VehicleDefaults.RadiusFactor) - 20;
        Assert.DoesNotContain(PlanCheck.Run(s.Plan), i => i.Category == "Radius");
        Assert.DoesNotContain(PlanCheck.Run(s.Plan), i => i.Category == "S-Bogen");
    }

    [Fact]
    public void S_Bogen_wird_erkannt()
    {
        var s = TwoCurves(true);
        Assert.Equal(2, s.Plan.Pieces.Count);
        Assert.Contains(PlanCheck.Run(s.Plan), i => i.Category == "S-Bogen");
    }

    [Fact]
    public void Steigung_im_Bogen_wird_gemeldet()
    {
        var s = TwoCurves(false);
        foreach (var p in s.Plan.Pieces) p.Grade = 0.025;
        Assert.Contains(PlanCheck.Run(s.Plan), i => i.Category == "Steigung im Bogen");
        foreach (var p in s.Plan.Pieces) p.Grade = 0.015;
        Assert.DoesNotContain(PlanCheck.Run(s.Plan), i => i.Category == "Steigung im Bogen");
    }

    [Fact]
    public void Fahrzeuglaenge_wird_gespeichert()
    {
        var plan = SamplePlan.Build();
        plan.VehicleLength = 250;
        Assert.Equal(250, PlanSerializer.FromJson(PlanSerializer.ToJson(plan), out _).VehicleLength);
    }
}
