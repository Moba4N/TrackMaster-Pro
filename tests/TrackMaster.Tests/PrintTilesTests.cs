using TrackMaster.Core;

namespace TrackMaster.Tests;

public class PrintTilesTests
{
    [Fact]
    public void Platte_wird_luckenlos_mit_Klebekante_auf_A4_verteilt()
    {
        // A4 hoch mit 10 mm Rand: 190 × 277 mm bedruckbar
        var tiles = PrintTiles.Layout((0, 0, 2000, 1250), 190, 277, 10);
        Assert.Equal(12 * 5, tiles.Count);
        Assert.Equal("A1", tiles[0].Label);
        Assert.Equal("E12", tiles[^1].Label);
        Assert.True(tiles.Min(t => t.X0) <= 0 && tiles.Max(t => t.X1) >= 2000);
        Assert.True(tiles.Min(t => t.Y0) <= 0 && tiles.Max(t => t.Y1) >= 1250);
        // Nachbarn überlappen genau um die Klebekante
        Assert.Equal(10, tiles[0].X1 - tiles[1].X0, 6);
        Assert.Equal(10, tiles[0].Y1 - tiles[12].Y0, 6);
    }

    [Fact]
    public void Kleiner_Plan_passt_auf_eine_Seite_und_Passmarken_liegen_im_Raster()
    {
        var tiles = PrintTiles.Layout((0, 0, 150, 200), 190, 277);
        Assert.Single(tiles);
        Assert.All(PrintTiles.Marks(tiles[0]), m => { Assert.Equal(0, m.X % 100); Assert.Equal(0, m.Y % 100); });
        Assert.Contains((100.0, 100.0), PrintTiles.Marks(tiles[0]));
    }

    [Fact]
    public void Zeilen_ueber_Z_bekommen_zwei_Buchstaben()
    {
        Assert.Equal("Z", PrintTiles.RowName(25));
        Assert.Equal("AA", PrintTiles.RowName(26));
    }
}
