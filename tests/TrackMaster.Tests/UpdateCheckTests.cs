using TrackMaster.Core;

namespace TrackMaster.Tests;

public class UpdateCheckTests
{
    [Fact]
    public void Neuere_Version_wird_erkannt()
    {
        var info = UpdateCheck.Parse("""{ "version": "1.5.0", "url": "https://example.org/download", "notes": "Neu: Fahrstraßen." }""");
        Assert.NotNull(info);
        Assert.True(UpdateCheck.IsNewer(info, new Version(1, 4, 0, 0)));
        Assert.False(UpdateCheck.IsNewer(info, new Version(1, 5, 0, 0)));
        Assert.False(UpdateCheck.IsNewer(info, new Version(2, 0, 0)));
        Assert.Equal("https://example.org/download", info.Url);
    }

    [Fact]
    public void Unsichere_oder_kaputte_Angaben_werden_verworfen()
    {
        Assert.Null(UpdateCheck.Parse("{ kaputt"));
        Assert.Null(UpdateCheck.Parse("""{ "version": "neu" }"""));
        Assert.Null(UpdateCheck.Parse("""{ "version": "1.5.0", "url": "http://example.org" }""")!.Url);
        Assert.Null(UpdateCheck.Parse("""{ "version": "1.5.0", "url": "javascript:alert(1)" }""")!.Url);
    }
}
