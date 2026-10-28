using System.Text.Json;
using TrackMaster.Core;

namespace TrackMaster.Tests;

public class TrainRouteTests
{
    /// <summary>Beispielplan mit einem Signal vor der ersten spitz befahrenen Weiche.</summary>
    private static (LayoutPlan Plan, PlacedPiece Switch, Signal Signal) PlanWithSignal()
    {
        var plan = SamplePlan.Build();
        var conn = Connectivity.Analyze(plan);
        foreach (var sw in plan.Pieces.Where(p => p.Def.Kind == PieceKind.Switch && p.Def.Drives > 0))
            if (conn.Find(sw.Id, 0)?.Link is { } before && plan.Find(before.PieceId)?.Def.Kind != PieceKind.Switch)
            {
                var sig = new Signal { Id = plan.NextId++, PieceId = before.PieceId, Index = before.Index, Name = "A" };
                plan.Signals.Add(sig);
                return (plan, sw, sig);
            }
        throw new InvalidOperationException("Beispielplan ohne passende Weiche");
    }

    [Fact]
    public void Weiche_hinter_dem_Signal_ergibt_Fahrstrassen_fuer_beide_Stellungen()
    {
        var (plan, sw, sig) = PlanWithSignal();
        var routes = TrainRoutes.Find(plan).Where(r => r.StartSignalId == sig.Id).ToList();
        Assert.True(routes.Count >= 2);
        var positions = routes.Select(r => r.Settings.First(s => s.PieceId == sw.Id).Path).Distinct().ToList();
        Assert.Equal(2, positions.Count);
        Assert.All(routes, r => Assert.Equal(sw.Id, r.PieceIds[0]));
        Assert.Contains(routes, r => r.Settings.Any(s => s.Position == "gerade"));
    }

    [Fact]
    public void Gegensignal_beendet_die_Fahrstrasse()
    {
        var (plan, sw, sig) = PlanWithSignal();
        var straight = TrainRoutes.Passages(sw).First(x => x.From == 0 && TrainRoutes.PositionName(sw, x.Path) == "gerade");
        var end = new Signal { Id = plan.NextId++, PieceId = sw.Id, Index = straight.To, Name = "B" };
        plan.Signals.Add(end);
        var routes = TrainRoutes.Find(plan).Where(r => r.StartSignalId == sig.Id).ToList();
        var r = Assert.Single(routes, r => r.EndSignalId == end.Id);
        Assert.Equal("A → B", r.Name);
        Assert.Equal([sw.Id], r.PieceIds);
        Assert.Contains("A → B", TrainRoutes.ToText(plan, routes));
    }

    [Fact]
    public void Export_ist_gueltiges_JSON_mit_Adressen()
    {
        var (plan, sw, _) = PlanWithSignal();
        sw.Address = 12;
        using var doc = JsonDocument.Parse(TrainRoutes.ToJson(plan));
        var root = doc.RootElement;
        Assert.Equal("steuerung-1", root.GetProperty("format").GetString());
        Assert.Contains(root.GetProperty("switches").EnumerateArray(), s => s.GetProperty("id").GetInt32() == sw.Id && s.GetProperty("address").GetInt32() == 12);
        Assert.True(root.GetProperty("routes").GetArrayLength() >= 2);
        Assert.StartsWith("Fahrstraße;", TrainRoutes.ToCsv(TrainRoutes.Find(plan)));
    }
}
