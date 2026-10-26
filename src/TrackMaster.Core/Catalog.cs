namespace TrackMaster.Core;

public enum PieceKind { Straight, Curve, Switch, Crossing, Buffer }

/// <summary>Teilstück eines Weichenstrangs: gerade (Turn = 0) oder Bogen (Turn = +1 rechts, −1 links).</summary>
public readonly record struct Segment(double Length, double Radius = 0, double Angle = 0, int Turn = 0)
{
    public static Segment Line(double length) => new(length);
    public static Segment Arc(double radius, double angle, bool right) => new(0, radius, angle, right ? 1 : -1);
}

/// <summary>Fahrweg eines Gleisstücks: Anfangspunkt und -richtung in eigenen Koordinaten, dann die Abschnitte.</summary>
public sealed record Route(double X, double Y, double Heading, IReadOnlyList<Segment> Segments)
{
    public double Length => Segments.Sum(s => s.Turn == 0 ? s.Length : s.Radius * s.Angle * Math.PI / 180);
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
    /// <summary>
    /// Fahrwege mit eigenem Anfangspunkt (aus Herstellerdaten). Weichen: alle beginnen an Anschluss 0, Fahrweg 0 ist der Stammstrang.
    /// Kreuzungen: zwei durchgehende Fahrwege. Hat Vorrang vor <see cref="Paths"/>.
    /// </summary>
    public IReadOnlyList<Route>? Routes { get; init; }
    /// <summary>Nur gezeichnete Stränge, z. B. die Bogenstränge einer Kreuzungsweiche.</summary>
    public IReadOnlyList<Route>? Decor { get; init; }
    /// <summary>Anzahl Antriebe, wenn sie sich nicht aus der Form ergibt (Kreuzungsweichen, Gleisverbindungen).</summary>
    public int? DriveCount { get; init; }
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
        : Routes is { } r ? r[0].Length
        : Paths is { } p ? p[0].Sum(s => s.Turn == 0 ? s.Length : s.Radius * s.Angle * Math.PI / 180) : Length;
    public bool CanFlip => Kind is PieceKind.Curve or PieceKind.Switch or PieceKind.Crossing;
    /// <summary>Flexgleis mit frei gewählter Länge bzw. Radius und Winkel.</summary>
    public bool IsFlex => Catalog.IsFlexSystem(SystemId);
    /// <summary>Einfache Weiche aus Gerade und Bogen (für Schnellwahl und Eigenschaften).</summary>
    public bool IsSimpleSwitch => Kind == PieceKind.Switch && (Routes is { } r
        ? r.Count == 2 && r[0].Segments is [{ Turn: 0 }] && r[1].Segments is [{ Turn: not 0 }, ..]
        : Paths is null);
    /// <summary>Anzahl Fahrwege (Stränge).</summary>
    public int RouteCount => Routes?.Count ?? Paths?.Count ?? (Kind is PieceKind.Switch or PieceKind.Crossing ? 2 : 1);
    /// <summary>Doppelte Kreuzungsweiche: zwei Antriebe.</summary>
    public bool IsDoubleSlip => Kind == PieceKind.Crossing && Name.StartsWith("DKW");
    /// <summary>Anzahl Weichenantriebe (Dreiwegweiche und DKW zwei).</summary>
    public int Drives => DriveCount ?? (Kind == PieceKind.Switch ? Math.Max(1, RouteCount - 1) : IsDoubleSlip ? 2 : 0);
}

public sealed class TrackSystem
{
    public required string Id { get; init; }
    private readonly string _name = "";
    public required string Name { get => NeutralName ?? _name; init => _name = value; }
    /// <summary>Neutrale Bezeichnung für Bildschirmfotos (siehe <see cref="Catalog.UseNeutralNames"/>).</summary>
    internal string? NeutralName { get; set; }
    /// <summary>true = Maße und Artikelnummern aus dem Herstellerkatalog geprüft.</summary>
    public bool Verified { get; init; }
    /// <summary>Mittelleiter (Märklin H0): Kehrschleifen brauchen keine Sonderschaltung.</summary>
    public bool ThreeRail { get; init; }
    /// <summary>Flexgleis des Herstellers: Artikelnummer (falls bekannt) und Länge eines Stücks. Ohne Angabe gilt das allgemeine Flexgleis der Spur.</summary>
    public string? FlexNumber { get; init; }
    public double? FlexLength { get; init; }
    /// <summary>Weitere Flexgleise des Herstellers (z. B. halbstarre Ausführung): Artikelnummer, Länge eines Stücks, Zusatz zum Namen.</summary>
    public IReadOnlyList<(string Number, double Length, string Label)> MoreFlex { get; init; } = [];
    /// <summary>Herkunft der Maße (Datei der XTrackCAD-Bibliothek), leer bei von Hand gepflegten Systemen.</summary>
    public string? Source { get; init; }
    public required IReadOnlyList<TrackPieceDef> Pieces { get; init; }
    /// <summary>Frühere Katalogeinträge, die nur noch zum Öffnen älterer Pläne dienen und in der Bibliothek nicht erscheinen.</summary>
    public IReadOnlyList<TrackPieceDef> Legacy { get; init; } = [];
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
/// Gleiskatalog aller Spuren. Die Systeme mit <see cref="TrackSystem.Source"/> sind aus der XTrackCAD-Gleisbibliothek
/// erzeugt (CatalogData.cs, Maße und Artikelnummern der Hersteller); von Hand gepflegte Systeme sind vorläufig (Verified = false).
/// </summary>
public static partial class Catalog
{
    private static TrackPieceDef S(string nr, double len, string name = "Gerade") => new()
        { Number = nr, Kind = PieceKind.Straight, Length = len, Name = name, Spec = $"{Fmt.Num(len)} mm", Category = name == "Gerade" ? "Gerade" : "Sonstige" };

    private static TrackPieceDef C(string nr, double r, double a, string? radiusName = null, string name = "Bogen") => new()
        { Number = nr, Kind = PieceKind.Curve, Radius = r, Angle = a, Name = $"{name} {(radiusName is null ? "" : radiusName + " · ")}{Fmt.Num(a)}°", Spec = $"R {Fmt.Num(r)} mm",
          Category = name == "Bogen" ? "Bögen" : "Sonstige" };

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

    /// <summary>Gleisstück mit Fahrwegen aus Herstellerdaten (erzeugter Katalog).</summary>
    private static TrackPieceDef G(string nr, PieceKind kind, string name, string spec, string category, bool right, int drives, Route[] routes, Route[]? decor = null)
    {
        var arc = routes.Skip(kind == PieceKind.Switch ? 1 : 0).SelectMany(r => r.Segments).FirstOrDefault(x => x.Turn != 0);
        var angle = kind == PieceKind.Crossing && routes.Length > 1 ? Math.Abs(Angles.Normalize(routes[1].Heading - routes[0].Heading)) : arc.Angle;
        return new()
        {
            Number = nr, Kind = kind, Name = name, Spec = spec, Category = category, RightHand = right, Routes = routes, Decor = decor,
            DriveCount = kind == PieceKind.Crossing ? drives : null,
            Length = routes[0].Length, Radius = arc.Radius, Angle = angle > 90 ? 180 - angle : angle,
        };
    }

    private static Route Rt(double x, double y, double heading, params Segment[] segments) => new(x, y, heading, segments);
    private static Segment Ln(double length) => Segment.Line(length);
    private static Segment Ar(double radius, double angle, int turn) => new(0, radius, angle, turn);

    private static TrackPieceDef P(string nr, double len, bool uncheckedGeometry = false) => new()
        { Number = nr, Kind = PieceKind.Buffer, Length = len, Unchecked = uncheckedGeometry, Name = uncheckedGeometry ? "Prellbock (allgemein)" : "Prellbock", Spec = $"{Fmt.Num(len)} mm", Category = "Sonstige" };

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

    public static IReadOnlyList<Gauge> Gauges { get; } = BuildGauges();

    private static readonly Dictionary<string, TrackPieceDef> ByKey = Index();

    private static Dictionary<string, TrackPieceDef> Index()
    {
        var map = new Dictionary<string, TrackPieceDef>();
        foreach (var gauge in Gauges)
            foreach (var system in gauge.Systems)
                foreach (var piece in system.Pieces.Concat(system.Legacy))
                {
                    var key = $"{gauge.Id}|{system.Id}|{piece.Number}";
                    var legacy = !system.Pieces.Contains(piece);
                    if (legacy && map.ContainsKey(key)) continue;
                    piece.GaugeId = gauge.Id;
                    piece.SystemId = system.Id;
                    piece.SystemName = system.Name;
                    piece.Verified = system.Verified && !piece.Unchecked && !legacy;
                    piece.Key = key;
                    map[key] = piece;
                }
        return map;
    }

    /// <summary>
    /// Ersetzt die Namen der Gleissysteme durch neutrale Bezeichnungen („Gleissystem A“, „B“ …). Nur für Bildschirmfotos
    /// im Handbuch, das keine Herstellernamen nennt; wirkt bis zum Programmende.
    /// </summary>
    public static void UseNeutralNames()
    {
        foreach (var gauge in Gauges)
            for (var i = 0; i < gauge.Systems.Count; i++)
            {
                var system = gauge.Systems[i];
                system.NeutralName = $"Gleissystem {(char)('A' + i % 26)}{(i >= 26 ? (i / 26).ToString() : "")}";
                foreach (var piece in system.Pieces.Concat(system.Legacy)) piece.SystemName = system.Name;
            }
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

    /// <summary>Alle Flexgleise eines Systems: das Standard-Flexgleis und weitere Ausführungen („flex-&lt;System&gt;~&lt;Artikel&gt;“).</summary>
    public static IReadOnlyList<FlexStock> Flexes(Gauge gauge, TrackSystem? system)
        => [Flex(gauge, system), .. (system?.MoreFlex ?? []).Select(m => new FlexStock($"{FlexSystemId}-{system!.Id}~{m.Number}", m.Number, m.Length, $"{system.Name}, {m.Label}"))];

    /// <summary>Flexgleis zu einem Systemschlüssel aus der Plandatei („flex“, „flex-&lt;System&gt;“ oder „flex-&lt;System&gt;~&lt;Artikel&gt;“).</summary>
    public static FlexStock? FlexOf(string gaugeId, string flexSystemId)
    {
        var gauge = Gauges.FirstOrDefault(g => g.Id == gaugeId);
        if (gauge is null) return null;
        if (flexSystemId == FlexSystemId) return Flex(gauge, null);
        if (!flexSystemId.StartsWith(FlexSystemId + "-", StringComparison.Ordinal)) return null;
        var rest = flexSystemId[(FlexSystemId.Length + 1)..];
        var tilde = rest.IndexOf('~');
        var system = gauge.Systems.FirstOrDefault(s => s.Id == (tilde < 0 ? rest : rest[..tilde]));
        if (system is null) return null;
        if (tilde >= 0) return Flexes(gauge, system).FirstOrDefault(f => f.SystemId == flexSystemId);
        return system.FlexLength is null ? null : Flex(gauge, system);
    }

    /// <summary>Schlüssel für ein gerades Flexgleis, z. B. „H0|flex|L500“ oder „H0|flex-mk|L500“.</summary>
    public static string FlexStraightKey(string gaugeId, double length, string flexSystemId = FlexSystemId)
        => $"{gaugeId}|{flexSystemId}|L{Math.Round(length, 2).ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>Schlüssel für ein gebogenes Flexgleis (Linksbogen, beim Verlegen spiegelbar), z. B. „H0|flex|R600A30“.</summary>
    public static string FlexCurveKey(string gaugeId, double radius, double angle, string flexSystemId = FlexSystemId)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return $"{gaugeId}|{flexSystemId}|R{Math.Round(radius, 2).ToString(inv)}A{Math.Round(angle, 3).ToString(inv)}";
    }

    private static TrackPieceDef? ParseFlex(string key)
    {
        var parts = key.Split('|');
        if (parts.Length != 3 || FlexOf(parts[0], parts[1]) is not { } stock) return null;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var spec = parts[2];
        TrackPieceDef? def = null;
        if (spec.StartsWith('L') && double.TryParse(spec[1..], System.Globalization.NumberStyles.Float, inv, out var len) && len is >= 5 and <= 20000)
            def = new TrackPieceDef { Number = stock.Number, Kind = PieceKind.Straight, Length = len, Name = "Flexgleis gerade", Spec = $"{Fmt.Num(len)} mm", Category = "Flexgleis" };
        else if (spec.StartsWith('R') && spec.IndexOf('A') is > 1 and var a
                 && double.TryParse(spec[1..a], System.Globalization.NumberStyles.Float, inv, out var r) && r is >= 10 and <= 1_000_000
                 && double.TryParse(spec[(a + 1)..], System.Globalization.NumberStyles.Float, inv, out var ang) && ang is > 0 and < 360
                 && r * ang * Math.PI / 180 <= FlexTrack.MaxLength)
            def = new TrackPieceDef { Number = stock.Number, Kind = PieceKind.Curve, Radius = r, Angle = ang, Name = $"Flexgleis Bogen {Fmt.Num(ang)}°", Spec = $"R {Fmt.Num(r)} mm", Category = "Flexgleis" };
        else if (spec.StartsWith('P') && FlexTrack.ParsePath(spec[1..]) is { } segments)
        {
            var total = FlexTrack.Length(segments);
            def = new TrackPieceDef
            {
                Number = stock.Number, Kind = PieceKind.Straight, Length = total, Routes = [new Route(0, 0, 0, segments)],
                Name = segments.All(x => x.Turn == 0) ? "Flexgleis gerade" : "Flexgleis geformt",
                Spec = $"{Fmt.Num(total)} mm" + (FlexTrack.SmallestRadius(segments) is { } minR ? $" · R ≥ {Fmt.Num(minR, 0)} mm" : ""),
                Radius = FlexTrack.SmallestRadius(segments) ?? 0, Category = "Flexgleis",
            };
        }
        if (def is null) return null;
        def.GaugeId = parts[0];
        def.SystemId = parts[1];
        def.SystemName = parts[1] == FlexSystemId ? "Flexgleis" : $"Flexgleis {stock.Name}";
        def.Verified = parts[1] == FlexSystemId || GetSystem(parts[0], parts[1][(FlexSystemId.Length + 1)..].Split('~')[0])?.Verified == true;
        def.Key = key;
        return def;
    }

    public static TrackSystem? GetSystem(string gaugeId, string systemId) => GetGauge(gaugeId).Systems.FirstOrDefault(s => s.Id == systemId);
    public static Gauge GetGauge(string id) => Gauges.FirstOrDefault(g => g.Id == id) ?? Gauges.First(g => g.Id == "H0");
}
