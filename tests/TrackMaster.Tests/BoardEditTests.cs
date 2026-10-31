using TrackMaster.Core;
using Xunit;

namespace TrackMaster.Tests;

public class BoardEditTests
{
    private const string Straight = "H0|mc|24188";

    private static PlanSession Session(LayoutPlan plan)
    {
        var s = new PlanSession();
        s.Replace(plan, null);
        return s;
    }

    [Fact]
    public void Modul_teilen_laengs_und_quer()
    {
        var rows = new List<ModuleEdit> { new(0, "A", 0, 0, 1200, 600) };
        var (split, error) = ModuleLayout.Split(rows, 0, acrossWidth: true, 500);
        Assert.Null(error);
        Assert.Equal(2, split!.Count);
        Assert.Equal(500, split[0].Width);
        Assert.Equal((500.0, 700.0), (split[1].X, split[1].Width));
        (split, _) = ModuleLayout.Split(rows, 0, acrossWidth: false, 200);
        Assert.Equal((200.0, 400.0), (split![0].Depth, split[1].Depth));
        Assert.NotNull(ModuleLayout.Split(rows, 0, true, 20).Error);
        // Wieder zusammenführen ergibt das alte Rechteck
        var (merged, _) = ModuleLayout.Merge(split, [0, 1]);
        Assert.Equal((1200.0, 600.0), (merged![0].Width, merged[0].Depth));
    }

    [Fact]
    public void Modul_an_lange_oder_kurze_Seite_anfuegen()
    {
        var rows = new List<ModuleEdit> { new(0, "A", 0, 0, 1200, 600), new(1, "B", 3000, 0, 800, 400) };
        var (r, error) = ModuleLayout.Join(rows, 1, 0, 0, longSide: true);
        Assert.Null(error);
        var b = r![1];
        Assert.Equal((1200.0, 0.0), (b.X, b.Y));
        Assert.Equal((400.0, 800.0), (b.Width, b.Depth));          // mit der langen Seite (800) an der rechten Kante
        (r, _) = ModuleLayout.Join(rows, 1, 0, 1, longSide: false); // unten, kurze Seite (400) an der Unterkante
        Assert.Equal((0.0, 600.0, 400.0, 800.0), (r![1].X, r[1].Y, r[1].Width, r[1].Depth));
        (r, _) = ModuleLayout.Join(rows, 1, 0, 1, longSide: true);
        Assert.Equal((800.0, 400.0), (r![1].Width, r[1].Depth));
    }

    [Fact]
    public void Klick_mit_Platte_ziehen_meldet_das_Teil_zum_Bemassen()
    {
        var s = Session(new LayoutPlan { BoardWidth = 2000, BoardHeight = 1000 });
        (int Module, int Leg)? clicked = null;
        s.BoardPartClicked += (m, l) => clicked = (m, l);
        Assert.True(s.BeginBoardDrag(1000, 500, 10));
        s.EndBoardDrag();
        Assert.Equal((-1, -1), clicked);
        Assert.False(s.History.CanUndo);
        s.SetBoardSize(2400, 900);
        Assert.Equal((2400.0, 900.0), (s.Plan.BoardWidth, s.Plan.BoardHeight));
    }

    [Theory]
    [InlineData(BoardShape.Rechteck)]
    [InlineData(BoardShape.L)]
    [InlineData(BoardShape.U)]
    [InlineData(BoardShape.T)]
    [InlineData(BoardShape.Z)]
    [InlineData(BoardShape.E)]
    [InlineData(BoardShape.G)]
    public void Anlage_um_90_Grad_drehen_dreht_Platte_und_Gleise_gemeinsam(BoardShape shape)
    {
        var plan = new LayoutPlan { BoardShape = shape, BoardWidth = 3000, BoardHeight = 2000, BoardDepth = 500, BoardLegLeft = shape == BoardShape.U ? 1200 : 0 };
        var s = Session(plan);
        var before = BoardGeometry.Rects(s.Plan).Select(r => (r.X0, r.Y0, r.X1, r.Y1)).ToList();
        var r0 = BoardGeometry.Rects(s.Plan)[0];
        s.Drop(Straight, (r0.X0 + r0.X1) / 2 - 94, (r0.Y0 + r0.Y1) / 2, 0);
        var mid = s.Plan.Pieces[0].Middle();
        Assert.True(BoardGeometry.Contains(s.Plan, mid.X, mid.Y));
        for (var k = 0; k < 4; k++)
        {
            s.RotateBoard();
            var m = s.Plan.Pieces[0].Middle();
            Assert.True(BoardGeometry.Contains(s.Plan, m.X, m.Y), $"Drehung {k + 1}");
        }
        // Viermal gedreht: wieder wie vorher
        var after = BoardGeometry.Rects(s.Plan).Select(r => (r.X0, r.Y0, r.X1, r.Y1)).ToList();
        Assert.Equal(before.OrderBy(r => r).ToList(), after.OrderBy(r => r).ToList());
        var end = s.Plan.Pieces[0].Middle();
        Assert.Equal(mid.X, end.X, 3);
        Assert.Equal(mid.Y, end.Y, 3);
    }

    [Fact]
    public void Modul_drehen_nimmt_die_Gleise_mit()
    {
        var plan = new LayoutPlan();
        ModuleLayout.Single(plan, 1200, 600);
        var s = Session(plan);
        s.Drop(Straight, 300, 300, 0);
        s.RotateBoard(0);
        Assert.Equal((600.0, 1200.0), (s.Plan.Modules[0].Width, s.Plan.Modules[0].Depth));
        var m = s.Plan.Pieces[0].Middle();
        Assert.True(BoardGeometry.Contains(s.Plan, m.X, m.Y));
        Assert.Equal(90, s.Plan.Pieces[0].Rotation, 3);
    }

    [Fact]
    public void Gelaende_waechst_mit_der_Grundhoehe_seiner_Ebene()
    {
        var plan = new LayoutPlan { BoardWidth = 2000, BoardHeight = 1000 };
        plan.Levels.Add(new PlanLevel { Name = "Berg", Height = 120 });
        var hill = new SceneryItem { Kind = SceneryKind.Terrain, Height = 30, Level = 1, Points = [(200, 200), (1200, 200), (1200, 900), (200, 900)] };
        plan.Scenery.Add(hill);
        Assert.Equal(150, TerrainField.GroundAt(plan, 700, 550), 0);
        plan.Levels[1].Height = 200;
        Assert.Equal(230, TerrainField.GroundAt(plan, 700, 550), 0);
    }

    [Fact]
    public void Hochliegende_Gleise_bekommen_mit_Gelaende_einen_Damm()
    {
        var plan = new LayoutPlan { BoardWidth = 2000, BoardHeight = 1000 };
        var p = plan.Add(Straight);
        PlanEditing.PlaceAt(p, 0, 500, 500, 0);
        p.Height = 80;
        var with = new Scene3DModel(plan, scenery: true);
        var without = new Scene3DModel(plan, scenery: false);
        Assert.Contains(with.Meshes.Keys, k => k.Color == new Rgb(0x86, 0x9A, 0x58) || k.Color == new Rgb(0x7A, 0x8C, 0x50));
        Assert.DoesNotContain(without.Meshes.Keys, k => k.Color == new Rgb(0x86, 0x9A, 0x58));
    }
}
