namespace TrackMaster.Core;

/// <summary>Eine Druckseite der Bauvorlage: Zeile (A, B, …) und Spalte (1, 2, …), Ausschnitt des Plans in mm.</summary>
public sealed record PrintTile(int Row, int Column, double X0, double Y0, double X1, double Y1)
{
    public string Label => $"{PrintTiles.RowName(Row)}{Column + 1}";
}

/// <summary>
/// Aufteilung des Plans im Maßstab 1:1 auf Druckseiten. Benachbarte Seiten überlappen um die Klebekante,
/// damit sie sich passgenau zusammenkleben lassen; die Passmarken liegen auf einem festen 100-mm-Raster des Plans.
/// </summary>
public static class PrintTiles
{
    public const double MarkSpacing = 100;

    public static string RowName(int row) => row < 26 ? ((char)('A' + row)).ToString() : $"{(char)('A' + row / 26 - 1)}{(char)('A' + row % 26)}";

    /// <param name="usableWidth">Bedruckbare Breite einer Seite in mm (ohne Rand).</param>
    /// <param name="usableHeight">Bedruckbare Höhe einer Seite in mm.</param>
    /// <param name="overlap">Klebekante in mm.</param>
    public static IReadOnlyList<PrintTile> Layout((double X0, double Y0, double X1, double Y1) area, double usableWidth, double usableHeight, double overlap = 10)
    {
        if (usableWidth <= overlap || usableHeight <= overlap) throw new ArgumentException(Lang.T("Die Seite ist kleiner als die Klebekante."));
        double stepX = usableWidth - overlap, stepY = usableHeight - overlap;
        double w = Math.Max(area.X1 - area.X0, 1), h = Math.Max(area.Y1 - area.Y0, 1);
        var cols = Math.Max(1, (int)Math.Ceiling((w - overlap) / stepX));
        var rows = Math.Max(1, (int)Math.Ceiling((h - overlap) / stepY));
        // Ausschnitt mittig über den Plan legen, damit der Rand rundum gleich ist
        var x0 = area.X0 - (cols * stepX + overlap - w) / 2;
        var y0 = area.Y0 - (rows * stepY + overlap - h) / 2;
        var tiles = new List<PrintTile>(rows * cols);
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                double tx = x0 + c * stepX, ty = y0 + r * stepY;
                tiles.Add(new PrintTile(r, c, tx, ty, tx + usableWidth, ty + usableHeight));
            }
        return tiles;
    }

    /// <summary>Passmarken auf einer Seite: Punkte des 100-mm-Rasters innerhalb des Ausschnitts.</summary>
    public static IEnumerable<(double X, double Y)> Marks(PrintTile t)
    {
        for (var x = Math.Ceiling(t.X0 / MarkSpacing) * MarkSpacing; x <= t.X1; x += MarkSpacing)
            for (var y = Math.Ceiling(t.Y0 / MarkSpacing) * MarkSpacing; y <= t.Y1; y += MarkSpacing)
                yield return (x, y);
    }
}
