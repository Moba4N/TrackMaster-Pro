using TrackMaster.Core;
using Xunit;

namespace TrackMaster.Tests;

public class TurntableCatalogTests
{
    [Fact]
    public void EverySystemOffersATurntable()
    {
        foreach (var g in Catalog.Gauges)
            foreach (var s in g.Systems.Where(s => !Catalog.IsFlexSystem(s.Id)))
                Assert.True(s.Pieces.Any(p => p.Kind == PieceKind.Turntable), $"{g.Id} {s.Name}: keine Drehscheibe");
    }

    [Theory]
    [InlineData("H0|mc|7286", "H0|mc|24188")]
    [InlineData("H0|geo|6152", null)]
    [InlineData("H0|rocoline|6154", null)]
    [InlineData("N|rf-n|9152", null)]
    [InlineData("0|std0|DS-0", null)]
    [InlineData("G|lgb|DS-G", null)]
    public void TrackAttachesToTurntablePort(string table, string? straightKey)
    {
        var def = Catalog.Get(table);
        var plan = new LayoutPlan { GaugeId = def.GaugeId, BoardWidth = 4000, BoardHeight = 3000 };
        var t = plan.Add(table);
        PlanEditing.PlaceAt(t, 0, 2000, 1500, 0);
        var key = straightKey ?? Catalog.GetSystem(def.GaugeId, def.SystemId)!.Pieces.First(p => p.Kind == PieceKind.Straight).Key;
        var conn = Connectivity.Analyze(plan);
        var port = conn.Find(t.Id, 3)!;
        var s = PlanEditing.Attach(plan, key, port, (0, 0));
        conn = Connectivity.Analyze(plan);
        Assert.False(conn.Find(s.Id, 0)!.IsOpen);
        // Ports liegen gleichmäßig rundum
        var ports = TrackGeometry.LocalConnectors(def, false);
        Assert.Equal(360.0 / def.Angle, ports.Count, 3);
    }
}
