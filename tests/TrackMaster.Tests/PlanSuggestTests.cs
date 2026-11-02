using TrackMaster.Core;
using Xunit;
using Xunit.Abstractions;

namespace TrackMaster.Tests;

public class PlanSuggestTests(ITestOutputHelper output)
{
    private static LayoutPlan Board(double w = 2000, double h = 1250) => new() { GaugeId = "H0", BoardWidth = w, BoardHeight = h };

    private static Dictionary<string, int> Stock(params (string Nr, int Count)[] items)
        => items.ToDictionary(i => "H0|mc|" + i.Nr, i => i.Count);

    /// <summary>Etwa der Inhalt zweier Startpackungen und eines Ergänzungspakets.</summary>
    private static Dictionary<string, int> Starter() => Stock(
        ("24130", 24), ("24230", 12), ("24188", 12), ("24172", 4), ("24071", 4), ("24064", 2),
        ("24611", 2), ("24612", 2), ("24224", 2), ("24977", 2), ("24624", 1));

    private void Dump(IReadOnlyList<PlanSuggestion> list)
    {
        foreach (var s in list)
            output.WriteLine($"{s.Name}: {s.Description} – {s.Used} Gleise, {s.Width} × {s.Height} mm");
    }

    [Fact]
    public void StarterSetGivesSeveralClosedLayouts()
    {
        var board = Board();
        var stock = Starter();
        var list = PlanSuggest.Build(board, stock);
        Dump(list);
        Assert.True(list.Count >= 3);
        Assert.Contains(list, s => s.Name == "Oval");
        foreach (var s in list)
        {
            // nie mehr Teile als vorhanden
            foreach (var (key, n) in s.Counts) Assert.True(n <= stock[key], $"{s.Name}: {key} {n} > {stock[key]}");
            var plan = Board();
            plan.Pieces.AddRange(s.Pieces);
            Assert.Empty(Connectivity.Analyze(plan).Open);
            Assert.DoesNotContain(PlanCheck.Run(plan), i => i.Level == IssueLevel.Fehler);
            Assert.True(s.Width <= 2000 && s.Height <= 1250);
        }
    }

    [Fact]
    public void CrossingGivesFigureEight()
    {
        // Kreuzung 48,6°: je Schleife 228,6° = je Hälfte 3 × 30° + 24,3°
        var list = PlanSuggest.Build(Board(3000, 1300), Stock(("24130", 12), ("24224", 4), ("24649", 1), ("24188", 12), ("24172", 4), ("24064", 4), ("24071", 4), ("24077", 4), ("24094", 4)));
        Dump(list);
        var eight = Assert.Single(list, s => s.Name == "Acht");
        var plan = Board(3000, 1300);
        plan.Pieces.AddRange(eight.Pieces);
        Assert.Empty(Connectivity.Analyze(plan).Open);
    }

    [Fact]
    public void TooFewCurvesGivesNothing()
    {
        Assert.Empty(PlanSuggest.Build(Board(), Stock(("24130", 5), ("24188", 10))));
    }

    [Fact]
    public void SmallBoardRejectsLargeRadius()
    {
        // R2 (515 mm) passt nicht auf 900 mm Tiefe
        var list = PlanSuggest.Build(Board(2000, 900), Stock(("24230", 12), ("24188", 6)));
        Assert.Empty(list);
    }

    [Fact]
    public void PortraitBoardTurnsLayout()
    {
        var list = PlanSuggest.Build(Board(1250, 2000), Stock(("24130", 12), ("24188", 8)));
        var oval = Assert.Single(list);
        Assert.True(oval.Height > oval.Width);
    }

    [Fact]
    public void ApplyIsUndoable()
    {
        var s = new PlanSession();
        s.NewPlan(new BoardSetup("H0", BoardShape.Rechteck, 2000, 1250, 600, 0));
        s.Attach("H0|mc|24188", (500, 500));
        var oval = PlanSuggest.Build(s.Plan, Stock(("24130", 12), ("24188", 8)))[0];
        s.ApplySuggestion(oval, replace: true);
        Assert.Equal(20, s.Plan.Pieces.Count);
        Assert.Empty(s.Connections.Open);
        s.Undo();
        Assert.Single(s.Plan.Pieces);
    }
}
