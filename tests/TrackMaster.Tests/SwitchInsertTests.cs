using TrackMaster.Core;

namespace TrackMaster.Tests;

public class SwitchInsertTests
{
    [ThreadStatic] private static string? Last;

    private static (PlanSession S, List<PlacedPiece> Helix) HelixSession()
    {
        var s = Sessions.H0();
        s.Message += m => Last = m;
        Assert.NotNull(s.BuildHelix(new HelixSpec("H0|mc|24230", 2, 100, false, LevelPerTurn: true), (1500, 1500)));
        return (s, s.Plan.Pieces.Where(p => p.Def.Kind == PieceKind.Curve).OrderBy(p => p.Id).ToList());
    }

    public static IEnumerable<object[]> Switches()
        => SwitchInsert.Switches(Sessions.H0().Plan, "mc").Where(d => d.SystemId == "mc").Select(d => new object[] { d.Key });

    [Theory]
    [MemberData(nameof(Switches))]
    public void Weiche_in_der_Gleiswendel_einsetzen_behaelt_Hoehe_Ebene_und_schliesst_die_Luecke(string key)
    {
        var (s, helix) = HelixSession();
        var target = helix[helix.Count / 2 + 3];   // in der 2. Windung
        var z0 = target.Height;
        var level = target.Level;
        var before = Connectivity.Analyze(s.Plan).Open.Count;
        s.Selection.Clear();
        s.Selection.Add(target.Id);
        var sw = s.InsertSwitch(key);
        Assert.True(sw is not null, Last);
        Assert.Null(s.Plan.Find(target.Id));
        Assert.Equal(z0, sw!.Height, 3);
        Assert.Equal(level, sw.Level);
        // Stammgleis wieder angeschlossen: nur der Abzweig ist als neues offenes Ende dazugekommen
        var conn = Connectivity.Analyze(s.Plan);
        Assert.Equal(before + 1, conn.Open.Count);
        Assert.Single(conn.Of(sw.Id), c => c.IsOpen);
        // Höhen passen an allen Anschlüssen
        foreach (var c in conn.All.Where(c => c.Link is not null))
            Assert.Equal(c.Z, conn.Find(c.Link!.PieceId, c.Link.Index)!.Z, 1);
        s.Undo();
        Assert.NotNull(s.Plan.Find(target.Id));
    }

    [Fact]
    public void Gegenrichtung_und_andere_Seite()
    {
        var (s, helix) = HelixSession();
        var key = SwitchInsert.Switches(s.Plan, "mc").First(d => d.SystemId == "mc" && d.IsSimpleSwitch).Key;
        var target = helix[5];
        s.Selection.Clear();
        s.Selection.Add(target.Id);
        var a = s.InsertSwitch(key); Assert.True(a is not null, "a " + Last);
        var branchA = OpenEnd(s, a!);
        s.Undo();
        s.Selection.Clear();
        s.Selection.Add(target.Id);
        var b = s.InsertSwitch(key, otherSide: true); Assert.True(b is not null, "b " + Last);
        var branchB = OpenEnd(s, b!);
        // Abzweig auf der anderen Seite: der Abzweig liegt weiter innen in der Wendel
        Assert.True(R(branchB) < R(branchA) - 20, $"{R(branchA)} / {R(branchB)}");
        s.Undo();
        s.Selection.Clear();
        s.Selection.Add(target.Id);
        var c = s.InsertSwitch(key, reverse: true); Assert.True(c is not null, "c " + Last);
        // Spitze am anderen Ende des alten Gleises
        var end1 = target.WorldConnectors()[1];
        var tip = c!.WorldConnectors()[0];
        Assert.True(Math.Abs(tip.X - end1.X) + Math.Abs(tip.Y - end1.Y) < 0.5);
    }

    private static (double X, double Y) OpenEnd(PlanSession s, PlacedPiece sw)
    {
        var c = Connectivity.Analyze(s.Plan).Of(sw.Id).Single(x => x.IsOpen);
        return (c.Pose.X, c.Pose.Y);
    }

    private static double R((double X, double Y) p) => Math.Sqrt((p.X - 1500) * (p.X - 1500) + (p.Y - 1500) * (p.Y - 1500));

    [Fact]
    public void Nur_einfache_Gleise_lassen_sich_ersetzen()
    {
        var (s, _) = HelixSession();
        s.Selection.Clear();
        Assert.Null(s.InsertSwitch("H0|mc|24611"));
        Assert.Contains("genau ein Gleis", Last);
    }
}
