using TrackMaster.Core;
using Xunit;

namespace TrackMaster.Tests;

public class CatalogPlanCheckTests
{
    [Fact]
    public void ShortSpacersAreNoCollision()
    {
        // Gerade, zwei kurze Ausgleichsstücke (8 + 2 mm), Bogen: die Planprüfung darf Gerade und Bogen nicht als Kollision melden
        var s = new PlanSession();
        s.NewPlan(new BoardSetup("H0", BoardShape.Rechteck, 2000, 1250, 600, 0));
        foreach (var nr in new[] { "42410", "98003", "98000", "42423", "42423" }) s.Attach("H0|rocoline|" + nr, (300, 300));
        Assert.Equal(5, s.Plan.Pieces.Count);
        Assert.DoesNotContain(PlanCheck.Run(s.Plan), i => i.Category == "Kollision");
    }

    [Theory]
    [InlineData("mk")]
    [InlineData("lgb")]
    public void EverySystemHasBufferStop(string systemId)
        => Assert.Contains(Catalog.Gauges.SelectMany(g => g.Systems).First(x => x.Id == systemId).Pieces, d => d.Kind == PieceKind.Buffer);
}
