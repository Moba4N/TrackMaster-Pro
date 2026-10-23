namespace TrackMaster.Core;

public enum PieceKind { Straight, Curve, Switch, Crossing, Buffer }

/// <summary>Teilstück eines Weichenstrangs: gerade (Turn = 0) oder Bogen (Turn = +1 rechts, −1 links).</summary>
public readonly record struct Segment(double Length, double Radius = 0, double Angle = 0, int Turn = 0)
{
    public static Segment Line(double length) => new(length);
    public static Segment Arc(double radius, double angle, bool right) => new(0, radius, angle, right ? 1 : -1);
}

/// <summary>Ein Gleisstück aus dem Katalog eines Herstellers. Maße in Millimetern, Winkel in Grad.</summary>
public sealed class TrackPieceDef
{
    public required string Number { get; init; }
    public required PieceKind Kind { get; init; }
    public double Length { get; init; }
    public double Radius { get; init; }
    public double Angle { get; init; }
    /// <summary>Weiche rechts bzw. Bogen nach rechts. Bögen sind im Katalog Linksbögen und werden beim Verlegen gespiegelt.</summary>
    public bool RightHand { get; init; }
    /// <summary>
    /// Stränge einer Weiche, wenn sie nicht aus einer Geraden und einem Bogen besteht (Dreiwegweiche, Bogenweiche,
    /// Y-Weiche, Abzweig mit anschließender Geraden). Strang 0 ist der Stammstrang.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Segment>>? Paths { get; init; }
    /// <summary>Maße dieses Teils noch nicht geprüft, auch wenn das System geprüft ist.</summary>
    public bool Unchecked { get; init; }
    public required string Name { get; init; }
    public required string Spec { get; init; }
    public required string Category { get; init; }

    public string GaugeId { get; internal set; } = "";
    public string SystemId { get; internal set; } = "";
    public string SystemName { get; internal set; } = "";
    public bool Verified { get; internal set; }
    /// <summary>Eindeutiger Schlüssel „Spur|System|Artikel“, so steht er auch in der Plandatei.</summary>
    public string Key { get; internal set; } = "";

    /// <summary>Gleislänge für Stückliste und Statistik (bei Weichen der Stammstrang).</summary>
    public double TrackLength => Kind == PieceKind.Curve ? Radius * Angle * Math.PI / 180
        : Paths is { } p ? p[0].Sum(s => s.Turn == 0 ? s.Length : s.Radius * s.Angle * Math.PI / 180) : Length;
    public bool CanFlip => Kind is PieceKind.Curve or PieceKind.Switch or PieceKind.Crossing;
    /// <summary>Flexgleis mit frei gewählter Länge bzw. Radius und Winkel.</summary>
    public bool IsFlex => Catalog.IsFlexSystem(SystemId);
    /// <summary>Einfache Weiche aus Gerade und Bogen (für Schnellwahl und Eigenschaften).</summary>
    public bool IsSimpleSwitch => Kind == PieceKind.Switch && Paths is null;
    /// <summary>Doppelte Kreuzungsweiche: zwei Antriebe.</summary>
    public bool IsDoubleSlip => Kind == PieceKind.Crossing && Name.StartsWith("DKW");
    /// <summary>Anzahl Weichenantriebe (Dreiwegweiche und DKW zwei).</summary>
    public int Drives => Kind == PieceKind.Switch ? Math.Max(1, (Paths?.Count ?? 2) - 1) : IsDoubleSlip ? 2 : 0;
}

public sealed class TrackSystem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>true = Maße und Artikelnummern aus dem Herstellerkatalog geprüft.</summary>
    public bool Verified { get; init; }
    /// <summary>Mittelleiter (Märklin H0): Kehrschleifen brauchen keine Sonderschaltung.</summary>
    public bool ThreeRail { get; init; }
    /// <summary>Flexgleis des Herstellers: Artikelnummer (falls bekannt) und Länge eines Stücks. Ohne Angabe gilt das allgemeine Flexgleis der Spur.</summary>
    public string? FlexNumber { get; init; }
    public double? FlexLength { get; init; }
    public required IReadOnlyList<TrackPieceDef> Pieces { get; init; }
}

/// <summary>Flexgleis, wie es im Handel verkauft wird: Systemschlüssel für den Katalog, Artikel, Länge eines Stücks.</summary>
public sealed record FlexStock(string SystemId, string Number, double Length, string Name);

public sealed class Gauge
{
    public required string Id { get; init; }
    public required string Scale { get; init; }
    /// <summary>Spurweite in mm.</summary>
    public required double TrackWidth { get; init; }
    public required double DefaultBoardWidth { get; init; }
    public required double DefaultBoardHeight { get; init; }
    /// <summary>Lichte Höhe über Schienenoberkante, die eine darüberliegende Strecke mindestens braucht (inkl. Trasse), in mm.</summary>
    public required double ClearanceHeight { get; init; }
    /// <summary>Länge eines Flexgleis-Stücks im Handel, für die Stückliste.</summary>
    public required double FlexStockLength { get; init; }
    public required IReadOnlyList<TrackSystem> Systems { get; init; }
}

/// <summary>
/// Gleiskatalog aller Spuren. Märklin C-Gleis und Trix C-Gleis sind mit Herstellerangaben hinterlegt,
/// die übrigen Systeme haben vorläufige Maße (Verified = false) und werden in der Oberfläche so gekennzeichnet.
/// </summary>
public static class Catalog
{
    private static TrackPieceDef S(string nr, double len, string name = "Gerade") => new()
        { Number = nr, Kind = PieceKind.Straight, Length = len, Name = name, Spec = $"{Fmt.Num(len)} mm", Category = name == "Gerade" ? "Gerade" : "Sonstige" };

    private static TrackPieceDef C(string nr, double r, double a, string? radiusName = null) => new()
        { Number = nr, Kind = PieceKind.Curve, Radius = r, Angle = a, Name = $"Bogen {(radiusName is null ? "" : radiusName + " · ")}{Fmt.Num(a)}°", Spec = $"R {Fmt.Num(r)} mm", Category = "Bögen" };

    /// <summary>Einfache Weiche: gerader Strang <paramref name="len"/>, Abzweig als Bogen, optional mit gerader Verlängerung <paramref name="tail"/>.</summary>
    private static TrackPieceDef W(string nr, bool right, double len, double r, double a, string? extra = null, double tail = 0) => new()
        { Number = nr, Kind = PieceKind.Switch, RightHand = right, Length = len, Radius = r, Angle = a,
          Paths = tail > 0 ? [[Segment.Line(len)], [Segment.Arc(r, a, right), Segment.Line(tail)]] : null,
          Name = $"Weiche {(right ? "rechts" : "links")}{(extra is null ? "" : " " + extra)}", Spec = $"{Fmt.Num(len)} mm · R {Fmt.Num(r)} · {Fmt.Num(a)}°", Category = "Weichen" };

    /// <summary>Weichenpaar links und rechts.</summary>
    private static TrackPieceDef[] WW(string left, string right, double len, double r, double a, string? extra = null, double tail = 0)
        => [W(left, false, len, r, a, extra, tail), W(right, true, len, r, a, extra, tail)];

    /// <summary>Dreiwegweiche: gerade und je ein Abzweig nach links und rechts.</summary>
    private static TrackPieceDef W3(string nr, double len, double r, double a) => new()
        { Number = nr, Kind = PieceKind.Switch, Length = len, Radius = r, Angle = a,
          Paths = [[Segment.Line(len)], [Segment.Arc(r, a, false)], [Segment.Arc(r, a, true)]],
          Name = "Dreiwegweiche", Spec = $"{Fmt.Num(len)} mm · R {Fmt.Num(r)} · {Fmt.Num(a)}°", Category = "Weichen" };

    /// <summary>Y-Weiche: beide Stränge biegen gleich weit nach links und rechts ab.</summary>
    private static TrackPieceDef Y(string nr, double r, double a, double tail = 0) => new()
        { Number = nr, Kind = PieceKind.Switch, Radius = r, Angle = a,
          Paths = tail > 0 ? [[Segment.Arc(r, a, false), Segment.Line(tail)], [Segment.Arc(r, a, true), Segment.Line(tail)]] : [[Segment.Arc(r, a, false)], [Segment.Arc(r, a, true)]],
          Name = "Y-Weiche", Spec = $"R {Fmt.Num(r)} · 2 × {Fmt.Num(a)}°", Category = "Weichen" };

    /// <summary>Bogenweiche: beide Stränge biegen zur selben Seite, außen der größere Radius, innen optional mit Vorlauf.</summary>
    private static TrackPieceDef BW(string nr, bool right, double rOuter, double aOuter, double rInner, double aInner, double lead = 0, bool uncheckedGeometry = true) => new()
        { Number = nr, Kind = PieceKind.Switch, RightHand = right, Radius = rInner, Angle = aInner, Unchecked = uncheckedGeometry,
          Paths = [lead > 0 ? [Segment.Line(lead), Segment.Arc(rOuter, aOuter, right)] : [Segment.Arc(rOuter, aOuter, right)], [Segment.Arc(rInner, aInner, right)]],
          Name = $"Bogenweiche {(right ? "rechts" : "links")}", Spec = $"R {Fmt.Num(rInner)} / {Fmt.Num(rOuter)} · {Fmt.Num(aInner)}°", Category = "Weichen" };

    private static TrackPieceDef[] BWW(string left, string right, double rOuter, double aOuter, double rInner, double aInner, double lead = 0, bool uncheckedGeometry = true)
        => [BW(left, false, rOuter, aOuter, rInner, aInner, lead, uncheckedGeometry), BW(right, true, rOuter, aOuter, rInner, aInner, lead, uncheckedGeometry)];

    private static TrackPieceDef X(string nr, double len, double a, bool doubleSlip = false) => new()
        { Number = nr, Kind = PieceKind.Crossing, Length = len, Angle = a, Name = doubleSlip ? $"DKW {Fmt.Num(a)}°" : $"Kreuzung {Fmt.Num(a)}°", Spec = $"{Fmt.Num(len)} mm", Category = "Kreuzungen" };

    private static TrackPieceDef P(string nr, double len) => new()
        { Number = nr, Kind = PieceKind.Buffer, Length = len, Name = "Prellbock", Spec = $"{Fmt.Num(len)} mm", Category = "Sonstige" };

    /// <summary>Märklin C-Gleis; Trix C-Gleis hat dieselben Maße mit Nummern 62… statt 24….</summary>
    private static List<TrackPieceDef> CGleis(string prefix, bool contactTracks) =>
    [
        S($"{prefix}360", 360), S($"{prefix}236", 236.1), S($"{prefix}229", 229.3), S($"{prefix}188", 188.3), S($"{prefix}172", 171.7),
        S($"{prefix}094", 94.2), S($"{prefix}077", 77.5), S($"{prefix}071", 70.8), S($"{prefix}064", 64.3),
        .. (contactTracks ? new[] { S($"{prefix}997", 94.2, "Entkupplungsgleis"), S($"{prefix}994", 94.2, "Schaltgleis") } : []),
        C($"{prefix}130", 360, 30, "R1"), C($"{prefix}115", 360, 15, "R1"), C($"{prefix}107", 360, 7.5, "R1"),
        C($"{prefix}230", 437.5, 30, "R2"), C($"{prefix}224", 437.5, 24.3, "R2"), C($"{prefix}215", 437.5, 15, "R2"), C($"{prefix}207", 437.5, 7.5, "R2"),
        C($"{prefix}330", 515, 30, "R3"), C($"{prefix}430", 579.3, 30, "R4"), C($"{prefix}530", 643.6, 30, "R5"), C($"{prefix}912", 1114.6, 12.1, "R9"),
        .. WW($"{prefix}611", $"{prefix}612", 188.3, 437.5, 22.5),
        .. WW($"{prefix}711", $"{prefix}712", 236.1, 1114.6, 12.1, "schlank"),
        .. BWW($"{prefix}671", $"{prefix}672", 437.5, 30, 360, 30),
        W3($"{prefix}630", 188.3, 437.5, 22.5),
        X($"{prefix}640", 188.3, 22.5), X($"{prefix}740", 236.1, 12.1), X($"{prefix}624", 188.3, 22.5, doubleSlip: true),
        P($"{prefix}977", 77.5),
    ];

    public static IReadOnlyList<Gauge> Gauges { get; } =
    [
        new() { Id = "Z", ClearanceHeight = 30, FlexStockLength = 600, Scale = "1:220", TrackWidth = 6.5, DefaultBoardWidth = 900, DefaultBoardHeight = 500, Systems =
        [
            new() { Id = "mini", Name = "Märklin mini-club", FlexLength = 660, Pieces =
            [
                S("8500", 110), S("G110", 110), S("G55", 55), S("G27,5", 27.5), S("G220", 220),
                C("8510", 145, 45, "R1"), C("8520", 195, 45, "R2"), C("8530", 220, 30, "R3"),
                C("R1", 145, 45, "R1"), C("R2", 195, 45, "R2"), C("R3", 220, 30, "R3"), C("R2-13", 195, 13, "R2"), C("R490-13", 490, 13),
                .. WW("8565", "8566", 110, 490, 13), W("WL", false, 110, 490, 13), W("WR", true, 110, 490, 13), .. BWW("BWL", "BWR", 220, 30, 195, 30),
                X("K13", 110, 13), X("DKW13", 110, 13, doubleSlip: true), P("PB", 27),
            ] },
            new() { Id = "rokuhan", Name = "Rokuhan", Pieces =
            [
                S("G110", 110), S("G55", 55), S("G220", 220), S("G27,5", 27.5),
                C("R120-45", 120, 45), C("R145-45", 145, 45), C("R170-45", 170, 45), C("R195-45", 195, 45), C("R220-30", 220, 30),
                .. WW("WL", "WR", 110, 490, 13), X("K13", 110, 13), P("PB", 55),
            ] },
        ] },
        new() { Id = "N", ClearanceHeight = 45, FlexStockLength = 914, Scale = "1:160", TrackWidth = 9, DefaultBoardWidth = 1600, DefaultBoardHeight = 800, Systems =
        [
            new() { Id = "kato", Name = "Kato Unitrack", Pieces =
            [
                S("S248", 248), S("S186", 186), S("S124", 124), S("S64", 64), S("S62", 62), S("S45.5", 45.5), S("S29", 29),
                C("R216-45", 216, 45), C("R249-45", 249, 45), C("R282-45", 282, 45), C("R315-45", 315, 45), C("R348-45", 348, 45),
                C("R381-15", 381, 15), C("R481-15", 481, 15), C("R718-15", 718, 15), C("R249-15", 249, 15), C("R282-15", 282, 15),
                W("EP481-15L", false, 186, 481, 15), W("EP481-15R", true, 186, 481, 15),
                .. WW("EP718-15L", "EP718-15R", 186, 718, 15, "#6"),
                X("X124-15", 124, 15), X("X186-15", 186, 15), P("E62", 62),
            ] },
            new() { Id = "picc", Name = "Fleischmann piccolo", FlexLength = 730, Pieces =
            [
                S("G222", 222), S("G111", 111), S("G104", 104.2), S("G55,5", 55.5), S("G27,75", 27.75),
                C("R1", 192, 30, "R1"), C("R1-45", 192, 45, "R1"), C("R1-15", 192, 15, "R1"),
                C("R2", 225.6, 30, "R2"), C("R2-45", 225.6, 45, "R2"), C("R2-15", 225.6, 15, "R2"), C("R3-45", 258.6, 45, "R3"), C("R4-45", 291.6, 45, "R4"),
                W("WL", false, 111, 225.6, 15), W("WR", true, 111, 225.6, 15),
                .. WW("WL-S", "WR-S", 111, 430, 15, "schlank"), .. BWW("BWL", "BWR", 225.6, 30, 192, 30),
                S("EG55,5", 55.5, "Entkupplungsgleis"), W3("DWW", 111, 225.6, 15),
                X("K15", 111, 15), X("K30", 111, 30), X("DKW15", 111, 15, doubleSlip: true), P("PB", 40),
            ] },
            new() { Id = "rf-n", Name = "Roco/Fleischmann ohne Bettung", FlexLength = 730, Pieces =
            [
                S("G222", 222), S("G111", 111), S("G104,2", 104.2), S("G55,5", 55.5), S("G27,75", 27.75),
                C("R1-30", 192, 30, "R1"), C("R1-15", 192, 15, "R1"), C("R2-30", 225.6, 30, "R2"), C("R2-15", 225.6, 15, "R2"),
                C("R3-30", 258.6, 30, "R3"), C("R4-30", 291.6, 30, "R4"), C("R5-30", 324.6, 30, "R5"),
                .. WW("WL", "WR", 111, 225.6, 15), .. WW("WL-S", "WR-S", 111, 430, 15, "schlank"),
                .. BWW("BWL", "BWR", 225.6, 30, 192, 30), W3("DWW", 111, 225.6, 15),
                S("EG55,5", 55.5, "Entkupplungsgleis"),
                X("K15", 111, 15), X("K30", 111, 30), X("DKW15", 111, 15, doubleSlip: true), P("PB", 40),
            ] },
            new() { Id = "minitrix", Name = "Minitrix", FlexLength = 750, Pieces =
            [
                S("14903", 312.6), S("14904", 104.2), S("G54,2", 54.2), S("G50,7", 50.7), S("G33,6", 33.6),
                C("14912", 192, 30, "R1"), C("R1-15", 192, 15, "R1"), C("R2", 228, 30, "R2"), C("R2-15", 228, 15, "R2"),
                C("R3", 263.8, 30, "R3"), C("R4", 297.6, 30, "R4"), C("R5", 331.4, 30, "R5"),
                .. WW("14938", "14939", 104.2, 228, 15), .. WW("WL-S", "WR-S", 104.2, 490, 15, "schlank"),
                X("K15", 104.2, 15), X("DKW15", 104.2, 15, doubleSlip: true), P("PB", 54.2),
            ] },
            new() { Id = "peco-n", Name = "Peco Streamline N (Code 55)", FlexNumber = "SL-300F", FlexLength = 914, Pieces =
            [
                S("G110", 110), S("G55", 55),
                .. WW("WL-M", "WR-M", 111, 305, 12, "mittel"), .. WW("WL-K", "WR-K", 111, 228, 15, "kurz"),
                .. WW("WL-L", "WR-L", 140, 914, 8, "lang"), Y("Y", 457, 6),
                X("K10", 112, 10), X("DKW10", 112, 10, doubleSlip: true), P("PB", 30),
            ] },
        ] },
        new() { Id = "TT", ClearanceHeight = 60, FlexStockLength = 890, Scale = "1:120", TrackWidth = 12, DefaultBoardWidth = 2000, DefaultBoardHeight = 1000, Systems =
        [
            new() { Id = "tillig", Name = "Tillig Modellgleis", FlexLength = 890, Pieces =
            [
                S("G1", 166), S("G2", 83), S("G3", 41.5), S("G4", 43), S("G5", 207.5),
                C("R11", 267, 30, "R11"), C("R12", 310, 30, "R12"), C("R13", 353, 30, "R13"), C("R14", 396, 30, "R14"),
                C("R11/15", 267, 15, "R11"), C("R12/15", 310, 15, "R12"), C("R13/15", 353, 15, "R13"),
                W("EW L", false, 166, 396, 15), W("EW R", true, 166, 396, 15),
                .. WW("EW2 L", "EW2 R", 207.5, 722, 12, "schlank"), W3("DWW", 166, 396, 15), .. BWW("BW L", "BW R", 396, 30, 353, 30),
                X("K15", 166, 15), X("DKW15", 166, 15, doubleSlip: true), P("PB", 50),
            ] },
            new() { Id = "piko-tt", Name = "Piko TT", Pieces =
            [
                S("G166", 166), S("G83", 83), S("G41,5", 41.5), S("G43", 43),
                C("R1", 267, 30, "R1"), C("R2", 310, 30, "R2"), C("R3", 353, 30, "R3"), C("R4", 396, 30, "R4"),
                .. WW("WL", "WR", 166, 396, 15), X("K15", 166, 15), P("PB", 41.5),
            ] },
        ] },
        new() { Id = "H0", ClearanceHeight = 80, FlexStockLength = 914, Scale = "1:87", TrackWidth = 16.5, DefaultBoardWidth = 2000, DefaultBoardHeight = 1250, Systems =
        [
            new() { Id = "mc", Name = "Märklin C-Gleis", Verified = true, ThreeRail = true, Pieces = CGleis("24", true) },
            new() { Id = "tc", Name = "Trix C-Gleis", Verified = true, Pieces = CGleis("62", false) },
            new() { Id = "mk", Name = "Märklin K-Gleis", ThreeRail = true, FlexNumber = "2205", FlexLength = 900, Pieces =
            [
                S("2200", 180), S("2201", 90), S("G45", 45), S("G33,5", 33.5), S("G30", 30), S("G168,9", 168.9),
                C("2221", 295.4, 30, "R1"), C("R1-15", 295.4, 15, "R1"), C("2231", 360, 30, "R2"), C("R2-15", 360, 15, "R2"), C("R2-7,5", 360, 7.5, "R2"),
                C("2241", 424.6, 30, "R3"), C("R3-15", 424.6, 15, "R3"), C("R4", 489.2, 30, "R4"),
                .. WW("2262", "2263", 168.9, 424.6, 22.5), .. WW("WL-S", "WR-S", 225, 1114.6, 12.1, "schlank"),
                W3("2270", 168.9, 424.6, 22.5), .. BWW("BWL", "BWR", 424.6, 30, 360, 30),
                X("K22,5", 168.9, 22.5), X("2260", 168.9, 22.5, doubleSlip: true), P("PB", 33.5),
            ] },
            new() { Id = "geo", Name = "Roco geoLine", Pieces =
            [
                S("G1", 200), S("G½", 100), S("61110", 230), S("61111", 115), S("G76,5", 76.5), S("G57,5", 57.5), S("G185", 185),
                C("R2", 358, 30, "R2"), C("R3", 419.6, 30, "R3"), C("R4", 481.2, 30, "R4"), C("R5", 542.8, 30, "R5"),
                C("R2-15", 358, 15, "R2"), C("R4-15", 481.2, 15, "R4"), C("R10-10", 888, 10, "R10"),
                W("WL10", false, 200, 873.5, 10), W("WR10", true, 200, 873.5, 10),
                .. BWW("BWL", "BWR", 481.2, 30, 419.6, 30), W3("DWW", 230, 873.5, 10),
                X("K10", 230, 10), X("DKW10", 230, 10, doubleSlip: true), P("PB", 100),
            ] },
            new() { Id = "rocoline", Name = "Roco Line (ohne Bettung)", FlexNumber = "42400", FlexLength = 920, Pieces =
            [
                S("G230", 230), S("G115", 115), S("G57,5", 57.5), S("G185", 185),
                C("R2", 358, 30, "R2"), C("R3", 419.6, 30, "R3"), C("R4", 481.2, 30, "R4"), C("R5", 542.8, 30, "R5"), C("R6", 604.4, 30, "R6"),
                .. WW("WL10", "WR10", 230, 873.5, 10), .. WW("WL15", "WR15", 345, 1946, 10, "schlank"),
                .. BWW("BWL", "BWR", 542.8, 30, 481.2, 30), W3("DWW", 230, 873.5, 10), Y("Y", 1600, 6),
                X("K10", 230, 10), X("DKW10", 230, 10, doubleSlip: true), P("PB", 57.5),
            ] },
            new() { Id = "piko", Name = "Piko A-Gleis", FlexLength = 940, Pieces =
            [
                S("G231", 231), S("G119", 119), S("G62", 61.9), S("G239", 239.07), S("G115", 115.5), S("G107", 107.32),
                C("R1", 360, 30, "R1"), C("R2", 421.9, 30, "R2"), C("R3", 483.8, 30, "R3"), C("R4", 545.6, 30, "R4"),
                C("R1-7,5", 360, 7.5, "R1"), C("R9", 908, 15, "R9"),
                W("WL", false, 239.1, 908, 15), W("WR", true, 239.1, 908, 15),
                W3("W3", 239.1, 908, 15), .. BWW("BWL", "BWR", 483.8, 30, 421.9, 30), Y("WY", 908, 7.5),
                X("K15", 239.1, 15), X("K30", 119.5, 30), X("DKW15", 239.1, 15, doubleSlip: true), P("PB", 62),
            ] },
            new() { Id = "profi", Name = "Fleischmann Profi-Gleis", FlexLength = 750, Pieces =
            [
                S("G200", 200), S("G105,6", 105.6), S("G100", 100), S("G50", 50),
                C("R1", 356.5, 30, "R1"), C("R2", 420, 30, "R2"), C("R2-15", 420, 15, "R2"), C("R3", 483.6, 30, "R3"), C("R4", 547, 30, "R4"),
                .. WW("WL18", "WR18", 200, 356.5, 18), .. WW("WL15", "WR15", 200, 960, 15, "schlank"),
                .. BWW("BWL", "BWR", 420, 30, 356.5, 30), W3("DWW", 200, 356.5, 18),
                X("K18", 200, 18), X("DKW18", 200, 18, doubleSlip: true), P("PB", 50),
            ] },
            new() { Id = "peco-h0", Name = "Peco Streamline H0 (Code 75)", FlexNumber = "SL-100F", FlexLength = 914, Pieces =
            [
                S("G228", 228), S("G114", 114),
                .. WW("WL-M", "WR-M", 220, 610, 12, "mittel"), .. WW("WL-L", "WR-L", 258, 1524, 12, "lang"),
                .. WW("WL-K", "WR-K", 189, 457, 12, "kurz"), Y("Y", 1524, 6), W3("DWW", 258, 1524, 12),
                .. BWW("BWL", "BWR", 1524, 15, 762, 22.5),
                X("K12", 258, 12), X("DKW12", 258, 12, doubleSlip: true), P("PB", 40),
            ] },
            new() { Id = "tillig-h0", Name = "Tillig Elite H0", FlexLength = 1000, Pieces =
            [
                S("G228", 228), S("G114", 114),
                .. WW("EW1 L", "EW1 R", 228, 866, 12, "EW1"), .. WW("EW3 L", "EW3 R", 314, 2500, 6.5, "EW3"),
                W3("DWW", 228, 866, 12), .. BWW("BWL", "BWR", 1000, 15, 800, 15),
                X("K15", 228, 15), X("DKW15", 228, 15, doubleSlip: true), P("PB", 40),
            ] },
        ] },
        new() { Id = "0", ClearanceHeight = 140, FlexStockLength = 914, Scale = "1:45", TrackWidth = 32, DefaultBoardWidth = 4000, DefaultBoardHeight = 2500, Systems =
        [
            new() { Id = "std0", Name = "Spur 0 Standard", Pieces =
            [
                S("G500", 500), S("G250", 250), S("G125", 125), S("G1000", 1000),
                C("R1", 1000, 22.5, "R1"), C("R2", 1250, 22.5, "R2"), C("R3", 1500, 15, "R3"), C("R1/11", 1000, 11.25, "R1"),
                W("WL", false, 500, 2000, 15), W("WR", true, 500, 2000, 15), .. WW("WL3", "WR3", 750, 3000, 12, "schlank"),
                W3("DWW", 500, 2000, 15), .. BWW("BWL", "BWR", 1500, 22.5, 1250, 22.5),
                X("K15", 500, 15), X("DKW15", 500, 15, doubleSlip: true), P("PB", 150),
            ] },
            new() { Id = "lenz0", Name = "Lenz Spur 0", Pieces =
            [
                S("G300", 300), S("G150", 150), S("G75", 75),
                C("R1-15", 1028, 15, "R1"), C("R1-7,5", 1028, 7.5, "R1"), C("R2-15", 1200, 15, "R2"),
                .. WW("WL", "WR", 400, 1028, 15), .. WW("WL-S", "WR-S", 700, 3000, 10, "schlank"),
                X("K15", 400, 15), P("PB", 75),
            ] },
        ] },
        new() { Id = "1", ClearanceHeight = 190, FlexStockLength = 1000, Scale = "1:32", TrackWidth = 45, DefaultBoardWidth = 5000, DefaultBoardHeight = 3000, Systems =
        [
            new() { Id = "m1", Name = "Märklin Spur 1", Pieces =
            [
                S("G450", 450), S("G225", 225), S("G112,5", 112.5), S("G56,25", 56.25),
                C("R1", 1020, 30, "R1"), C("R1-15", 1020, 15, "R1"), C("R2", 1194, 30, "R2"), C("R3", 1600, 22.5, "R3"),
                .. WW("WL", "WR", 450, 1020, 30), .. WW("WL-S", "WR-S", 900, 3600, 15, "schlank"),
                W3("DWW", 450, 1020, 30), X("K30", 450, 30), P("PB", 112.5),
            ] },
        ] },
        new() { Id = "G", ClearanceHeight = 230, FlexStockLength = 1500, Scale = "1:22,5", TrackWidth = 45, DefaultBoardWidth = 6000, DefaultBoardHeight = 4000, Systems =
        [
            new() { Id = "lgb", Name = "LGB", Pieces =
            [
                S("G300", 300), S("G150", 150), S("G75", 75), S("G600", 600), S("G1200", 1200),
                C("R1", 600, 30, "R1"), C("R1-15", 600, 15, "R1"), C("R2", 780, 30, "R2"), C("R3", 1175, 22.5, "R3"), C("R5", 2320, 15, "R5"),
                W("WL R1", false, 300, 600, 30), W("WR R1", true, 300, 600, 30),
                .. WW("WL R3", "WR R3", 450, 1175, 22.5, "R3"), .. WW("WL R5", "WR R5", 575, 2320, 15, "R5"),
                W3("DWW R1", 300, 600, 30), X("K30", 300, 30), X("DKW30", 300, 30, doubleSlip: true), P("PB", 150),
            ] },
        ] },
    ];

    private static readonly Dictionary<string, TrackPieceDef> ByKey = Index();

    private static Dictionary<string, TrackPieceDef> Index()
    {
        var map = new Dictionary<string, TrackPieceDef>();
        foreach (var gauge in Gauges)
            foreach (var system in gauge.Systems)
                foreach (var piece in system.Pieces)
                {
                    piece.GaugeId = gauge.Id;
                    piece.SystemId = system.Id;
                    piece.SystemName = system.Name;
                    piece.Verified = system.Verified && !piece.Unchecked;
                    piece.Key = $"{gauge.Id}|{system.Id}|{piece.Number}";
                    map[piece.Key] = piece;
                }
        return map;
    }

    public const string FlexSystemId = "flex";
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, TrackPieceDef?> FlexCache = new();

    /// <summary>Frühere Schlüssel, die in gespeicherten Plänen stehen können.</summary>
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["H0|mc|24620"] = "H0|mc|24624",
    };

    public static TrackPieceDef? Find(string key)
        => ByKey.TryGetValue(key, out var d) ? d
         : Aliases.TryGetValue(key, out var alias) ? ByKey[alias]
         : FlexCache.GetOrAdd(key, ParseFlex);
    public static TrackPieceDef Get(string key) => Find(key) ?? throw new KeyNotFoundException($"Unbekanntes Gleis „{key}“.");

    public static bool IsFlexSystem(string systemId) => systemId == FlexSystemId || systemId.StartsWith(FlexSystemId + "-", StringComparison.Ordinal);

    /// <summary>
    /// Flexgleis für ein Gleissystem: das des Herstellers (Systemschlüssel „flex-&lt;System&gt;“), sonst das allgemeine der Spur („flex“).
    /// </summary>
    public static FlexStock Flex(Gauge gauge, TrackSystem? system)
        => system is { FlexLength: { } len }
            ? new FlexStock($"{FlexSystemId}-{system.Id}", system.FlexNumber ?? "Flexgleis", len, system.Name)
            : new FlexStock(FlexSystemId, "Flexgleis", gauge.FlexStockLength, $"Spur {gauge.Id} allgemein");

    /// <summary>Flexgleis zu einem Systemschlüssel aus der Plandatei („flex“ oder „flex-&lt;System&gt;“).</summary>
    public static FlexStock? FlexOf(string gaugeId, string flexSystemId)
    {
        var gauge = Gauges.FirstOrDefault(g => g.Id == gaugeId);
        if (gauge is null) return null;
        if (flexSystemId == FlexSystemId) return Flex(gauge, null);
        if (!flexSystemId.StartsWith(FlexSystemId + "-", StringComparison.Ordinal)) return null;
        var system = gauge.Systems.FirstOrDefault(s => s.Id == flexSystemId[(FlexSystemId.Length + 1)..]);
        return system?.FlexLength is null ? null : Flex(gauge, system);
    }

    /// <summary>Schlüssel für ein gerades Flexgleis, z. B. „H0|flex|L500“ oder „H0|flex-mk|L500“.</summary>
    public static string FlexStraightKey(string gaugeId, double length, string flexSystemId = FlexSystemId)
        => $"{gaugeId}|{flexSystemId}|L{Math.Round(length, 1).ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>Schlüssel für ein gebogenes Flexgleis (Linksbogen, beim Verlegen spiegelbar), z. B. „H0|flex|R600A30“.</summary>
    public static string FlexCurveKey(string gaugeId, double radius, double angle, string flexSystemId = FlexSystemId)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return $"{gaugeId}|{flexSystemId}|R{Math.Round(radius, 1).ToString(inv)}A{Math.Round(angle, 2).ToString(inv)}";
    }

    private static TrackPieceDef? ParseFlex(string key)
    {
        var parts = key.Split('|');
        if (parts.Length != 3 || FlexOf(parts[0], parts[1]) is not { } stock) return null;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var spec = parts[2];
        TrackPieceDef? def = null;
        if (spec.StartsWith('L') && double.TryParse(spec[1..], System.Globalization.NumberStyles.Float, inv, out var len) && len is >= 5 and <= 5000)
            def = new TrackPieceDef { Number = stock.Number, Kind = PieceKind.Straight, Length = len, Name = "Flexgleis gerade", Spec = $"{Fmt.Num(len)} mm", Category = "Flexgleis" };
        else if (spec.StartsWith('R') && spec.IndexOf('A') is > 1 and var a
                 && double.TryParse(spec[1..a], System.Globalization.NumberStyles.Float, inv, out var r) && r is >= 50 and <= 20000
                 && double.TryParse(spec[(a + 1)..], System.Globalization.NumberStyles.Float, inv, out var ang) && ang is > 0 and <= 180)
            def = new TrackPieceDef { Number = stock.Number, Kind = PieceKind.Curve, Radius = r, Angle = ang, Name = $"Flexgleis Bogen {Fmt.Num(ang)}°", Spec = $"R {Fmt.Num(r)} mm", Category = "Flexgleis" };
        if (def is null) return null;
        def.GaugeId = parts[0];
        def.SystemId = parts[1];
        def.SystemName = parts[1] == FlexSystemId ? "Flexgleis" : $"Flexgleis {stock.Name}";
        def.Verified = parts[1] == FlexSystemId || GetSystem(parts[0], parts[1][(FlexSystemId.Length + 1)..])?.Verified == true;
        def.Key = key;
        return def;
    }

    public static TrackSystem? GetSystem(string gaugeId, string systemId) => GetGauge(gaugeId).Systems.FirstOrDefault(s => s.Id == systemId);
    public static Gauge GetGauge(string id) => Gauges.FirstOrDefault(g => g.Id == id) ?? Gauges.First(g => g.Id == "H0");
}
