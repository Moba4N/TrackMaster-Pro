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

    [Fact]
    public void Weichenname_steht_in_Fahrstrassen_und_im_Export_als_Text_und_CSV()
    {
        var (plan, sw, sig) = PlanWithSignal();
        sw.Address = 7;
        sw.Name = "W Einfahrt";
        var copy = PlanSerializer.FromJson(PlanSerializer.ToJson(plan), out _);
        Assert.Equal("W Einfahrt", copy.Find(sw.Id)!.Name);
        var routes = TrainRoutes.Find(plan).Where(r => r.StartSignalId == sig.Id).ToList();
        Assert.Contains("W Einfahrt (Adresse 7)", TrainRoutes.ToText(plan, routes));
        Assert.Contains(";W Einfahrt;7;", TrainRoutes.ToCsv(routes));
        var csv = TrainRoutes.ControlCsv(plan);
        Assert.StartsWith("\uFEFF\"Art\";", csv);
        Assert.Contains("\"W Einfahrt\";", csv);
        Assert.Contains("\"7\"", csv);
        var text = TrainRoutes.ControlText(plan);
        Assert.Contains("W Einfahrt", text);
        Assert.Contains("Adresse 7", text);
        using var doc = JsonDocument.Parse(TrainRoutes.ToJson(plan));
        Assert.Contains(doc.RootElement.GetProperty("switches").EnumerateArray(), s => s.GetProperty("name").GetString() == "W Einfahrt");
    }

    [Fact]
    public void Nummerierung_benennt_Weichen_ohne_eigenen_Namen()
    {
        var session = new PlanSession();
        session.Replace(SamplePlan.Build(), null);
        var switches = session.Plan.Pieces.Where(p => p.Def.Drives > 0).ToList();
        Assert.True(switches.Count >= 2);
        switches[0].Name = "Einfahrt";
        session.NumberElectrics();
        Assert.Equal("Einfahrt", switches[0].Name);
        Assert.All(switches.Skip(1), p => Assert.Matches("^W\\d+$", p.Name));
        Assert.Equal(switches.Count - 1, switches.Skip(1).Select(p => p.Name).Distinct().Count());
    }
}
