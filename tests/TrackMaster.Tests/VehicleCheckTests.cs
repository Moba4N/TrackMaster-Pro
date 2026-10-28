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

public class RideTrainTests
{
    [Fact]
    public void Weichenstellung_aendert_den_Fahrweg()
    {
        var plan = SamplePlan.Build();
        RidePath? path = null;
        int? start = null;
        foreach (var p in plan.Pieces)
        {
            path = RidePath.Build(plan, p.Id);
            if (path.Switches.Any(w => w.Distance > 10)) { start = p.Id; break; }
        }
        Assert.NotNull(start);
        var first = path!.Switches.First(w => w.Distance > 10);
        Assert.Equal(first, path.NextSwitch(first.Distance - 5));
        var turned = RidePath.Build(plan, start, new Dictionary<int, int> { [first.PieceId] = 1 });
        Assert.Contains(turned.Switches, w => w.PieceId == first.PieceId && Math.Abs(w.Distance - first.Distance) < 1e-3);
        // Bis zur Weiche gleich, danach anders
        var before = first.Distance - 1;
        Assert.Equal(path.PointAt(before), turned.PointAt(before));
        var after = first.Distance + 200;
        var (a, b) = (path.PointAt(after), turned.PointAt(after));
        Assert.True(Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) > 1);
    }

    [Fact]
    public void Zug_hat_Lok_und_Wagen()
    {
        var plan = SamplePlan.Build();
        var path = RidePath.Build(plan, null);
        var train = new Train3D(plan);
        var meshes = train.Build(path, train.Length);
        Assert.Contains(meshes, m => m.Key.Color == Train3D.LocoColor && !m.Value.IsEmpty);
        Assert.Contains(meshes, m => m.Key.Color == Train3D.CoachColor && m.Value.TriangleCount >= 3 * 12);
        Assert.True(meshes.Values.SelectMany(m => m.Points).All(p => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z)));
    }
}
