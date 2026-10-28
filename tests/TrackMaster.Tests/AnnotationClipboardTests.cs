using TrackMaster.Core;

namespace TrackMaster.Tests;

public class AnnotationClipboardTests
{
    [Fact]
    public void Beschriftung_und_Mass_ueberstehen_Speichern_und_Laden()
    {
        var s = new PlanSession();
        s.AddLabel("Bahnhof Nord", 400, 300);
        s.AddDimension((0, 0), (1000, 0));
        var back = PlanSerializer.FromJson(PlanSerializer.ToJson(s.Plan), out _, out var newer);
        Assert.False(newer);
        var label = Assert.Single(back.Scenery, x => x.Kind == SceneryKind.Label);
        Assert.Equal("Bahnhof Nord", label.Name);
        var dim = Assert.Single(back.Scenery, x => x.Kind == SceneryKind.Dimension);
        Assert.Equal([(0.0, 0.0), (1000.0, 0.0)], dim.Points);
        Assert.Equal((500.0, 0.0), dim.Center());
    }

    [Fact]
    public void Neueres_Dateiformat_wird_erkannt()
    {
        var json = PlanSerializer.ToJson(SamplePlan.Build()).Replace($"\"format\": {PlanSerializer.CurrentFormat}", "\"format\": 99");
        PlanSerializer.FromJson(json, out _, out var newer);
        Assert.True(newer);
    }

    [Fact]
    public void Mass_rastet_an_Gleisende_ein()
    {
        var plan = SamplePlan.Build();
        var end = plan.Pieces[0].WorldConnectors()[0];
        var (x, y) = Annotation.Snap(plan, end.X + 3, end.Y - 2, 10);
        Assert.Equal(Math.Round(end.X, 1), x);
        Assert.Equal(Math.Round(end.Y, 1), y);
    }

    [Fact]
    public void Kopieren_und_Einfuegen_bringt_Gleise_mit_Elektrik_und_neuen_Kennungen()
    {
        var s = new PlanSession();
        var piece = s.Plan.Pieces[0];
        s.AddFeeder(piece.Id);
        s.Plan.Feeders[^1].Circuit = "Stromkreis 2";
        s.Selection.Clear();
        s.Selection.Add(piece.Id);
        var json = s.CopySelection();
        Assert.NotNull(json);

        var count = s.Plan.Pieces.Count;
        var feeders = s.Plan.Feeders.Count;
        Assert.True(s.Paste(json, (5000, 5000)));
        Assert.Equal(count + 1, s.Plan.Pieces.Count);
        Assert.Equal(feeders + 1, s.Plan.Feeders.Count);
        var pasted = s.Plan.Find(Assert.Single(s.Selection))!;
        Assert.NotEqual(piece.Id, pasted.Id);
        Assert.Equal(piece.Key, pasted.Key);
        Assert.Equal(5000, pasted.X, 6);
        Assert.Contains(s.Plan.Feeders, f => f.PieceId == pasted.Id && f.Circuit == "Stromkreis 2");
        Assert.Equal(s.Plan.Pieces.Count, s.Plan.Pieces.Select(p => p.Id).Distinct().Count());

        s.Undo();
        Assert.Equal(count, s.Plan.Pieces.Count);
    }

    [Fact]
    public void Einfuegen_in_andere_Spur_wird_abgelehnt()
    {
        var s = new PlanSession();
        s.Selection.Add(s.Plan.Pieces[0].Id);
        var json = s.CopySelection();
        var other = new PlanSession();
        other.NewPlan(BoardSetup.Default(other.Plan.GaugeId == "N" ? "H0" : "N"));
        Assert.False(other.Paste(json));
        Assert.False(other.Paste("kein Plan"));
    }
}
