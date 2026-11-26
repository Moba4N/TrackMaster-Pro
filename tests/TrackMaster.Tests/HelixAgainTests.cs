using TrackMaster.Core;
using Xunit;

namespace TrackMaster.Tests;

public class HelixAgainTests
{
    private static (PlanSession Session, HelixSpec Spec) Built()
    {
        var session = new PlanSession();
        session.Replace(new LayoutPlan { GaugeId = "H0", BoardWidth = 3000, BoardHeight = 2000 }, null);
        var curve = session.Plan.Gauge.Systems.SelectMany(s => s.Pieces)
            .Where(p => p.Kind == PieceKind.Curve && 360 % p.Angle < 0.01 && p.Radius > 300).OrderBy(p => p.Radius).First();
        var spec = new HelixSpec(curve.Key, 2, 80, true, Construction: HelixConstruction.Spanten);
        Assert.NotNull(session.BuildHelix(spec, (1500, 1000)));
        return (session, spec);
    }

    [Fact]
    public void Holzliste_einer_gebauten_Wendel_laesst_sich_spaeter_wieder_aufrufen()
    {
        var (session, spec) = Built();
        var again = session.HelixMaterialAgain();
        Assert.NotNull(again);
        Assert.Equal(Helix.Material(spec, session.Plan.Gauge).ToCsv(), again.ToCsv());
    }

    [Fact]
    public void Wendel_bleibt_beim_Speichern_und_Laden_erhalten()
    {
        var (session, spec) = Built();
        var ids = session.Plan.Helices.Single().PieceIds;
        var back = PlanSerializer.FromJson(PlanSerializer.ToJson(session.Plan), out _);
        var h = Assert.Single(back.Helices);
        Assert.Equal(spec, h.Spec);
        Assert.Equal(ids, h.PieceIds);
        Assert.All(h.PieceIds, id => Assert.NotNull(back.Find(id)));
    }

    [Fact]
    public void Ohne_Wendel_oder_nach_dem_Loeschen_gibt_es_keine_Holzliste()
    {
        var (session, _) = Built();
        session.SelectAll();
        session.Delete();
        Assert.Null(session.HelixMaterialAgain());
        Assert.Empty(PlanSerializer.FromJson(PlanSerializer.ToJson(session.Plan), out _).Helices);
        session.Undo();
        Assert.NotNull(session.HelixMaterialAgain());
    }
}
