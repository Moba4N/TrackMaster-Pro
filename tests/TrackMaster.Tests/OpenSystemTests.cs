using TrackMaster.Core;
using Xunit;

namespace TrackMaster.Tests;

public class OpenSystemBuiltTests
{
    [Fact]
    public void Bibliothek_zeigt_beim_Oeffnen_das_verbaute_System_auch_ohne_passende_Angabe()
    {
        var s = Sessions.H0();
        var systems = s.Plan.Gauge.Systems.Where(x => x.Pieces.Any(p => p.Kind == PieceKind.Straight)).ToList();
        var (built, other) = (systems[1], systems[0]);
        s.SelectSystem(built.Id);
        s.Add(built.Pieces.First(p => p.Kind == PieceKind.Straight).Key);
        s.SelectSystem(other.Id); // nur in der Bibliothek umgeschaut, nichts davon verlegt
        var json = PlanSerializer.ToJson(s.Plan);

        var t = new PlanSession();
        t.NewPlan(BoardSetup.Default("H0"));
        string? shownAtChange = null;
        t.Changed += () => shownAtChange ??= t.CurrentSystem.Id; // so aktualisiert sich die Windows-Bibliothek
        t.OpenJson(json, "Test");
        Assert.Equal(built.Id, t.CurrentSystem.Id);
        Assert.Equal(built.Id, shownAtChange);

        // Wiederherstellen (Sicherung, Neustart des Fensters) wählt ebenso
        var u = new PlanSession();
        u.NewPlan(BoardSetup.Default("H0"));
        u.Restore(json, null, dirty: true);
        Assert.Equal(built.Id, u.CurrentSystem.Id);
    }
}
