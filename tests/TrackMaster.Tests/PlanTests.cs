using TrackMaster.Core;

namespace TrackMaster.Tests;

public class PlanTests
{
    [Fact]
    public void Beispielplan_ist_geschlossen()
    {
        var plan = SamplePlan.Build();
        var c = Connectivity.Analyze(plan);
        Assert.Equal(29, plan.Pieces.Count);
        Assert.Empty(c.Open);
        Assert.Equal(2, plan.Statistics(c).Switches);
    }

    [Fact]
    public void Speichern_und_Laden_ergibt_denselben_Plan()
    {
        var plan = SamplePlan.Build();
        var json = PlanSerializer.ToJson(plan);
        var back = PlanSerializer.FromJson(json, out var skipped);
        Assert.Equal(0, skipped);
        Assert.Equal(plan.Name, back.Name);
        Assert.Equal(plan.Pieces.Count, back.Pieces.Count);
        Assert.Empty(Connectivity.Analyze(back).Open);
    }

    [Fact]
    public void Datei_aus_der_Browser_Version_wird_gelesen()
    {
        const string json = """{"app":"TrackMaster Pro","format":1,"name":"Test","gauge":"H0","board":{"w":1500,"h":800},"pieces":[{"key":"H0|mc|24188","x":100,"y":200,"rot":0,"flip":false,"z":0},{"key":"H0|xx|unbekannt","x":0,"y":0,"rot":0,"flip":false,"z":0}]}""";
        var plan = PlanSerializer.FromJson(json, out var skipped);
        Assert.Equal(1, skipped);
        Assert.Single(plan.Pieces);
        Assert.Equal(1500, plan.BoardWidth);
    }

    [Fact]
    public void Fremde_Datei_wird_abgelehnt()
        => Assert.Throws<InvalidDataException>(() => PlanSerializer.FromJson("""{"foo":1}""", out _));

    [Fact]
    public void Einrasten_dreht_und_verschiebt_das_Gleis_an_das_freie_Ende()
    {
        var plan = LayoutPlan.Empty("H0");
        var a = PlanEditing.Attach(plan, "H0|mc|24188", null, (500, 500));
        var b = plan.Add("H0|mc|24230");
        var aEnd = a.WorldConnectors()[1];
        b.X = aEnd.X + 5; b.Y = aEnd.Y - 4; b.Rotation = 20;
        Assert.True(PlanEditing.Snap(plan, new HashSet<int> { b.Id }, 15));
        var c = Connectivity.Analyze(plan);
        Assert.NotNull(c.Find(b.Id, 0)!.Link);
    }

    [Fact]
    public void Anschluss_wechseln_legt_die_Weiche_anders_herum_an()
    {
        var plan = LayoutPlan.Empty("H0");
        var a = PlanEditing.Attach(plan, "H0|mc|24188", null, (500, 500));
        var w = PlanEditing.Attach(plan, "H0|mc|24611", Connectivity.Analyze(plan).Find(a.Id, 1), (0, 0));
        var (next, error) = PlanEditing.SwapAnchor(plan, w);
        Assert.Null(error);
        Assert.NotNull(next);
        var c = Connectivity.Analyze(plan);
        Assert.Null(c.Find(w.Id, 0)!.Link);
        Assert.NotNull(c.Find(w.Id, 1)!.Link);
    }

    [Fact]
    public void Rueckgaengig_und_Wiederholen()
    {
        var plan = SamplePlan.Build();
        var history = new EditHistory();
        history.Record(plan);
        plan.Pieces.RemoveAt(0);
        var undone = history.Undo(plan)!;
        Assert.Equal(29, undone.Pieces.Count);
        var redone = history.Redo(undone)!;
        Assert.Equal(28, redone.Pieces.Count);
    }

    [Fact]
    public void Stueckliste_zaehlt_Artikel()
    {
        var rows = BillOfMaterials.Build(SamplePlan.Build());
        Assert.Equal(12, rows.Single(r => r.Def.Number == "24230").Count);
        Assert.Equal(2, rows.Single(r => r.Def.Number == "24977").Count);
        Assert.Contains("24230", BillOfMaterials.ToCsv(SamplePlan.Build()));
    }

    [Fact]
    public void Gleis_einer_anderen_Spur_wird_abgelehnt()
    {
        var plan = LayoutPlan.Empty("N");
        Assert.Throws<InvalidOperationException>(() => PlanEditing.Attach(plan, "H0|mc|24188", null, (0, 0)));
    }

    [Fact]
    public void Svg_Export_enthaelt_Platte_und_Gleise()
    {
        var svg = SvgExport.Build(SamplePlan.Build());
        Assert.Contains("<svg", svg);
        Assert.Contains("2000", svg);
        Assert.Contains("stroke=\"#4E5257\"", svg);
    }
}
