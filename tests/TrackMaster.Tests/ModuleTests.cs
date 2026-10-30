using TrackMaster.Core;
using Xunit;

namespace TrackMaster.Tests;

public class BoardShapeTests
{
    [Theory]
    [InlineData(BoardShape.E, 3000, 2100, 500)]
    [InlineData(BoardShape.G, 3000, 2400, 500)]
    public void E_und_G_Form_haben_die_Flaeche_ihrer_Rechtecke_und_einen_geschlossenen_Umriss(BoardShape shape, double w, double h, double d)
    {
        var rects = BoardGeometry.Rects(shape, w, h, d, 0);
        var outline = Assert.Single(BoardGeometry.Outlines(shape, w, h, d, 0));
        // Fläche des Umrisses (Gaußsche Trapezformel) = Summe der Rechtecke, die Rechtecke überlappen also nicht
        var area = Math.Abs(outline.Select((p, i) => p.X * outline[(i + 1) % outline.Count].Y - outline[(i + 1) % outline.Count].X * p.Y).Sum()) / 2;
        Assert.Equal(rects.Sum(r => (r.X1 - r.X0) * (r.Y1 - r.Y0)), area, 1);
        Assert.All(rects, r => Assert.True(r.X0 >= 0 && r.Y0 >= 0 && r.X1 <= w && r.Y1 <= h));
    }

    [Fact]
    public void E_Form_hat_drei_Arme_und_Luecken_dazwischen()
    {
        var plan = new LayoutPlan { BoardShape = BoardShape.E, BoardWidth = 3000, BoardHeight = 2100, BoardDepth = 500 };
        Assert.True(BoardGeometry.Contains(plan, 2500, 250));   // oberer Arm
        Assert.True(BoardGeometry.Contains(plan, 2500, 1050));  // mittlerer Arm
        Assert.True(BoardGeometry.Contains(plan, 2500, 1850));  // unterer Arm
        Assert.False(BoardGeometry.Contains(plan, 2500, 650));  // Lücke
        Assert.True(BoardGeometry.Contains(plan, 250, 650));    // Rücken
    }

    [Fact]
    public void Schenkeltiefe_der_G_Form_bleibt_unter_einem_Viertel_der_Tiefe()
        => Assert.True(BoardGeometry.Depth(BoardShape.G, 3000, 2000, 900, 0) < 500);
}

public class ModuleTests
{
    private const string Straight = "H0|mc|24188";

    private static LayoutPlan TwoModules()
    {
        var plan = LayoutPlan.Empty("H0");
        ModuleLayout.Single(plan, 1000, 500);
        plan.Modules.Add(ModuleLayout.NextModule(plan));
        ModuleLayout.Fit(plan);
        return plan;
    }

    [Fact]
    public void Neues_Modul_kommt_rechts_daneben_und_die_Platte_waechst_mit()
    {
        var plan = TwoModules();
        Assert.Equal(1000, plan.Modules[1].X);
        Assert.Equal(2000, plan.BoardWidth);
        Assert.Equal(500, plan.BoardHeight);
        Assert.Equal(2, BoardGeometry.Outlines(plan).Count);
        Assert.True(BoardGeometry.Contains(plan, 1500, 250));
        Assert.False(BoardGeometry.Contains(plan, 2500, 250));
    }

    [Fact]
    public void Verschobenes_Modul_nimmt_seine_Gleise_mit_und_rastet_an_der_Kante_ein()
    {
        var plan = TwoModules();
        var a = plan.Add(Straight); a.X = 200; a.Y = 250;
        var b = plan.Add(Straight); b.X = 1200; b.Y = 250;
        // Modul 2 unter Modul 1 schieben, 12 mm daneben: rastet an der Unterkante ein
        ModuleLayout.Apply(plan, [new(0, "A", 0, 0, 1000, 500), new(1, "B", 5, 512, 1000, 500)]);
        Assert.Equal(0, plan.Modules[1].X, 3);
        Assert.Equal(500, plan.Modules[1].Y, 3);
        Assert.Equal(200, a.X, 3);
        Assert.Equal(200, b.X, 3);
        Assert.Equal(750, b.Y, 3);
        Assert.Equal(1000, plan.BoardHeight, 3);
        Assert.Equal("B", plan.Modules[1].Name);
    }

    [Fact]
    public void Modul_links_von_null_verschiebt_die_ganze_Anlage()
    {
        var plan = TwoModules();
        var a = plan.Add(Straight); a.X = 200; a.Y = 250;
        ModuleLayout.Apply(plan, [new(0, "A", 0, 0, 1000, 500), new(1, "B", -1000, 0, 1000, 500)]);
        Assert.Equal(0, plan.Modules.Min(m => m.X), 3);
        Assert.Equal(1200, a.X, 3);
    }

    [Fact]
    public void Plan_als_Modul_ansetzen_uebernimmt_Gleise_Elektrik_und_Gelaende()
    {
        var plan = LayoutPlan.Empty("H0");
        plan.BoardWidth = 1200; plan.BoardHeight = 600;
        plan.Add(Straight);
        var other = LayoutPlan.Empty("H0");
        other.BoardWidth = 900; other.BoardHeight = 600;
        var p = other.Add(Straight); p.X = 100; p.Y = 300;
        other.Feeders.Add(new Feeder { Id = other.NextId++, PieceId = p.Id });
        other.Scenery.Add(new SceneryItem { Id = other.NextId++, Kind = SceneryKind.Tree, X = 50, Y = 50, Width = 40 });
        Assert.Null(ModuleLayout.Append(plan, other, "Bahnhof"));
        Assert.Equal(BoardShape.Module, plan.BoardShape);
        Assert.Equal(["Modul 1", "Bahnhof"], plan.Modules.Select(m => m.Name));
        Assert.Equal(2100, plan.BoardWidth);
        Assert.Equal(2, plan.Pieces.Count);
        var copy = plan.Pieces[1];
        Assert.Equal(1300, copy.X, 3);
        Assert.Equal(copy.Id, Assert.Single(plan.Feeders).PieceId);
        Assert.Equal(1250, Assert.Single(plan.Scenery).X, 3);
        Assert.Equal(plan.Pieces.Count + plan.Feeders.Count + plan.Scenery.Count, plan.Pieces.Select(x => x.Id).Concat(plan.Feeders.Select(x => x.Id)).Concat(plan.Scenery.Select(x => x.Id)).Distinct().Count());
    }

    [Fact]
    public void Plan_anderer_Spur_wird_nicht_angesetzt()
        => Assert.NotNull(ModuleLayout.Append(LayoutPlan.Empty("H0"), LayoutPlan.Empty("N"), "x"));

    [Fact]
    public void Module_ueberstehen_Speichern_und_Laden()
    {
        var plan = TwoModules();
        plan.Modules[1].Name = "Schattenbahnhof";
        var back = PlanSerializer.FromJson(PlanSerializer.ToJson(plan), out _);
        Assert.Equal(BoardShape.Module, back.BoardShape);
        Assert.Equal(["Modul 1", "Schattenbahnhof"], back.Modules.Select(m => m.Name));
        Assert.Equal(2000, back.BoardWidth);
    }

    [Fact]
    public void Gleis_ueber_der_Modulgrenze_wird_gemeldet()
    {
        var plan = TwoModules();
        var len = Catalog.Get(Straight).TrackLength;
        var over = plan.Add(Straight); over.X = 1000 - len / 2; over.Y = 250;
        var onEdge = plan.Add(Straight); onEdge.X = 1000 - len; onEdge.Y = 400;
        Assert.Equal([over.Id], ModuleLayout.PiecesAcrossJoints(plan).Select(p => p.Id));
        Assert.Contains(PlanCheck.Run(plan), i => i.Category == "Modulgrenze");
    }
}
