using TrackMaster.Core;

namespace TrackMaster.Tests;

public class RecoveryTests
{
    private static string TempDir() => Directory.CreateTempSubdirectory("tm-").FullName;

    [Fact]
    public void Sicherung_bringt_den_Plan_mit_Pfad_zurueck_und_wird_geloescht()
    {
        var r = new Recovery(TempDir());
        Assert.Null(r.TryLoad());
        var plan = SamplePlan.Build();
        r.Write(plan, @"C:\Plaene\Anlage.trackmaster.json");
        var snap = r.TryLoad();
        Assert.NotNull(snap);
        Assert.Equal(@"C:\Plaene\Anlage.trackmaster.json", snap.Path);
        Assert.Equal(plan.Pieces.Count, PlanSerializer.FromJson(snap.Json, out _).Pieces.Count);
        r.Clear();
        Assert.Null(r.TryLoad());
    }

    [Fact]
    public void Defekte_Sicherung_wird_ignoriert()
    {
        var dir = TempDir();
        var r = new Recovery(dir);
        File.WriteAllText(r.FilePath, "{ kaputt");
        Assert.Null(r.TryLoad());
    }

    [Fact]
    public void Zuletzt_geoeffnet_neueste_zuerst_ohne_Doppelte()
    {
        var dir = TempDir();
        var a = Path.Combine(dir, "a.trackmaster.json");
        var b = Path.Combine(dir, "b.trackmaster.json");
        File.WriteAllText(a, "");
        File.WriteAllText(b, "");
        var recent = new RecentFiles(Path.Combine(dir, "zuletzt.txt"));
        recent.Add(a);
        recent.Add(b);
        recent.Add(a);
        Assert.Equal([a, b], recent.Items);
        File.Delete(b);
        Assert.Equal([a], recent.Items);
    }
}
