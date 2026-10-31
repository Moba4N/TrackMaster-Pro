using TrackMaster.Core;
using Xunit;

namespace TrackMaster.Tests;

public class BenchworkTests
{
    [Fact]
    public void Rechteckplatte_bekommt_Rahmen_Beine_und_Materialliste()
    {
        var plan = new LayoutPlan { BoardWidth = 2000, BoardHeight = 1000 };
        var spec = new BenchworkSpec { Height = 1000, TopThickness = 10, FrameThickness = 18, FrameHeight = 70, CrossSpacing = 500, LegSpacing = 1200 };
        var b = Benchwork.Build(plan, spec);
        // 2 lange Zargen über die volle Breite, 2 Stirnzargen dazwischen
        var zargen = b.Pieces.Where(p => p.Kind == BenchKind.Zarge).Select(p => p.Length).OrderBy(x => x).ToList();
        Assert.Equal([964, 964, 2000, 2000], zargen);
        // Querriegel höchstens 500 mm auseinander: 1982 mm innen → 4 Felder, 3 Riegel
        Assert.Equal(3, b.Pieces.Count(p => p.Kind == BenchKind.Querriegel));
        // 2 Felder zwischen den Beinen je Seite → 3 Beinpaare
        Assert.Equal(6, b.LegCount);
        var leg = b.Pieces.First(p => p.Kind == BenchKind.Bein);
        Assert.Equal(1000 - 10 - spec.FootHeight, leg.Length, 3);
        Assert.Equal(-spec.Height + spec.FootHeight, leg.Z0, 3);
        Assert.Contains(b.Materials, m => m.Item == "Stellfüße" && m.Quantity == 6);
        Assert.Contains(b.Materials, m => m.Item == "Sperrholzplatte" && m.Quantity == 1);
        Assert.All(b.Pieces, p => Assert.True(p.Z1 <= 0.001));
    }

    [Fact]
    public void Benachbarte_Plattenteile_teilen_sich_keine_doppelten_Beine()
    {
        var plan = new LayoutPlan { BoardShape = BoardShape.L, BoardWidth = 3000, BoardHeight = 2000, BoardDepth = 600 };
        var b = Benchwork.Build(plan, new BenchworkSpec());
        var legs = b.Pieces.Where(p => p.Kind == BenchKind.Bein).ToList();
        for (var i = 0; i < legs.Count; i++)
            for (var j = i + 1; j < legs.Count; j++)
                Assert.False(legs[i].X0 < legs[j].X1 - 0.1 && legs[i].X1 > legs[j].X0 + 0.1 && legs[i].Y0 < legs[j].Y1 - 0.1 && legs[i].Y1 > legs[j].Y0 + 0.1);
    }

    [Fact]
    public void L_Bein_besteht_aus_zwei_Latten()
    {
        var plan = new LayoutPlan { BoardWidth = 1200, BoardHeight = 600 };
        var b = Benchwork.Build(plan, new BenchworkSpec { LegStyle = BenchLegStyle.LBein });
        Assert.Equal(4, b.LegCount);
        Assert.Equal(8, b.Pieces.Count(p => p.Kind == BenchKind.Bein));
    }

    [Fact]
    public void Lange_Zargen_werden_als_zu_stossen_gemeldet_und_Latten_gezaehlt()
    {
        var plan = new LayoutPlan { BoardWidth = 4000, BoardHeight = 800 };
        var b = Benchwork.Build(plan, new BenchworkSpec { StockLength = 2500 });
        Assert.Contains(b.Notes, n => n.Contains("4.000") || n.Contains("4000"));
        Assert.Equal(2, Benchwork.Bars([1200, 1200, 1000], 2500));
        Assert.Equal(2, Benchwork.Bars([4000], 2500));
    }

    [Fact]
    public void Unterbau_wird_gespeichert_und_in_3D_gezeigt()
    {
        var s = new PlanSession();
        s.Replace(LayoutPlan.Empty("H0"), null);
        s.SetBenchwork(new BenchworkSpec { Height = 900, LegStyle = BenchLegStyle.LBein });
        var back = PlanSerializer.FromJson(PlanSerializer.ToJson(s.Plan), out _);
        Assert.NotNull(back.Benchwork);
        Assert.Equal(900, back.Benchwork!.Height);
        Assert.Equal(BenchLegStyle.LBein, back.Benchwork.LegStyle);
        var without = new Scene3DModel(LayoutPlan.Empty("H0")).TriangleCount;
        Assert.True(new Scene3DModel(back).TriangleCount > without);
        s.Undo();
        Assert.Null(s.Plan.Benchwork);
    }

    [Fact]
    public void Zu_niedrige_Hoehe_wird_abgelehnt()
        => Assert.NotEmpty(Benchwork.Validate(new BenchworkSpec { Height = 250 }));
}
