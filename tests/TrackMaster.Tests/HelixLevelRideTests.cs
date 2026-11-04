using TrackMaster.Core;
using Xunit;

namespace TrackMaster.Tests;

internal static class Sessions
{
    public static PlanSession H0()
    {
        var s = new PlanSession();
        s.NewPlan(BoardSetup.Default("H0"));
        return s;
    }

    public static PlacedPiece Add(this PlanSession s, string key)
    {
        s.Attach(key, (1000, 1000));
        return s.Plan.Pieces[^1];
    }
}

public class HelixLevelTests
{
    private static LayoutPlan Plan() => new() { GaugeId = "H0", BoardWidth = 4000, BoardHeight = 4000 };

    [Fact]
    public void Jede_Windung_kommt_auf_eine_neue_Ebene()
    {
        var plan = Plan();
        var spec = new HelixSpec("H0|mc|24230", 3, 100, false, StartHeight: 50, LevelPerTurn: true);
        Assert.Equal(2, Helix.NewLevels(plan, spec));
        var (inner, _) = Helix.Build(plan, spec, null, (1500, 1500));
        Assert.Equal(3, plan.Levels.Count);
        Assert.Equal(50, inner[0].Height, 1);
        var perTurn = inner.Count / 3;
        Assert.All(inner.Take(perTurn), p => Assert.Equal(0, p.Level));
        Assert.All(inner.Skip(perTurn).Take(perTurn), p => Assert.Equal(1, p.Level));
        Assert.All(inner.Skip(2 * perTurn), p => Assert.Equal(2, p.Level));
        Assert.Equal(150, plan.Levels[1].Height, 0);
        Assert.Equal(250, plan.Levels[2].Height, 0);
        // Die Windungen bleiben trotzdem durchgehend befahrbar
        Assert.Equal(2, Connectivity.Analyze(plan).Open.Count);
    }

    [Fact]
    public void Wendel_beginnt_auf_der_gewaehlten_Ebene_in_deren_Hoehe()
    {
        var plan = Plan();
        plan.Levels.Add(new PlanLevel { Name = "Oben", Height = 300 });
        var (inner, _) = Helix.Build(plan, new HelixSpec("H0|mc|24230", 2, 100, true, Level: 1), null, (1500, 1500));
        Assert.Equal(2, plan.Levels.Count);
        Assert.All(inner, p => Assert.Equal(1, p.Level));
        Assert.Equal(300, inner[0].Height, 1);
        Assert.Equal(500, inner[^1].ConnectorHeights()[1], 1);
    }

    [Fact]
    public void Starthoehe_gilt_auch_am_Anschlussgleis_und_Rueckgaengig_entfernt_neue_Ebenen()
    {
        var s = Sessions.H0();
        s.Add("H0|mc|24188");
        var spec = new HelixSpec("H0|mc|24230", 2, 100, false, StartHeight: 40, LevelPerTurn: true);
        Assert.NotNull(s.BuildHelix(spec, (1500, 1500)));
        Assert.Equal(2, s.Plan.Levels.Count);
        var first = s.Plan.Pieces.Where(p => p.Def.Kind == PieceKind.Curve).MinBy(p => p.Id)!;
        Assert.Equal(40, first.Height, 1);
        s.Undo();
        Assert.Single(s.Plan.Levels);
    }

    [Fact]
    public void Mehrere_Gleise_bekommen_eine_gemeinsame_Hoehe()
    {
        var s = Sessions.H0();
        s.Add("H0|mc|24188");
        s.Add("H0|mc|24188");
        s.Add("H0|mc|24188");
        foreach (var p in s.Plan.Pieces) { p.Height = p.Id * 10; p.Grade = 0.02; s.Selection.Add(p.Id); }
        s.Commit();
        s.Plan.Pieces[2].Locked = true;
        Assert.Null(s.SelectionHeight);
        s.SetSelectionHeight(85);
        Assert.Equal(85, s.Plan.Pieces[0].Height);
        Assert.Equal(0, s.Plan.Pieces[1].Grade);
        Assert.NotEqual(85, s.Plan.Pieces[2].Height);
        s.Selection.Remove(s.Plan.Pieces[2].Id);
        Assert.Equal(85, s.SelectionHeight);
    }
}

public class RideChoiceTests
{
    private static (LayoutPlan Plan, PlacedPiece Switch) Siding(bool leftHand)
    {
        var turnout = Catalog.GetGauge("H0").Systems.First(s => s.Id == "mc").Pieces
            .First(p => p.IsSimpleSwitch && p.Routes is null && p.Paths is null && p.RightHand != leftHand);
        var s = Sessions.H0();
        s.Add("H0|mc|24188");
        var sw = s.Add(turnout.Key);
        return (s.Plan, sw);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Richtungen_an_der_Weiche_heissen_links_rechts_geradeaus(bool leftHand)
    {
        var (_, sw) = Siding(leftHand);
        var choices = RidePath.Choices(sw, 0);
        Assert.Equal(2, choices.Count);
        Assert.Contains(choices, c => c.Side == 0 && c.Route == 0);
        Assert.Contains(choices, c => c.Side == (leftHand ? -1 : 1) && c.Route == 1);
    }

    [Fact]
    public void Richtung_waehlen_aendert_nur_den_Weg_hinter_der_Weiche()
    {
        var plan = SamplePlan.Build();
        RidePath? path = null;
        foreach (var p in plan.Pieces)
        {
            path = RidePath.Build(plan, p.Id);
            if (path.Stops.Count > 1 && path.Stops[1].Distance > 10) break;
        }
        var routes = new Dictionary<int, int>();
        // Zug steht hinter der ersten Weiche, vor der zweiten
        var second = path!.Stops[1];
        var at = second.Distance - 5;
        var choice = path.Choices(second).First(c => c.Route != 0);
        var turned = path.ChooseNext(at, choice.Side, routes);
        Assert.Equal(choice.Route, routes[second.PieceId]);
        Assert.Equal(path.PointAt(at), turned.PointAt(at));
        Assert.Equal(path.PointAt(1), turned.PointAt(1));
        var (a, b) = (path.PointAt(second.Distance + 200), turned.PointAt(second.Distance + 200));
        Assert.True(Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) > 1);
        Assert.Contains(Lang.T("Weiche"), turned.Describe(at, routes));
        // Weiterfahren verlängert den Weg
        var len = turned.Length;
        turned.EnsureAhead(len - 10, routes);
        Assert.True(turned.Length > len);
    }

    [Fact]
    public void Kreuzungsweiche_laesst_sich_abzweigend_befahren()
    {
        var dkw = Catalog.GetGauge("H0").Systems.SelectMany(s => s.Pieces).First(p => p.IsDoubleSlip);
        var s = Sessions.H0();
        var plan = s.Plan;
        var piece = s.Add(dkw.Key);
        Assert.Equal(2, RidePath.RouteCount(piece));
        var choices = RidePath.Choices(piece, 0);
        Assert.Equal(2, choices.Count);
        Assert.Contains(choices, c => c.Side == 0);
        var straight = RidePath.Build(plan, piece.Id);
        var slip = RidePath.Build(plan, piece.Id, new Dictionary<int, int> { [piece.Id] = 1 });
        Assert.NotEmpty(straight.Stops);
        var ends = piece.WorldConnectors();
        Assert.Contains(slip.Points, q => Math.Abs(q.X - ends[3].X) < 1 && Math.Abs(q.Y - ends[3].Y) < 1);
        Assert.DoesNotContain(straight.Points.Take(20), q => Math.Abs(q.X - ends[3].X) < 1 && Math.Abs(q.Y - ends[3].Y) < 1);
    }
}

public class OpenSystemTests
{
    [Fact]
    public void Beim_Oeffnen_wird_das_im_Plan_verwendete_Gleissystem_gewaehlt()
    {
        var s = Sessions.H0();
        var systems = s.Plan.Gauge.Systems.Where(x => x.Pieces.Any(p => p.Kind == PieceKind.Straight)).ToList();
        Assert.True(systems.Count >= 2);
        var (other, chosen) = (systems[1], systems[0]);
        s.SelectSystem(other.Id);
        var straight = other.Pieces.First(p => p.Kind == PieceKind.Straight);
        s.Add(straight.Key);
        s.Add(straight.Key);
        s.SelectSystem(chosen.Id);
        var json = PlanSerializer.ToJson(s.Plan);
        Assert.Contains($"\"system\": \"{chosen.Id}\"", json);

        var t = new PlanSession();
        t.NewPlan(BoardSetup.Default("H0"));
        t.SelectSystem(other.Id);
        t.OpenJson(json, "Test");
        Assert.Equal(chosen.Id, t.CurrentSystem.Id);

        // Ältere Datei ohne Angabe: System des zuletzt verlegten Gleises
        s.Add(chosen.Pieces.First(p => p.Kind == PieceKind.Straight).Key);
        var old = PlanSerializer.ToJson(s.Plan).Replace($"\"system\": \"{chosen.Id}\",", "");
        Assert.DoesNotContain("\"system\"", old);
        t.SelectSystem(other.Id);
        t.OpenJson(old, "Alt");
        Assert.Equal(chosen.Id, t.CurrentSystem.Id);
        Assert.Equal(chosen.Id, t.Plan.SystemId);
    }
}

public class ParallelSpacingTests
{
    [Fact]
    public void Gleisabstand_kommt_vom_Gleissystem_und_die_Auswahl_nennt_die_Hersteller()
    {
        var n = Catalog.GetGauge("N");
        var piccolo = n.Systems.First(s => s.Id == "picc");
        Assert.Equal(33.6, Helix.DefaultSpacing(n, piccolo));
        Assert.Equal(33, Helix.DefaultSpacing(n, n.Systems.First(s => s.Id == "kato")));
        var choices = Helix.SpacingChoices(n);
        Assert.Equal([33, 33.6], choices.Select(c => c.Spacing));
        Assert.Contains("Minitrix", choices[1].Label);
        Assert.Contains(piccolo.Name, choices[1].Label);
        // ohne Herstellerangabe: üblicher Wert der Spur
        var h0 = Catalog.GetGauge("H0");
        Assert.Equal(62, Helix.DefaultSpacing(h0, h0.Systems.First(s => s.Id == "weinert")));
        Assert.Equal(77.5, Helix.DefaultSpacing(h0, h0.Systems.First(s => s.Id == "mc")));
        Assert.Equal(62, Helix.DefaultSpacing(h0));
    }
}
