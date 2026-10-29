using TrackMaster.Core;

namespace TrackMaster.Tests;

public class TableTests
{
    [Theory]
    [InlineData("H0|rocoline|42615", 40, 126.5)]
    [InlineData("N|kato|20-283", 36, 80)]
    [InlineData("Z|mini|89983", 24, 66)]
    public void Drehscheibe_hat_Anschluesse_rundum_auf_dem_Radius(string key, int count, double radius)
    {
        var d = Catalog.Get(key);
        Assert.Equal(PieceKind.Turntable, d.Kind);
        var ports = TrackGeometry.LocalConnectors(d, false);
        Assert.Equal(count, ports.Count);
        Assert.Equal(new Pose(0, 0, 180), ports[0]);
        Assert.All(ports, p => Assert.Equal(radius, Math.Sqrt((p.X - d.Center.X) * (p.X - d.Center.X) + (p.Y - d.Center.Y) * (p.Y - d.Center.Y)), 6));
        // Jeder Anschluss zeigt von der Mitte weg
        Assert.All(ports, p => Assert.True(Math.Cos(p.HeadingRad) * (p.X - d.Center.X) + Math.Sin(p.HeadingRad) * (p.Y - d.Center.Y) > radius * 0.99));
    }

    [Theory]
    [InlineData("Z", "mini", "8994")]
    [InlineData("N", "kato", "20-283")]
    public void Drehscheiben_bleiben_in_der_Bibliothek(string gauge, string system, string number)
        => Assert.Contains(Catalog.GetSystem(gauge, system)!.Pieces, p => p.Number == number);

    [Fact]
    public void Alte_Nummer_der_Drehscheibe_fuehrt_zur_neuen()
        => Assert.Equal("89983", Catalog.Get("Z|mini|8599").Number);

    [Fact]
    public void Schiebebuehne_hat_Anschluesse_links_und_rechts()
    {
        var d = Catalog.Get("Z|mini|8994");
        Assert.Equal(PieceKind.TransferTable, d.Kind);
        var ports = TrackGeometry.LocalConnectors(d, false);
        Assert.Equal(10, ports.Count);
        Assert.Equal(5, ports.Count(p => p.Heading == 180 && Math.Abs(p.X) < 0.01));
        Assert.Equal(5, ports.Count(p => p.Heading == 0 && Math.Abs(p.X - 220.17) < 0.01));
    }

    [Fact]
    public void Freie_Anschluesse_sind_keine_offenen_Gleisenden_und_Gleise_rasten_ein()
    {
        var plan = LayoutPlan.Empty("H0");
        var table = PlanEditing.Attach(plan, "H0|rocoline|42615", null, (1000, 600));
        Assert.Empty(Connectivity.Analyze(plan).Open);
        var conn = Connectivity.Analyze(plan);
        var track = PlanEditing.Attach(plan, "H0|rocoline|42410", conn.Find(table.Id, 5), (0, 0));
        var after = Connectivity.Analyze(plan);
        Assert.NotNull(after.Find(table.Id, 5)!.Link);
        Assert.Single(after.Open); // nur das äußere Ende des Gleises
        Assert.Equal(track.Id, after.Open[0].PieceId);
    }

    [Fact]
    public void Mitfahren_ueber_die_Drehscheibe_auf_das_gegenueberliegende_Gleis()
    {
        var plan = LayoutPlan.Empty("H0");
        var table = PlanEditing.Attach(plan, "H0|rocoline|42615", null, (1000, 600));
        var a = PlanEditing.Attach(plan, "H0|rocoline|42410", Connectivity.Analyze(plan).Find(table.Id, 0), (0, 0));
        var b = PlanEditing.Attach(plan, "H0|rocoline|42410", Connectivity.Analyze(plan).Find(table.Id, 20), (0, 0));
        var path = RidePath.Build(plan, a.Id);
        Assert.True(path.Length > 2 * 126.5);
        // Der Weg reicht von einem Gleis über die Bühne bis zum anderen
        var ends = new[] { a, b }.Select(p => p.WorldConnectors()[1]).ToList();
        Assert.Contains(path.Points, q => Math.Abs(q.X - ends[1].X) < 3 && Math.Abs(q.Y - ends[1].Y) < 3);
    }

    [Fact]
    public void Steigung_und_Plan_Dateien_vertragen_Drehscheiben()
    {
        var plan = LayoutPlan.Empty("N");
        var t = PlanEditing.Attach(plan, "N|kato|20-283", null, (500, 400));
        Assert.Equal(36, t.ConnectorHeights().Count);
        var back = PlanSerializer.FromJson(PlanSerializer.ToJson(plan), out var skipped);
        Assert.Equal(0, skipped);
        Assert.Equal("N|kato|20-283", back.Pieces[0].Key);
        Assert.Contains("<circle", SvgExport.Build(plan));
    }
}
