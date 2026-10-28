using System.Diagnostics;
using TrackMaster.Core;
using Xunit;
using Xunit.Abstractions;

namespace TrackMaster.Tests;

/// <summary>Große Anlage: rund 3000 Gleise aus kopierten Beispielanlagen. Die Zeiten stehen in der Testausgabe.</summary>
public class LargePlanTests(ITestOutputHelper output)
{
    private static LayoutPlan Build(int minPieces)
    {
        var s = new PlanSession();
        s.SelectAll();
        var json = s.CopySelection()!;
        var w = s.Plan.Pieces.Max(p => p.X) - s.Plan.Pieces.Min(p => p.X) + 400;
        var h = s.Plan.Pieces.Max(p => p.Y) - s.Plan.Pieces.Min(p => p.Y) + 400;
        for (var i = 1; s.Plan.Pieces.Count < minPieces; i++)
            Assert.True(s.Paste(json, ((i % 8) * w, (i / 8) * h)));
        return s.Plan;
    }

    [Fact]
    public void Three_thousand_pieces_stay_fast()
    {
        var plan = Build(3000);
        var times = new List<(string Name, long Ms)>();
        T Time<T>(string name, Func<T> f)
        {
            var sw = Stopwatch.StartNew();
            var r = f();
            times.Add((name, sw.ElapsedMilliseconds));
            return r;
        }
        var connections = Time("Verbindungen", () => Connectivity.Analyze(plan));
        var issues = Time("Planprüfung", () => PlanCheck.Run(plan));
        var json = Time("Speichern (JSON)", () => PlanSerializer.ToJson(plan));
        var loaded = Time("Laden (JSON)", () => PlanSerializer.FromJson(json, out _));
        var bom = Time("Stückliste", () => BillOfMaterials.Build(plan));
        var routes = Time("Fahrstraßen", () => TrainRoutes.Find(plan));
        var scene = Time("3D-Modell", () => new Scene3DModel(plan));
        var ride = Time("Mitfahrt-Strecke", () => RidePath.Build(plan, null));

        output.WriteLine($"{plan.Pieces.Count} Gleise, {scene.TriangleCount} Dreiecke, {routes.Count} Fahrstraßen, {issues.Count} Meldungen");
        foreach (var (name, ms) in times) output.WriteLine($"{name}: {ms} ms");

        Assert.True(plan.Pieces.Count >= 3000);
        Assert.Equal(plan.Pieces.Count, loaded.Pieces.Count);
        Assert.NotEmpty(bom);
        Assert.NotNull(connections);
        Assert.NotNull(ride);
        // Großzügige Grenze, damit langsame Build-Rechner nicht scheitern; Ausreißer zeigen trotzdem ein Problem
        Assert.All(times, t => Assert.True(t.Ms < 15000, $"{t.Name}: {t.Ms} ms"));
    }
}
