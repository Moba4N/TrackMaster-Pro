namespace TrackMaster.Core;

public enum PieceKind { Straight, Curve, Switch, Crossing, Buffer, Turntable, TransferTable }

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
    /// <summary>
    /// Drehscheibe und Schiebebühne: Gleisanschlüsse in eigenen Koordinaten (Richtung aus dem Teil heraus).
    /// Von jedem Anschluss führt ein kurzes Gleisstück (<see cref="Length"/>) zur Bühne.
    /// </summary>
    public IReadOnlyList<Pose>? Ports { get; init; }
    /// <summary>Drehscheibe: Mittelpunkt, Radius der Grube (= halbe Bühnenlänge) und Außenradius des Rings.</summary>
    public (double X, double Y) Center { get; init; }
    public double PitRadius { get; init; }
    public double RimRadius { get; init; }
    /// <summary>Schiebebühne: Grube als Rechteck in eigenen Koordinaten.</summary>
    public (double X0, double Y0, double X1, double Y1) PitBox { get; init; }
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
    /// <summary>Drehscheibe oder Schiebebühne: viele Anschlüsse, von denen meist nur einige belegt sind.</summary>
    public bool IsTable => Kind is PieceKind.Turntable or PieceKind.TransferTable;
    /// <summary>Flexgleis mit frei gewählter Länge bzw. Radius und Winkel.</summary>
    public bool IsFlex => Catalog.IsFlexSystem(SystemId);
    /// <summary>Einfache Weiche aus Gerade und Bogen (für Schnellwahl und Eigenschaften).</summary>
    public bool IsSimpleSwitch => Kind == PieceKind.Switch && (Routes is { } r
        ? r.Count == 2 && r[0].Segments is [{ Turn: 0 }] && r[1].Segments is [{ Turn: not 0 }, ..]
        : Paths is null);
    /// <summary>Anzahl Fahrwege (Stränge).</summary>
    public int RouteCount => Ports?.Count ?? Routes?.Count ?? Paths?.Count ?? (Kind is PieceKind.Switch or PieceKind.Crossing ? 2 : 1);
    /// <summary>Doppelte Kreuzungsweiche: zwei Antriebe.</summary>
    public bool IsDoubleSlip => Kind == PieceKind.Crossing && (Name.StartsWith("DKW") || Name.StartsWith("Double slip"));
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
/// Gleiskatalog aller Spuren, gelesen aus der Datendatei (siehe CatalogFile.cs). Die Systeme mit <see cref="TrackSystem.Source"/>
/// stammen aus der XTrackCAD-Gleisbibliothek (Maße und Artikelnummern der Hersteller); von Hand gepflegte Systeme sind vorläufig (Verified = false).
/// </summary>
public static partial class Catalog
{
    private static IReadOnlyList<Gauge>? _gauges;
    private static Dictionary<string, TrackPieceDef>? _byKey;
    private static readonly object LoadLock = new();

    /// <summary>Alle Spuren mit ihren Gleissystemen; beim ersten Zugriff aus der Datendatei gelesen.</summary>
    public static IReadOnlyList<Gauge> Gauges { get { EnsureLoaded(); return _gauges!; } }
    private static Dictionary<string, TrackPieceDef> ByKey { get { EnsureLoaded(); return _byKey!; } }

    private static void EnsureLoaded()
    {
        if (_byKey is not null) return;
        lock (LoadLock)
        {
            if (_byKey is not null) return;
            _gauges = Load();
            _byKey = Index();
        }
    }

    private static Dictionary<string, TrackPieceDef> Index()
    {
        var map = new Dictionary<string, TrackPieceDef>();
        foreach (var gauge in _gauges!)
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
                system.NeutralName = Lang.F($"Gleissystem {(char)('A' + i % 26)}{(i >= 26 ? (i / 26).ToString() : "")}");
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
    public static TrackPieceDef Get(string key) => Find(key) ?? throw new KeyNotFoundException(Lang.F($"Unbekanntes Gleis „{key}“."));

    public static bool IsFlexSystem(string systemId) => systemId == FlexSystemId || systemId.StartsWith(FlexSystemId + "-", StringComparison.Ordinal);

    /// <summary>
    /// Flexgleis für ein Gleissystem: das des Herstellers (Systemschlüssel „flex-&lt;System&gt;“), sonst das allgemeine der Spur („flex“).
    /// </summary>
    public static FlexStock Flex(Gauge gauge, TrackSystem? system)
        => system is { FlexLength: { } len }
            ? new FlexStock($"{FlexSystemId}-{system.Id}", system.FlexNumber ?? Lang.T("Flexgleis"), len, system.Name)
            : new FlexStock(FlexSystemId, Lang.T("Flexgleis"), gauge.FlexStockLength, Lang.F($"Spur {gauge.Id} allgemein"));

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
            def = new TrackPieceDef { Number = stock.Number, Kind = PieceKind.Straight, Length = len, Name = Lang.T("Flexgleis gerade"), Spec = $"{Fmt.Num(len)} mm", Category = Lang.T("Flexgleis") };
        else if (spec.StartsWith('R') && spec.IndexOf('A') is > 1 and var a
                 && double.TryParse(spec[1..a], System.Globalization.NumberStyles.Float, inv, out var r) && r is >= 10 and <= 1_000_000
                 && double.TryParse(spec[(a + 1)..], System.Globalization.NumberStyles.Float, inv, out var ang) && ang is > 0 and < 360
                 && r * ang * Math.PI / 180 <= FlexTrack.MaxLength)
            def = new TrackPieceDef { Number = stock.Number, Kind = PieceKind.Curve, Radius = r, Angle = ang, Name = Lang.F($"Flexgleis Bogen {Fmt.Num(ang)}°"), Spec = $"R {Fmt.Num(r)} mm", Category = Lang.T("Flexgleis") };
        else if (spec.StartsWith('P') && FlexTrack.ParsePath(spec[1..]) is { } segments)
        {
            var total = FlexTrack.Length(segments);
            def = new TrackPieceDef
            {
                Number = stock.Number, Kind = PieceKind.Straight, Length = total, Routes = [new Route(0, 0, 0, segments)],
                Name = segments.All(x => x.Turn == 0) ? Lang.T("Flexgleis gerade") : Lang.T("Flexgleis geformt"),
                Spec = $"{Fmt.Num(total)} mm" + (FlexTrack.SmallestRadius(segments) is { } minR ? $" · R ≥ {Fmt.Num(minR, 0)} mm" : ""),
                Radius = FlexTrack.SmallestRadius(segments) ?? 0, Category = Lang.T("Flexgleis"),
            };
        }
        if (def is null) return null;
        def.GaugeId = parts[0];
        def.SystemId = parts[1];
        def.SystemName = parts[1] == FlexSystemId ? Lang.T("Flexgleis") : Lang.F($"Flexgleis {stock.Name}");
        def.Verified = parts[1] == FlexSystemId || GetSystem(parts[0], parts[1][(FlexSystemId.Length + 1)..].Split('~')[0])?.Verified == true;
        def.Key = key;
        return def;
    }

    public static TrackSystem? GetSystem(string gaugeId, string systemId) => GetGauge(gaugeId).Systems.FirstOrDefault(s => s.Id == systemId);
    public static Gauge GetGauge(string id) => Gauges.FirstOrDefault(g => g.Id == id) ?? Gauges.First(g => g.Id == "H0");
}
