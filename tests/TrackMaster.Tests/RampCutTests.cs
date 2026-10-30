using System.IO.Compression;
using System.Xml.Linq;
using TrackMaster.Core;
using Xunit;

namespace TrackMaster.Tests;

public static class SvgTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    /// <summary>Gültiges SVG mit genau einem zu schneidenden Umriss.</summary>
    public static void AssertSinglePart(SvgFile f)
    {
        Assert.EndsWith(".svg", f.Name);
        var doc = XDocument.Parse(f.Content);
        Assert.Equal(Svg + "svg", doc.Root!.Name);
        Assert.Single(doc.Descendants(), e => (string?)e.Attribute("class") == "cut");
    }
}

public class SnapTests
{
    [Fact]
    public void Einzelnes_Gleis_rastet_auch_falsch_herum_hineingezogen_ein()
    {
        var plan = LayoutPlan.Empty("H0");
        var a = PlanEditing.Attach(plan, "H0|mc|24188", null, (500, 500));
        var end = a.WorldConnectors()[1];
        // Gleis zeigt in dieselbe Richtung wie das freie Ende, Anschluss 0 liegt darauf
        var b = plan.Add("H0|mc|24230");
        PlanEditing.PlaceAt(b, 0, end.X + 6, end.Y + 3, end.Heading + 180);
        Assert.True(PlanEditing.Snap(plan, new HashSet<int> { b.Id }, 15));
        Assert.NotNull(Connectivity.Analyze(plan).Find(b.Id, 0)!.Link);
    }

    [Fact]
    public void Einzelnes_Gleis_rastet_mit_dem_Ende_ein_das_ueber_dem_freien_Ende_liegt()
    {
        var plan = LayoutPlan.Empty("H0");
        var a = PlanEditing.Attach(plan, "H0|mc|24188", null, (500, 500));
        var end = a.WorldConnectors()[1];
        var b = plan.Add("H0|mc|24188");
        // Anschluss 1 des neuen Gleises liegt quer über dem freien Ende
        PlanEditing.PlaceAt(b, 1, end.X - 4, end.Y + 5, end.Heading + 90);
        Assert.True(PlanEditing.Snap(plan, new HashSet<int> { b.Id }, 15));
        var c = Connectivity.Analyze(plan);
        Assert.NotNull(c.Find(b.Id, 1)!.Link);
        Assert.Equal(a.Id, c.Find(b.Id, 1)!.Link!.PieceId);
    }

    [Fact]
    public void Gruppen_rasten_weiterhin_nur_bei_passender_Richtung_ein()
    {
        var plan = LayoutPlan.Empty("H0");
        var a = PlanEditing.Attach(plan, "H0|mc|24188", null, (500, 500));
        var end = a.WorldConnectors()[1];
        var b = plan.Add("H0|mc|24188");
        PlanEditing.PlaceAt(b, 0, end.X + 3, end.Y, end.Heading + 180);
        var c = PlanEditing.Attach(plan, "H0|mc|24188", Connectivity.Analyze(plan).Find(b.Id, 1), (0, 0));
        Assert.False(PlanEditing.Snap(plan, new HashSet<int> { b.Id, c.Id }, 15));
    }
}

public class RampCutTests
{
    private const string Straight = "H0|mc|24188";

    /// <summary>Gerade Steigung aus <paramref name="count"/> Gleisen, ab x = 0 entlang y = 500.</summary>
    private static (LayoutPlan Plan, HashSet<int> Ids) Ramp(int count, double grade = 0.03)
    {
        var plan = new LayoutPlan { GaugeId = "H0", BoardWidth = 3000, BoardHeight = 1000 };
        var len = Catalog.Get(Straight).TrackLength;
        for (var i = 0; i < count; i++)
            plan.Pieces.Add(new PlacedPiece { Id = i + 1, Key = Straight, X = i * len, Y = 500, Height = grade * i * len, Grade = grade });
        return (plan, plan.Pieces.Select(p => p.Id).ToHashSet());
    }

    [Fact]
    public void Gerade_Steigung_wird_in_gleich_lange_Teile_mit_Laschen_geteilt()
    {
        var (plan, ids) = Ramp(10);
        var spec = RampTrasse.Defaults(plan.Gauge) with { MaxPartLength = 800 };
        var (cut, error) = RampTrasse.Build(plan, ids, spec);
        Assert.Null(error);
        var total = 10 * Catalog.Get(Straight).TrackLength;
        Assert.Equal(total, cut!.TotalLength, 1);
        Assert.Equal((int)Math.Ceiling(total / 800), cut.Parts.Count);
        Assert.Equal(cut.Parts.Count - 1, cut.Laschen);
        foreach (var p in cut.Parts)
        {
            Assert.Equal(total / cut.Parts.Count, p.Length, 1);
            // gerade gedreht: so lang wie das Teil, so hoch wie die Trasse breit
            Assert.Equal(p.Length, p.Width, 1);
            Assert.Equal(spec.Width, p.Height, 1);
        }
        Assert.Equal(0, cut.Parts[0].HeightFrom, 1);
        Assert.Equal(total * 0.03, cut.Parts[^1].HeightTo, 1);
        // Stöße der Gleise auf den Teilen: 9 innere Stöße
        Assert.Equal(9, cut.Parts.Sum(p => p.Ticks.Count));
    }

    [Fact]
    public void Steigung_beginnt_am_unteren_Ende_auch_wenn_die_Gleise_umgekehrt_liegen()
    {
        // Gefälle in Zeichenrichtung: das untere Ende ist Anschluss 1 des letzten Gleises
        var (plan, ids) = Ramp(4, -0.03);
        foreach (var p in plan.Pieces) p.Height += 100;
        var (cut, _) = RampTrasse.Build(plan, ids, RampTrasse.Defaults(plan.Gauge));
        Assert.True(cut!.Parts[0].HeightFrom < cut.Parts[^1].HeightTo);
    }

    [Fact]
    public void Bogen_ergibt_gebogene_Teile_die_auf_die_Platte_passen()
    {
        var plan = LayoutPlan.Empty("H0");
        PlanEditing.Attach(plan, "H0|mc|24230", null, (1000, 600));
        for (var i = 0; i < 7; i++)
            PlanEditing.Attach(plan, "H0|mc|24230", Connectivity.Analyze(plan).Open.First(), (0, 0));
        var ids = plan.Pieces.Select(p => p.Id).ToHashSet();
        PlanEditing.ApplyGradient(plan, ids, 0, 2.5);
        var (cut, error) = RampTrasse.Build(plan, ids, RampTrasse.Defaults(plan.Gauge));
        Assert.Null(error);
        Assert.DoesNotContain(cut!.Notes, n => n.StartsWith("Achtung"));
        // Ein steigender Vollkreis ist nicht geschlossen: Laschen nur zwischen den Teilen
        Assert.Equal(cut.Parts.Count - 1, cut.Laschen);
        Assert.All(cut.Parts, p => Assert.True(p.Height > RampTrasse.Defaults(plan.Gauge).Width + 1));
    }

    [Fact]
    public void Ohne_Auswahl_gibt_es_eine_Meldung()
    {
        var (plan, _) = Ramp(2);
        var (cut, error) = RampTrasse.Build(plan, new HashSet<int>(), RampTrasse.Defaults(plan.Gauge));
        Assert.Null(cut);
        Assert.NotNull(error);
    }

    [Fact]
    public void Jedes_Teil_und_jede_Lasche_ist_eine_eigene_Datei_im_Zip()
    {
        var (plan, ids) = Ramp(10);
        var (cut, _) = RampTrasse.Build(plan, ids, RampTrasse.Defaults(plan.Gauge) with { MaxPartLength = 600 });
        var files = cut!.ToSvgFiles("Test");
        Assert.Equal(cut.Parts.Count + cut.Laschen, files.Count);
        Assert.All(files, SvgTests.AssertSinglePart);
        using var zip = new ZipArchive(new MemoryStream(SvgFiles.Zip(files)));
        Assert.Equal(files.Select(f => f.Name), zip.Entries.Select(e => e.FullName));
        using var reader = new StreamReader(zip.Entries[0].Open());
        Assert.Equal(files[0].Content, reader.ReadToEnd());
    }
}
