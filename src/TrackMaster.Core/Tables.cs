namespace TrackMaster.Core;

/// <summary>
/// Drehscheiben und Schiebebühnen. Maße aus der Gleisbibliothek von XTrackCAD (Ordner params, Dateien je Hersteller),
/// umgerechnet von Zoll in mm: Radius der Anschlüsse, Grube und Ring, Teilung bzw. Lage der Gleisanschlüsse.
/// Sie werden dem Gleissystem des Herstellers zugeordnet.
/// </summary>
internal static class Tables
{
    public static string Category => Lang.T("Drehscheiben und Schiebebühnen");

    private sealed record Entry(string Gauge, string[] Systems, Func<double, TrackPieceDef> Make);

    private static readonly Entry[] Entries =
    [
        // Z (mrklnz.xtp, Z-Rokuhan.xtp)
        new("Z", ["mini"], g => Turntable("8599", Lang.T("Drehscheibe"), 85, 66, 85, 15, 24, g)),
        new("Z", ["mini"], _ => Transfer("8994", Lang.T("Schiebebühne"), 0.34, [195.49, 160.46, 110.42, 85.40, 35.37], 220.51, [195.49, 160.46, 110.42, 85.40, 35.37], (0.34, 0.34, 220.51, 220.51))),
        new("Z", ["rokuhan"], g => Turntable("S037", Lang.T("Drehscheibe"), 85, 85, 85, 15, 24, g)),
        // N (N-Kato Track and Structures.xtp, N-PecoCode55Finescale.xtp)
        new("N", ["kato"], g => Turntable("20-283", Lang.T("Drehscheibe"), 80, 80, 108.5, 10, 36, g)),
        new("N", ["peco-n", "peco-n80"], g => Turntable("NB-55", Lang.T("Drehscheibe"), 81.8, 76.2, 81.8, 10, 36, g)),
        // TT (TT-Roco-Turntable.xtp): passt mit Übergangsgleis an das Modellgleis
        new("TT", ["tillig"], g => Turntable("35900", Lang.T("Drehscheibe (Roco)"), 91.5, 91.5, 128.5, 15, 24, g)),
        // H0 (HO-Roco HO Code 83.xtp, fl-profi.xtp, fl-model.xtp, mrkhomde.xtp, HO-Marklin M-Track.xtp)
        new("H0", ["rocoline"], g => Turntable("42615", Lang.T("Drehscheibe"), 126.5, 126.5, 153.5, 9, 40, g)),
        new("H0", ["profi"], g => Turntable("6154", Lang.T("Drehscheibe"), 227.5, 155, 227.5, 7.5, 48, g)),
        new("H0", ["fl-model"], g => Turntable("6050", Lang.T("Drehscheibe (Handbetrieb)"), 111, 82.5, 111, 15, 24, g)),
        new("H0", ["fl-model"], g => Turntable("6052", Lang.T("Drehscheibe elektrisch"), 111, 82.5, 111, 15, 24, g)),
        new("H0", ["mm"], g => Turntable("7186", Lang.T("Drehscheibe"), 193, 155, 193, 7.5, 48, g)),
        new("H0", ["mm"], _ => Transfer("7294", Lang.T("Schiebebühne"), 0, [154.40, 77.40, 0, -77.40, -154.80], 360, [142.0, 64.6, -12.8, -90.2, -154.8], (0, -210.3, 360, 209.7))),
        // G (lgb.xtp)
        new("G", ["lgb"], g => Turntable("14000", Lang.T("Drehscheibe"), 370, 235, 370, 22.5, 16, g)),
    ];

    /// <summary>Hängt die Drehscheiben und Schiebebühnen an die Gleissysteme ihrer Hersteller an.</summary>
    public static IReadOnlyList<Gauge> AddTo(IReadOnlyList<Gauge> gauges)
    {
        foreach (var e in Entries)
        {
            var gauge = gauges.First(g => g.Id == e.Gauge);
            foreach (var id in e.Systems)
                gauge.Systems.First(s => s.Id == id).Append(e.Make(gauge.TrackWidth));
        }
        return gauges;
    }

    /// <summary>
    /// Drehscheibe: Anschlüsse im Abstand <paramref name="step"/> rundum auf dem Radius <paramref name="r"/>; Anschluss 0 liegt im Ursprung,
    /// die Mitte rechts davon. Liegen die Anschlüsse außerhalb der Grube, gehört das Gleisstück bis zur Grube zur Drehscheibe.
    /// </summary>
    private static TrackPieceDef Turntable(string nr, string name, double r, double pit, double rim, double step, int count, double trackWidth)
    {
        var ports = Enumerable.Range(0, count).Select(k =>
        {
            var a = k * step * Math.PI / 180;
            return new Pose(r - r * Math.Cos(a), -r * Math.Sin(a), 180 + k * step);
        }).ToList();
        return new TrackPieceDef
        {
            Number = nr, Kind = PieceKind.Turntable, Name = name, Category = Category, Ports = ports,
            Center = (r, 0), PitRadius = pit, RimRadius = Math.Max(rim, r), Radius = r, Angle = step,
            Length = r - pit > 1 ? r - pit : trackWidth,
            Spec = Lang.F($"Ø {Fmt.Num(2 * Math.Max(rim, r), 0)} mm · Bühne {Fmt.Num(2 * pit, 0)} mm · {count} × {Fmt.Num(step)}°"),
        };
    }

    /// <summary>Schiebebühne: Anschlüsse links (Richtung −x) und rechts (+x) mit ihrer Querlage; Anschluss 0 liegt im Ursprung.</summary>
    private static TrackPieceDef Transfer(string nr, string name, double xLeft, double[] left, double xRight, double[] right, (double X0, double Y0, double X1, double Y1) box)
    {
        // Herstellerdaten haben y nach oben, der Plan nach unten
        double ox = xLeft, oy = -left[0];
        var ports = left.Select(y => new Pose(xLeft - ox, -y - oy, 180))
            .Concat(right.Select(y => new Pose(xRight - ox, -y - oy, 0))).ToList();
        var pit = (box.X0 - ox, -box.Y1 - oy, box.X1 - ox, -box.Y0 - oy);
        return new TrackPieceDef
        {
            Number = nr, Kind = PieceKind.TransferTable, Name = name, Category = Category, Ports = ports, PitBox = pit,
            Length = 20, Radius = 0, Angle = 0,
            Spec = Lang.F($"{Fmt.Num(box.X1 - box.X0, 0)} × {Fmt.Num(box.Y1 - box.Y0, 0)} mm · {left.Length} + {right.Length} Anschlüsse"),
        };
    }
}

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
