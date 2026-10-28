namespace TrackMaster.Core;

/// <summary>Grube und Bühne einer verlegten Drehscheibe oder Schiebebühne in Weltkoordinaten (für Plan, Druck, SVG und 3D).</summary>
public sealed record TableShape(bool Round, (double X, double Y) Center, double PitRadius, double RimRadius,
                                IReadOnlyList<(double X, double Y)> Pit, (double X, double Y) BridgeA, (double X, double Y) BridgeB)
{
    /// <summary>Liegt der Punkt auf Grube oder Ring (zum Anklicken)?</summary>
    public bool Contains(double x, double y)
        => Round ? Math.Sqrt((x - Center.X) * (x - Center.X) + (y - Center.Y) * (y - Center.Y)) <= RimRadius : Geometry2D.PointInPolygon(Pit, x, y);

    /// <summary>Drehscheibe oder Schiebebühne unter dem Punkt (die oberste, wenn mehrere übereinanderliegen).</summary>
    public static PlacedPiece? Hit(LayoutPlan plan, double x, double y)
        => plan.Pieces.Where(p => p.Def.IsTable && Of(p)!.Contains(x, y)).OrderByDescending(p => p.Height).FirstOrDefault();

    public static TableShape? Of(PlacedPiece p)
    {
        var d = p.Def;
        (double X, double Y) W(double x, double y) { var q = p.ToWorld(new Pose(x, y, 0)); return (q.X, q.Y); }
        if (d.Kind == PieceKind.Turntable)
        {
            var (cx, cy) = d.Center;
            var pit = Enumerable.Range(0, 48).Select(i => W(cx + d.PitRadius * Math.Cos(i * Math.PI / 24), cy + d.PitRadius * Math.Sin(i * Math.PI / 24))).ToList();
            // Bühne in Richtung Anschluss 0
            return new TableShape(true, W(cx, cy), d.PitRadius, d.RimRadius, pit, W(cx - d.PitRadius, cy), W(cx + d.PitRadius, cy));
        }
        if (d.Kind == PieceKind.TransferTable)
        {
            var (x0, y0, x1, y1) = d.PitBox;
            return new TableShape(false, W((x0 + x1) / 2, (y0 + y1) / 2), 0, 0, [W(x0, y0), W(x1, y0), W(x1, y1), W(x0, y1)], W(x0, 0), W(x1, 0));
        }
        return null;
    }
}
