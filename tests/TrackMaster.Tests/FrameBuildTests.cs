using TrackMaster.Core;
using Xunit;

namespace TrackMaster.Tests;

public class FrameBuildTests
{
    private const string Straight = "H0|mc|24188";

    private static LayoutPlan Plan() => new() { GaugeId = "H0", BoardWidth = 2000, BoardHeight = 1000 };

    /// <summary>Gerade Gleise hintereinander entlang x ab <paramref name="x"/>, auf Höhe <paramref name="height"/> mit Steigung <paramref name="grade"/>.</summary>
    private static void Line(LayoutPlan plan, double x, double y, int count, double height, double grade = 0)
    {
        var len = Catalog.Get(Straight).TrackLength;
        for (var i = 0; i < count; i++)
            plan.Pieces.Add(new PlacedPiece { Id = plan.Pieces.Count + 1, Key = Straight, X = x + i * len, Y = y, Height = height + grade * i * len, Grade = grade });
    }

    [Fact]
    public void Ebenes_Gleis_liegt_auf_jedem_gekreuzten_Spant_in_gleicher_Hoehe()
    {
        var plan = Plan();
        Line(plan, 0, 500, 10, 0);
        var spec = FrameBuild.Defaults(plan.Gauge);
        var result = FrameBuild.Build(plan, spec);
        var expected = spec.BaseHeight - FrameBuild.TrackBuildHeight(plan.Gauge) - spec.Thickness;
        Assert.NotEmpty(result.Ribs);
        foreach (var r in result.Ribs)
        {
            var s = Assert.Single(r.Supports);
            Assert.Equal(expected, s.HeightFrom, 1);
            Assert.Equal(expected, s.HeightTo, 1);
            // Gleis quer zum Spant: Auflage so breit wie die Trasse, mittig unter dem Gleis
            Assert.Equal(spec.TrasseWidth, s.To - s.From, 1);
            Assert.Equal(500, (s.From + s.To) / 2, 1);
            Assert.Empty(r.Cutouts);
            Assert.Equal(0, r.Start);
            Assert.Equal(1000, r.End);
        }
    }

    [Fact]
    public void Steigendes_Gleis_hebt_die_Auflagen_von_Spant_zu_Spant()
    {
        var plan = Plan();
        Line(plan, 0, 500, 10, 0, 0.02);
        var result = FrameBuild.Build(plan, FrameBuild.Defaults(plan.Gauge));
        var heights = result.Ribs.Select(r => r.Supports.Single().Mid).ToList();
        Assert.True(heights.Count >= 5);
        for (var i = 1; i < heights.Count; i++)
            Assert.Equal(result.Spec.Spacing * 0.02, heights[i] - heights[i - 1], 0);
    }

    [Fact]
    public void Untere_Trasse_unter_einer_oberen_bekommt_einen_Ausschnitt()
    {
        var plan = Plan();
        Line(plan, 0, 500, 10, 0);
        Line(plan, 0, 520, 10, 120);
        var spec = FrameBuild.Defaults(plan.Gauge);
        var result = FrameBuild.Build(plan, spec);
        var build = FrameBuild.TrackBuildHeight(plan.Gauge);
        foreach (var r in result.Ribs)
        {
            Assert.Equal(2, r.Supports.Count);
            var cut = Assert.Single(r.Cutouts);
            var lower = r.Supports.MinBy(s => s.Mid)!;
            var upper = r.Supports.MaxBy(s => s.Mid)!;
            Assert.Equal(lower.From, cut.From);
            Assert.Equal(lower.HeightFrom, cut.BottomFrom);
            // Durchfahrt: lichte Höhe der Spur über der unteren Schienenoberkante, darüber noch Holz unter der oberen Trasse
            Assert.True(cut.Top >= lower.Mid + spec.Thickness + build + plan.Gauge.ClearanceHeight - build - spec.Thickness - 0.5);
            Assert.True(cut.Top < upper.Mid);
            // Der Spant reicht bis unter die obere Trasse
            Assert.Equal(Math.Ceiling(upper.Mid), r.Height);
        }
        Assert.DoesNotContain(result.Notes, n => n.StartsWith("Achtung"));
    }

    [Fact]
    public void Zu_kleiner_Ebenenabstand_wird_gemeldet()
    {
        var plan = Plan();
        Line(plan, 0, 500, 10, 0);
        Line(plan, 0, 500, 10, 60);
        var result = FrameBuild.Build(plan, FrameBuild.Defaults(plan.Gauge));
        Assert.Contains(result.Notes, n => n.StartsWith("Achtung Spant"));
    }

    [Fact]
    public void Laengs_liegen_die_Spanten_entlang_der_Breite_und_parallele_Gleise_ergeben_keine_Ausschnitte()
    {
        var plan = Plan();
        Line(plan, 0, 300, 10, 0);
        Line(plan, 0, 700, 10, 0);
        var spec = FrameBuild.Defaults(plan.Gauge) with { Direction = FrameDirection.Laengs };
        var result = FrameBuild.Build(plan, spec);
        // Gleise laufen längs, also parallel zu den Spanten: nur Spanten, die unter einem Gleis liegen, tragen etwas
        Assert.All(result.Ribs, r => Assert.Equal(2000, r.Length));
        Assert.All(result.Ribs, r => Assert.Empty(r.Cutouts));
    }

    [Fact]
    public void Csv_und_Svg_enthalten_alle_Spanten()
    {
        var plan = Plan();
        Line(plan, 0, 500, 10, 0);
        Line(plan, 0, 520, 10, 120);
        var result = FrameBuild.Build(plan, FrameBuild.Defaults(plan.Gauge));
        var csv = result.ToCsv();
        var files = result.ToSvgFiles("Test");
        Assert.Equal(result.Ribs.Count, files.Count);
        foreach (var (r, f) in result.Ribs.Zip(files))
        {
            Assert.Contains($"\r\n{r.Number};", csv);
            Assert.Contains($"Spant {r.Number} ·", f.Content);
            SvgTests.AssertSinglePart(f);
        }
        Assert.Contains("Ausschnitt", csv);
    }
}

public class HelixCutTests
{
    [Theory]
    [InlineData(HelixShape.Rund, 1)]
    [InlineData(HelixShape.Oval, 2)]
    public void Zuschnitt_der_Holzliste_zeigt_jedes_Teil_einmal(HelixShape shape, int tracks)
    {
        var spec = new HelixSpec("H0|mc|24230", 2.5, 100, false, shape, "H0|mc|24188", 2, tracks, tracks == 2 ? 77.5 : 0, tracks == 2 ? "H0|mc|24330" : null);
        var m = Helix.Material(spec, Catalog.Gauges.First(g => g.Id == "H0"));
        var cut = Assert.IsType<HelixCut>(m.Cut);
        Assert.Equal(m.InnerBoardRadius, cut.InnerRadius, 0);
        Assert.Equal(m.OuterBoardRadius, cut.OuterRadius, 0);
        Assert.Empty(cut.Problems());
        var files = cut.ToSvgFiles("Test");
        Assert.Equal(cut.Segments + cut.Boards + cut.Laschen, files.Count);
        Assert.All(files, SvgTests.AssertSinglePart);
        Assert.Equal(tracks == 2 ? 2 : 1, cut.TrackRadii.Count);
        if (shape == HelixShape.Oval) Assert.True(cut.Boards > 0);
    }

    [Fact]
    public void Geaenderte_Masse_landen_in_der_Zeichnung()
    {
        var spec = new HelixSpec("H0|mc|24230", 2, 100, false);
        var cut = Helix.Material(spec, Catalog.Gauges.First(g => g.Id == "H0")).Cut! with { InnerRadius = 300, OuterRadius = 420, SegmentsPerCircle = 8, Segments = 16 };
        var files = cut.ToSvgFiles("Test");
        Assert.Contains("R 300–420 · 45°", files[0].Content);
        Assert.Equal(16 + cut.Laschen, files.Count);
    }

    [Fact]
    public void Unsinnige_Masse_werden_abgelehnt()
    {
        var cut = new HelixCut(400, 405, 1, 10, 0, 0, 0, 0, 0, 0, 10, [450]);
        var problems = cut.Problems();
        Assert.Contains(problems, p => p.StartsWith("Außenradius"));
        Assert.Contains(problems, p => p.StartsWith("Segmente je Kreis"));
    }
}
