using TrackMaster.Core;
using Xunit;

namespace TrackMaster.Tests;

public class LevelTests
{
    private const string Straight = "H0|mc|24188";

    private static PlanSession Session()
    {
        var s = new PlanSession();
        s.Replace(LayoutPlan.Empty("H0"), null);
        return s;
    }

    private static List<LevelEdit> Edits(LayoutPlan plan) => plan.Levels.Select((l, i) => new LevelEdit(i, l.Name, l.Height, l.Visible)).ToList();

    [Fact]
    public void Neue_Gleise_kommen_auf_die_aktive_Ebene_in_ihrer_Grundhoehe()
    {
        var s = Session();
        var edits = Edits(s.Plan);
        edits.Add(new LevelEdit(-1, "Bergstrecke", 120, true));
        s.ApplyLevels(edits, 1);
        s.Drop(Straight, 500, 500, 0);
        var p = Assert.Single(s.Plan.Pieces);
        Assert.Equal(1, p.Level);
        Assert.Equal(120, p.Height, 3);
        // Angesetztes Gleis bleibt auf der Ebene des Nachbarn, auch wenn eine andere Ebene aktiv ist
        s.SetActiveLevel(0);
        s.SetActive(p.Id, 1);
        s.Attach(Straight, (0, 0));
        Assert.All(s.Plan.Pieces, q => Assert.Equal(1, q.Level));
    }

    [Fact]
    public void Hoechstens_zehn_Ebenen()
    {
        var s = Session();
        var edits = Enumerable.Range(0, 14).Select(i => new LevelEdit(i == 0 ? 0 : -1, $"E{i}", i * 100, true)).ToList();
        s.ApplyLevels(edits, 12);
        Assert.Equal(LayoutPlan.MaxLevels, s.Plan.Levels.Count);
        Assert.Equal(LayoutPlan.MaxLevels - 1, s.Plan.ActiveLevel);
    }

    [Fact]
    public void Ausgeblendete_Ebenen_fehlen_in_der_Ansicht_und_werden_abgewaehlt()
    {
        var s = Session();
        var edits = Edits(s.Plan);
        edits.Add(new LevelEdit(-1, "Schattenbahnhof", -150, true));
        s.ApplyLevels(edits, 0);
        s.Drop(Straight, 300, 300, 0);
        s.SetActiveLevel(1);
        s.Drop(Straight, 300, 600, 0);
        s.SelectAll();
        Assert.Equal(2, s.Selection.Count);
        s.SetLevelVisible(1, false);
        Assert.Single(s.View.Pieces);
        Assert.Equal(2, s.Plan.Pieces.Count);
        Assert.Single(s.Selection);
        Assert.Equal(2, s.VisibleConnectors().Count());
        Assert.Equal(2, s.Plan.Statistics().Pieces);
        // Nur eine Ebene zeigen, dann wieder alle
        s.ShowOnlyLevel(1);
        Assert.Equal(1, Assert.Single(s.View.Pieces).Level);
        s.ShowOnlyLevel(-1);
        Assert.Same(s.Plan, s.View);
    }

    [Fact]
    public void Rueckgaengig_laesst_die_Sichtbarkeit_wie_sie_ist()
    {
        var s = Session();
        var edits = Edits(s.Plan);
        edits.Add(new LevelEdit(-1, "Oben", 100, true));
        s.ApplyLevels(edits, 0);
        s.Drop(Straight, 300, 300, 0);
        s.SetLevelVisible(1, false);
        s.Undo();
        Assert.Empty(s.Plan.Pieces);
        Assert.False(s.Plan.Levels[1].Visible);
    }

    [Fact]
    public void Grundhoehe_aendern_hebt_die_Gleise_der_Ebene_und_Entfernen_legt_sie_eine_Ebene_tiefer()
    {
        var s = Session();
        var edits = Edits(s.Plan);
        edits.Add(new LevelEdit(-1, "Mitte", 100, true));
        edits.Add(new LevelEdit(-1, "Oben", 200, true));
        s.ApplyLevels(edits, 2);
        s.Drop(Straight, 300, 300, 0);
        var p = s.Plan.Pieces[0];
        Assert.Equal(200, p.Height, 3);
        edits = Edits(s.Plan);
        edits[2] = edits[2] with { Height = 260 };
        s.ApplyLevels(edits, 2);
        Assert.Equal(260, p.Height, 3);
        // Ebene „Oben“ entfernen: Gleis kommt auf „Mitte“ und in deren Höhe
        s.ApplyLevels(Edits(s.Plan).Take(2).ToList(), 0);
        Assert.Equal(1, p.Level);
        Assert.Equal(100, p.Height, 3);
    }

    [Fact]
    public void Gewaehlte_auf_eine_Ebene_legen_aendert_die_Hoehe_um_den_Ebenenabstand()
    {
        var s = Session();
        var edits = Edits(s.Plan);
        edits.Add(new LevelEdit(-1, "Oben", 150, true));
        s.ApplyLevels(edits, 0);
        s.Drop(Straight, 300, 300, 0);
        var p = s.Plan.Pieces[0];
        p.Height = 20;
        s.MoveSelectionToLevel(1);
        Assert.Equal(1, p.Level);
        Assert.Equal(170, p.Height, 3);
    }

    [Fact]
    public void Ebenen_werden_gespeichert_und_gelesen()
    {
        var s = Session();
        var edits = Edits(s.Plan);
        edits.Add(new LevelEdit(-1, "Bergstrecke", 120, false));
        s.ApplyLevels(edits, 0);
        s.Drop(Straight, 300, 300, 0);
        s.MoveSelectionToLevel(1);
        s.SetActiveLevel(1);
        s.AddLabel("Gipfel", 100, 100);
        var json = PlanSerializer.ToJson(s.Plan);
        var back = PlanSerializer.FromJson(json, out _);
        Assert.Equal(2, back.Levels.Count);
        Assert.Equal("Bergstrecke", back.Levels[1].Name);
        Assert.Equal(120, back.Levels[1].Height);
        Assert.True(back.Levels[1].Visible);       // MoveSelectionToLevel blendet die Zielebene ein
        Assert.Equal(1, back.ActiveLevel);
        Assert.Equal(1, back.Pieces[0].Level);
        Assert.Equal(1, back.Scenery.Single(x => x.Kind == SceneryKind.Label).Level);
        // Ein Plan mit nur einer Ebene schreibt keine Ebenen in die Datei
        Assert.DoesNotContain("\"levels\"", PlanSerializer.ToJson(LayoutPlan.Empty("H0")));
    }

    [Theory]
    [InlineData("Z")]
    [InlineData("H0")]
    [InlineData("G")]
    public void Ebenenabstand_ist_ein_Vielfaches_von_10_mm_ueber_der_Durchfahrtshoehe(string gauge)
    {
        var plan = LayoutPlan.Empty(gauge);
        Assert.Equal(0, plan.LevelStep % 10, 6);
        Assert.True(plan.LevelStep > plan.Gauge.ClearanceHeight);
    }
}
