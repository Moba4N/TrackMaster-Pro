namespace TrackMaster.Core;

public enum PieceKind { Straight, Curve, Switch, Crossing, Buffer }

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
    public required string Name { get; init; }
    public required string Spec { get; init; }
    public required string Category { get; init; }

    public string GaugeId { get; internal set; } = "";
    public string SystemId { get; internal set; } = "";
    public string SystemName { get; internal set; } = "";
    public bool Verified { get; internal set; }
    /// <summary>Eindeutiger Schlüssel „Spur|System|Artikel“, so steht er auch in der Plandatei.</summary>
    public string Key { get; internal set; } = "";

    /// <summary>Gleislänge für Stückliste und Statistik (bei Weichen der gerade Strang).</summary>
    public double TrackLength => Kind == PieceKind.Curve ? Radius * Angle * Math.PI / 180 : Length;
    public bool CanFlip => Kind is PieceKind.Curve or PieceKind.Switch or PieceKind.Crossing;
    /// <summary>Flexgleis mit frei gewählter Länge bzw. Radius und Winkel.</summary>
    public bool IsFlex => SystemId == Catalog.FlexSystemId;
}

public sealed class TrackSystem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>true = Maße und Artikelnummern aus dem Herstellerkatalog geprüft.</summary>
    public bool Verified { get; init; }
    /// <summary>Mittelleiter (Märklin H0): Kehrschleifen brauchen keine Sonderschaltung.</summary>
    public bool ThreeRail { get; init; }
    public required IReadOnlyList<TrackPieceDef> Pieces { get; init; }
}

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
/// Gleiskatalog aller Spuren. Nur Märklin C-Gleis ist mit Herstellerangaben hinterlegt,
/// die übrigen Systeme haben vorläufige Maße (Verified = false) und werden in der Oberfläche so gekennzeichnet.
/// </summary>
public static class Catalog
{
    private static TrackPieceDef S(string nr, double len) => new()
        { Number = nr, Kind = PieceKind.Straight, Length = len, Name = "Gerade", Spec = $"{Fmt.Num(len)} mm", Category = "Gerade" };

    private static TrackPieceDef C(string nr, double r, double a, string? radiusName = null) => new()
        { Number = nr, Kind = PieceKind.Curve, Radius = r, Angle = a, Name = $"Bogen {(radiusName is null ? "" : radiusName + " · ")}{Fmt.Num(a)}°", Spec = $"R {Fmt.Num(r)} mm", Category = "Bögen" };

    private static TrackPieceDef W(string nr, bool right, double len, double r, double a, string? extra = null) => new()
        { Number = nr, Kind = PieceKind.Switch, RightHand = right, Length = len, Radius = r, Angle = a,
          Name = $"Weiche {(right ? "rechts" : "links")}{(extra is null ? "" : " " + extra)}", Spec = $"{Fmt.Num(len)} mm · R {Fmt.Num(r)} · {Fmt.Num(a)}°", Category = "Weichen" };

    private static TrackPieceDef X(string nr, double len, double a, bool doubleSlip = false) => new()
        { Number = nr, Kind = PieceKind.Crossing, Length = len, Angle = a, Name = doubleSlip ? $"DKW {Fmt.Num(a)}°" : $"Kreuzung {Fmt.Num(a)}°", Spec = $"{Fmt.Num(len)} mm", Category = "Kreuzungen" };

    private static TrackPieceDef P(string nr, double len) => new()
        { Number = nr, Kind = PieceKind.Buffer, Length = len, Name = "Prellbock", Spec = $"{Fmt.Num(len)} mm", Category = "Sonstige" };

    public static IReadOnlyList<Gauge> Gauges { get; } =
    [
        new() { Id = "Z", ClearanceHeight = 30, FlexStockLength = 600, Scale = "1:220", TrackWidth = 6.5, DefaultBoardWidth = 900, DefaultBoardHeight = 500, Systems =
        [
            new() { Id = "mini", Name = "Märklin mini-club", Pieces = [S("G110", 110), S("G55", 55), C("R1", 145, 45, "R1"), C("R2", 195, 45, "R2"), C("R3", 220, 30, "R3"), W("WL", false, 110, 490, 13), W("WR", true, 110, 490, 13), P("PB", 27)] },
        ] },
        new() { Id = "N", ClearanceHeight = 45, FlexStockLength = 914, Scale = "1:160", TrackWidth = 9, DefaultBoardWidth = 1600, DefaultBoardHeight = 800, Systems =
        [
            new() { Id = "kato", Name = "Kato Unitrack", Pieces = [S("S248", 248), S("S124", 124), S("S62", 62), C("R249-45", 249, 45), C("R282-45", 282, 45), C("R315-45", 315, 45), W("EP481-15L", false, 186, 481, 15), W("EP481-15R", true, 186, 481, 15), P("E62", 62)] },
            new() { Id = "picc", Name = "Fleischmann piccolo", Pieces = [S("G111", 111), S("G104", 104.2), C("R1", 192, 30, "R1"), C("R2", 225.6, 30, "R2"), W("WL", false, 111, 225.6, 15), W("WR", true, 111, 225.6, 15), P("PB", 40)] },
        ] },
        new() { Id = "TT", ClearanceHeight = 60, FlexStockLength = 890, Scale = "1:120", TrackWidth = 12, DefaultBoardWidth = 2000, DefaultBoardHeight = 1000, Systems =
        [
            new() { Id = "tillig", Name = "Tillig Modellgleis", Pieces = [S("G1", 166), S("G2", 83), C("R11", 267, 30, "R11"), C("R12", 310, 30, "R12"), C("R13", 353, 30, "R13"), W("EW L", false, 166, 396, 15), W("EW R", true, 166, 396, 15), P("PB", 50)] },
        ] },
        new() { Id = "H0", ClearanceHeight = 80, FlexStockLength = 914, Scale = "1:87", TrackWidth = 16.5, DefaultBoardWidth = 2000, DefaultBoardHeight = 1250, Systems =
        [
            new() { Id = "mc", Name = "Märklin C-Gleis", Verified = true, ThreeRail = true, Pieces =
            [
                S("24188", 188.3), S("24172", 171.7), S("24236", 236.1), S("24360", 360), S("24094", 94.2), S("24071", 70.8), S("24064", 64.3),
                C("24130", 360, 30, "R1"), C("24115", 360, 15, "R1"), C("24230", 437.5, 30, "R2"), C("24215", 437.5, 15, "R2"), C("24207", 437.5, 7.5, "R2"),
                C("24330", 515, 30, "R3"), C("24430", 579.3, 30, "R4"), C("24530", 643.6, 30, "R5"),
                W("24611", false, 188.3, 437.5, 22.5), W("24612", true, 188.3, 437.5, 22.5),
                W("24711", false, 236.1, 1114.6, 12.1, "schlank"), W("24712", true, 236.1, 1114.6, 12.1, "schlank"),
                X("24640", 188.3, 22.5), X("24620", 188.3, 22.5, doubleSlip: true), P("24977", 77.5),
            ] },
            new() { Id = "geo", Name = "Roco geoLine", Pieces = [S("G1", 200), S("G½", 100), C("R2", 358, 30, "R2"), C("R3", 419.6, 30, "R3"), C("R4", 481.2, 30, "R4"), W("WL10", false, 200, 873.5, 10), W("WR10", true, 200, 873.5, 10), P("PB", 100)] },
            new() { Id = "piko", Name = "Piko A-Gleis", Pieces = [S("G231", 231), S("G119", 119), S("G62", 61.9), C("R1", 360, 30, "R1"), C("R2", 421.9, 30, "R2"), C("R3", 483.8, 30, "R3"), W("WL", false, 239.1, 908, 15), W("WR", true, 239.1, 908, 15), X("K15", 239.1, 15), P("PB", 62)] },
        ] },
        new() { Id = "0", ClearanceHeight = 140, FlexStockLength = 914, Scale = "1:45", TrackWidth = 32, DefaultBoardWidth = 4000, DefaultBoardHeight = 2500, Systems =
        [
            new() { Id = "std0", Name = "Spur 0 Standard", Pieces = [S("G500", 500), S("G250", 250), C("R1", 1000, 22.5, "R1"), C("R2", 1250, 22.5, "R2"), W("WL", false, 500, 2000, 15), W("WR", true, 500, 2000, 15), P("PB", 150)] },
        ] },
        new() { Id = "G", ClearanceHeight = 230, FlexStockLength = 1500, Scale = "1:22,5", TrackWidth = 45, DefaultBoardWidth = 6000, DefaultBoardHeight = 4000, Systems =
        [
            new() { Id = "lgb", Name = "LGB", Pieces = [S("G300", 300), S("G150", 150), C("R1", 600, 30, "R1"), C("R2", 780, 30, "R2"), C("R3", 1175, 22.5, "R3"), W("WL R1", false, 300, 600, 30), W("WR R1", true, 300, 600, 30), P("PB", 150)] },
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
                    piece.Verified = system.Verified;
                    piece.Key = $"{gauge.Id}|{system.Id}|{piece.Number}";
                    map[piece.Key] = piece;
                }
        return map;
    }

    public const string FlexSystemId = "flex";
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, TrackPieceDef?> FlexCache = new();

    public static TrackPieceDef? Find(string key) => ByKey.TryGetValue(key, out var d) ? d : FlexCache.GetOrAdd(key, ParseFlex);
    public static TrackPieceDef Get(string key) => Find(key) ?? throw new KeyNotFoundException($"Unbekanntes Gleis „{key}“.");

    /// <summary>Schlüssel für ein gerades Flexgleis, z. B. „H0|flex|L500“.</summary>
    public static string FlexStraightKey(string gaugeId, double length)
        => $"{gaugeId}|{FlexSystemId}|L{Math.Round(length, 1).ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>Schlüssel für ein gebogenes Flexgleis (Linksbogen, beim Verlegen spiegelbar), z. B. „H0|flex|R600A30“.</summary>
    public static string FlexCurveKey(string gaugeId, double radius, double angle)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return $"{gaugeId}|{FlexSystemId}|R{Math.Round(radius, 1).ToString(inv)}A{Math.Round(angle, 2).ToString(inv)}";
    }

    private static TrackPieceDef? ParseFlex(string key)
    {
        var parts = key.Split('|');
        if (parts.Length != 3 || parts[1] != FlexSystemId || Gauges.All(g => g.Id != parts[0])) return null;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var spec = parts[2];
        TrackPieceDef? def = null;
        if (spec.StartsWith('L') && double.TryParse(spec[1..], System.Globalization.NumberStyles.Float, inv, out var len) && len is >= 5 and <= 5000)
            def = new TrackPieceDef { Number = "Flex", Kind = PieceKind.Straight, Length = len, Name = "Flexgleis gerade", Spec = $"{Fmt.Num(len)} mm", Category = "Flexgleis" };
        else if (spec.StartsWith('R') && spec.IndexOf('A') is > 1 and var a
                 && double.TryParse(spec[1..a], System.Globalization.NumberStyles.Float, inv, out var r) && r is >= 50 and <= 20000
                 && double.TryParse(spec[(a + 1)..], System.Globalization.NumberStyles.Float, inv, out var ang) && ang is > 0 and <= 180)
            def = new TrackPieceDef { Number = "Flex", Kind = PieceKind.Curve, Radius = r, Angle = ang, Name = $"Flexgleis Bogen {Fmt.Num(ang)}°", Spec = $"R {Fmt.Num(r)} mm", Category = "Flexgleis" };
        if (def is null) return null;
        def.GaugeId = parts[0];
        def.SystemId = FlexSystemId;
        def.SystemName = "Flexgleis";
        def.Verified = true;
        def.Key = key;
        return def;
    }

    public static TrackSystem? GetSystem(string gaugeId, string systemId) => GetGauge(gaugeId).Systems.FirstOrDefault(s => s.Id == systemId);
    public static Gauge GetGauge(string id) => Gauges.FirstOrDefault(g => g.Id == id) ?? Gauges.First(g => g.Id == "H0");
}
