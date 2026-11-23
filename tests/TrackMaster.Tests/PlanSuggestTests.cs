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

    /// <summary>Prüft jeden Vorschlag: Bestand eingehalten (feste Teile), geschlossen, auf der Platte, ohne Fehler.</summary>
    private static void AssertValid(LayoutPlan board, IReadOnlyDictionary<string, int> stock, IReadOnlyList<PlanSuggestion> list)
    {
        foreach (var s in list)
        {
            foreach (var (key, n) in s.Counts)
                if (!Catalog.Get(key).IsFlex) Assert.True(n <= stock[key], $"{s.Name}: {key} {n} > {stock[key]}");
            var plan = new LayoutPlan { GaugeId = board.GaugeId, BoardWidth = board.BoardWidth, BoardHeight = board.BoardHeight };
            plan.Pieces.AddRange(s.Pieces);
            Assert.Empty(Connectivity.Analyze(plan).Open);
            Assert.DoesNotContain(PlanCheck.Run(plan), i => i.Level == IssueLevel.Fehler);
        }
    }

    /// <summary>Typischer Bestand eines Systems: Bögen der zwei kleinsten Radien, Geraden, je Weiche zwei, Prellböcke.</summary>
    private static (LayoutPlan Board, Dictionary<string, int> Stock) Typical(string systemId, int flexBars = 0)
    {
        var (gauge, system) = Catalog.Gauges.SelectMany(g => g.Systems.Select(s => (g, s))).First(x => x.s.Id == systemId);
        var usable = system.Pieces.Where(PlanSuggest.IsUsable).ToList();
        var radii = usable.Where(d => d.Kind == PieceKind.Curve).Select(d => Math.Round(d.Radius, 1)).Distinct().Order().Take(2).ToList();
        var stock = usable.Where(d => d.Kind != PieceKind.Crossing).ToDictionary(d => d.Key, d => d.Kind switch
        {
            PieceKind.Curve => radii.Contains(Math.Round(d.Radius, 1)) ? 24 : 4,
            PieceKind.Straight => 12,
            _ => 2,
        });
        if (flexBars > 0) stock[PlanSuggest.FlexStockKey(Catalog.Flex(gauge, system).SystemId)] = flexBars;
        var r = radii.DefaultIfEmpty(gauge.TrackWidth * 30).Max();
        return (new LayoutPlan { GaugeId = gauge.Id, BoardWidth = Math.Round(r * 5.5), BoardHeight = Math.Round(r * 2.8) }, stock);
    }

    [Theory]
    [InlineData("rf-n")]      // Weichen mit Abzweig plus Gerade (Stränge), fehlten bisher ganz
    [InlineData("minitrix")]
    [InlineData("mk")]        // Prellbock fehlte im Katalog
    [InlineData("lgb")]
    [InlineData("tillig-h0")] // kein Bogen im Weichenwinkel: Abstellgleis schräg
    [InlineData("rocoline")]  // kurze Ausgleichsstücke D2–D12
    public void EverySystemGivesSidings(string systemId)
    {
        var (board, stock) = Typical(systemId);
        var list = PlanSuggest.Build(board, stock);
        Dump(list);
        Assert.Contains(list, s => s.Name == "Oval");
        Assert.Contains(list, s => s.Name == "Oval mit Abstellgleis");
        Assert.Contains(list, s => s.Name == "Oval mit zwei Abstellgleisen");
        AssertValid(board, stock, list);
    }

    [Theory]
    [InlineData("peco-55-n")]
    [InlineData("weinert")]
    [InlineData("tillig-h0m")]
    public void FlexOnlySystemsUseFlexTrack(string systemId)
    {
        // Ohne Flexgleis gibt es keine Bögen und damit keinen Vorschlag
        var (board, stock) = Typical(systemId);
        Assert.Empty(PlanSuggest.Build(board, stock));
        (board, stock) = Typical(systemId, flexBars: 20);
        var list = PlanSuggest.Build(board, stock);
        Dump(list);
        Assert.Contains(list, s => s.Name == "Oval mit Abstellgleis");
        Assert.Contains(list, s => s.Name == "Oval mit Ausweiche");
        Assert.All(list, s => Assert.Contains("Flexgleis", s.Description));
        AssertValid(board, stock, list);
        // nie mehr Flexgleis als die Stangen (mit 5 % Verschnitt) hergeben
        var bar = Catalog.Flex(Catalog.GetGauge(board.GaugeId), Catalog.GetSystem(board.GaugeId, systemId)).Length;
        Assert.All(list, s => Assert.True(s.Pieces.Where(p => p.Def.IsFlex).Sum(p => p.Def.TrackLength) <= 20 * bar * 0.95));
    }

    [Fact]
    public void FlexBudgetLimitsLayout()
    {
        // Zwei Stangen reichen für kein Oval in H0
        var (board, stock) = Typical("weinert", flexBars: 2);
        Assert.Empty(PlanSuggest.Build(board, stock));
    }

    [Fact]
    public void TurnoutsWithPathsAreOffered()
    {
        var s = new PlanSession();
        s.NewPlan(new BoardSetup("N", BoardShape.Rechteck, 1500, 800, 400, 0));
        s.SelectSystem("rf-n");
        Assert.Contains(s.SuggestPieces(), p => p.Def.Number == "22261");
        // Bogenweichen mit zwei Radien und Dreiwegweichen werden genannt, aber nicht verbaut
        Assert.Contains(s.SuggestUnused(), d => d.Number == "22273");
        Assert.Contains(s.SuggestUnused(), d => d.Number == "22247");
        Assert.NotEmpty(s.SuggestFlex());
    }

    [Theory]
    [InlineData("mc")]
    [InlineData("tc")]
    public void CurvedTurnoutsGiveSidings(string systemId)
    {
        // Nur Bogenweichen (C-Gleis R1, 30° mit 77,5 mm Gerade) im Bestand: das Abstellgleis zweigt im Bogen ab
        var (board, stock) = Typical(systemId);
        foreach (var k in stock.Keys.Where(k => Catalog.Get(k) is { Kind: PieceKind.Switch } d && PlanSuggest.CurvedTurnout(d) is null).ToList()) stock.Remove(k);
        var curved = stock.Keys.Where(k => Catalog.Get(k).Kind == PieceKind.Switch).ToList();
        Assert.Equal(4, curved.Count);
        var list = PlanSuggest.Build(board, stock);
        AssertValid(board, stock, list);
        Assert.Contains(list, s => s.Name == "Oval mit Abstellgleis" && s.Counts.Keys.Any(curved.Contains));
        Assert.Contains(list, s => s.Name == "Oval mit zwei Abstellgleisen" && s.Counts.Keys.Count(curved.Contains) >= 1);
    }

    [Fact]
    public void LeftAndRightTurnoutsAreBothUsed()
    {
        // Eine linke und eine rechte Weiche: beide werden verbaut, keine wird gespiegelt
        var board = new LayoutPlan { GaugeId = "H0", BoardWidth = 2400, BoardHeight = 1200 };
        var stock = new Dictionary<string, int>
        {
            ["H0|mc|24611"] = 1, ["H0|mc|24612"] = 1, ["H0|mc|24130"] = 12, ["H0|mc|24188"] = 14, ["H0|mc|24172"] = 4,
            ["H0|mc|24977"] = 2, ["H0|mc|24224"] = 2,
        };
        var list = PlanSuggest.Build(board, stock);
        AssertValid(board, stock, list);
        Assert.All(list.SelectMany(s => s.Pieces).Where(p => p.Def.Kind == PieceKind.Switch), p => Assert.False(p.Flipped));
        Assert.Contains(list, s => s.Name == "Oval mit zwei Abstellgleisen" && s.Counts.ContainsKey("H0|mc|24611") && s.Counts.ContainsKey("H0|mc|24612"));
        Assert.Contains(list, s => s.Name == "Oval mit Ausweiche" && s.Counts.ContainsKey("H0|mc|24611") && s.Counts.ContainsKey("H0|mc|24612"));
    }

    [Fact]
    public void MoreTurnoutsGiveLoopAndSiding()
    {
        var board = new LayoutPlan { GaugeId = "H0", BoardWidth = 2400, BoardHeight = 1200 };
        var stock = new Dictionary<string, int>
        {
            ["H0|mc|24611"] = 2, ["H0|mc|24612"] = 2, ["H0|mc|24130"] = 12, ["H0|mc|24188"] = 14, ["H0|mc|24172"] = 4,
            ["H0|mc|24071"] = 4, ["H0|mc|24064"] = 4, ["H0|mc|24977"] = 2, ["H0|mc|24224"] = 4,
        };
        var list = PlanSuggest.Build(board, stock);
        AssertValid(board, stock, list);
        Assert.Contains(list, s => s.Name == "Oval mit Ausweiche und Abstellgleis" && s.Pieces.Count(p => p.Def.Kind == PieceKind.Switch) == 3);
    }

    private static Dictionary<string, int> BigMc() => new()
    {
        ["H0|mc|24611"] = 3, ["H0|mc|24612"] = 3, ["H0|mc|24130"] = 24, ["H0|mc|24230"] = 24, ["H0|mc|24188"] = 16, ["H0|mc|24172"] = 8,
        ["H0|mc|24071"] = 6, ["H0|mc|24064"] = 6, ["H0|mc|24077"] = 4, ["H0|mc|24094"] = 4, ["H0|mc|24977"] = 4, ["H0|mc|24224"] = 8,
    };

    [Fact]
    public void LargerStockGivesStationsYardsAndCrossovers()
    {
        var board = new LayoutPlan { GaugeId = "H0", BoardWidth = 2600, BoardHeight = 1400 };
        var stock = BigMc();
        stock["H0|mc|24115"] = 4;
        stock["H0|mc|24215"] = 8;
        var list = PlanSuggest.Build(board, stock);
        AssertValid(board, stock, list);
        int Turnouts(PlanSuggestion s) => s.Pieces.Count(p => p.Def.Kind == PieceKind.Switch);
        Assert.Contains(list, s => s.Name == "Oval mit Abstellgruppe" && Turnouts(s) == 3 && s.Pieces.Count(p => p.Def.Kind == PieceKind.Buffer) == 3);
        Assert.Contains(list, s => s.Name == "Oval mit Bahnhof" && Turnouts(s) == 4);
        Assert.Contains(list, s => s.Name == "Doppeloval mit Gleiswechsel" && Turnouts(s) == 4);
        Assert.Contains(list, s => s.Name == "Hundeknochen");
        Assert.Contains(list, s => s.Name == "Oval mit Anschlussgleis" && Turnouts(s) == 1);
        Assert.Contains(list, s => s.Name == "Oval mit dreigleisigem Bahnhof" && Turnouts(s) == 4);
        Assert.Contains(list, s => s.Name == "Doppeloval mit Gleiswechsel und Abstellgleisen" && Turnouts(s) >= 3);
        Assert.All(list.SelectMany(s => s.Pieces).Where(p => p.Def.Kind == PieceKind.Switch), p => Assert.False(p.Flipped));
    }

    [Theory]
    [InlineData("rf-n")]
    [InlineData("minitrix")]
    [InlineData("tillig")]
    [InlineData("piko")]
    [InlineData("rocoline")]
    public void CrossoversWorkAcrossSystems(string systemId)
    {
        var (board, stock) = Typical(systemId);
        foreach (var k in stock.Keys.Where(k => Catalog.Get(k).Kind == PieceKind.Switch).ToList()) stock[k] = 4;
        var list = PlanSuggest.Build(board, stock);
        AssertValid(board, stock, list);
        Assert.Contains(list, s => s.Name == "Doppeloval mit Gleiswechsel");
    }

    [Fact]
    public void StockListReadsCsvAndText()
    {
        (string, string)[] entries = [("H0|mc|24130", "24130"), ("H0|mc|24188", "24188"), ("H0|mc|24611", "24611"), ("H0|mc|24977", "24977"), ("H0|kato-h0|2-150", "2-150")];
        var text = "\uFEFFArtikel;Bezeichnung;Anzahl\r\n24130;Bogen R1 30°;12\r\n\"24188\";\"Gerade 188,3 mm\";8\n"
            + "4 x 24611\n24977 Prellbock 2 Stück\n24130;Bogen;4\n99999;Unbekannt;3\n2 150 3\n# Kommentar 24188 5\n24 188\n";
        var list = PlanSuggest.ParseStockList(text, entries);
        Assert.Equal(16, list.Counts["H0|mc|24130"]);
        Assert.Equal(9, list.Counts["H0|mc|24188"]);   // 8 + eine Zeile ohne Anzahl
        Assert.Equal(4, list.Counts["H0|mc|24611"]);
        Assert.Equal(2, list.Counts["H0|mc|24977"]);
        Assert.Equal(3, list.Counts["H0|kato-h0|2-150"]);
        Assert.Equal(["99999;Unbekannt;3"], list.Unknown);
    }

    [Fact]
    public void StockListReadsBillOfMaterialsWithStockColumn()
    {
        // Stückliste als CSV: die Spalte „Bestand“ zählt, nicht „Anzahl“
        (string, string)[] entries = [("H0|mc|24130", "24130")];
        var csv = "\"Hersteller/System\";\"Artikel\";\"Bezeichnung\";\"Daten\";\"Anzahl\";\"Bestand\";\"Zu kaufen\"\r\n\"Märklin C-Gleis\";\"24130\";\"Bogen\";\"R 360 mm · 30°\";\"12\";\"20\";\"0\"\r\n";
        Assert.Equal(20, PlanSuggest.ParseStockList(csv, entries).Counts["H0|mc|24130"]);
    }

    [Fact]
    public void SessionStockImportSplitsUnusable()
    {
        var s = new PlanSession();
        s.NewPlan(new BoardSetup("H0", BoardShape.Rechteck, 2400, 1200, 600, 0));
        s.SelectSystem("mc");
        var (counts, unusable, unknown) = s.ParseSuggestStock("24130;12\n24630;1\nfoo 12345\n");
        Assert.Equal(12, counts["H0|mc|24130"]);
        Assert.Contains(unusable, d => d.Number == "24630");   // Dreiwegweiche
        Assert.Single(unknown);
    }

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

