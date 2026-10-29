using System.Text.Json;
using System.Text.RegularExpressions;

namespace TrackMaster.Core;

/// <summary>
/// Liest den Gleiskatalog (Gleiskatalog.json, im Programm eingebettet), erfasst nach Herstellerangaben (Erzeuger: tools/katalog).
/// Texte stehen auf Deutsch in der Datei; englische Namen und Daten stehen dort, wo die Übersetzung nicht aus Lang.en.json folgt.
/// </summary>
public static partial class Catalog
{
    /// <summary>Hinweis für „Über das Programm“: Herkunft der Gleisdaten.</summary>
    public static string DataNotice => Lang.T("Gleisdaten nach Angaben der Hersteller (Kataloge, Produktblätter, Anleitungen).");

    private static IReadOnlyList<Gauge> Load()
    {
        using var own = typeof(Catalog).Assembly.GetManifestResourceStream("TrackMaster.Core.Gleiskatalog.json")!;
        return Read(own);
    }

    private static List<Gauge> Read(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        if (root.GetProperty("format").GetString() != "gleisdaten-1")
            throw new InvalidDataException(Lang.T("Unbekanntes Format der Gleisdaten."));
        return root.GetProperty("gauges").EnumerateArray().Select(g => new Gauge
        {
            Id = Str(g, "id"),
            Scale = Str(g, "scale"),
            TrackWidth = Num(g, "trackWidth"),
            DefaultBoardWidth = Num(g, "boardWidth"),
            DefaultBoardHeight = Num(g, "boardHeight"),
            ClearanceHeight = Num(g, "clearance"),
            FlexStockLength = Num(g, "flexStock"),
            Systems = g.GetProperty("systems").EnumerateArray().Select(ReadSystem).ToList(),
        }).ToList();
    }

    private static TrackSystem ReadSystem(JsonElement s) => new()
    {
        Id = Str(s, "id"),
        Name = Lang.T(Str(s, "name")),
        Verified = Flag(s, "verified"),
        ThreeRail = Flag(s, "threeRail"),
        FlexNumber = Opt(s, "flexNumber"),
        FlexLength = s.TryGetProperty("flexLength", out var fl) ? fl.GetDouble() : null,
        MoreFlex = List(s, "moreFlex", m => (Str(m, "nr"), Num(m, "len"), Lang.T(Str(m, "label")))),
        Source = Opt(s, "source") is { } src ? Lang.T(src) : null,
        Pieces = List(s, "pieces", ReadPiece),
        Legacy = List(s, "legacy", ReadPiece),
        Aliases = s.TryGetProperty("aliases", out var al) ? al.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetString() ?? "") : new Dictionary<string, string>(),
    };

    private static TrackPieceDef ReadPiece(JsonElement p) => new()
    {
        Number = Str(p, "nr"),
        Kind = Enum.Parse<PieceKind>(Str(p, "kind")),
        Name = Text(p, "name", Lang.T),
        Spec = Text(p, "spec", EnglishNumbers),
        Category = Text(p, "cat", Lang.T),
        Length = Num(p, "len"),
        Radius = Num(p, "r"),
        Angle = Num(p, "a"),
        RightHand = Flag(p, "right"),
        Paths = p.TryGetProperty("paths", out var ps) ? ps.EnumerateArray().Select(Segments).ToList() : null,
        Routes = p.TryGetProperty("routes", out var rs) ? Routes(rs) : null,
        Decor = p.TryGetProperty("decor", out var ds) ? Routes(ds) : null,
        DriveCount = p.TryGetProperty("drives", out var d) ? d.GetInt32() : null,
        Unchecked = Flag(p, "unchecked"),
        Ports = p.TryGetProperty("ports", out var po) ? po.EnumerateArray().Select(x => new Pose(x[0].GetDouble(), x[1].GetDouble(), x[2].GetDouble())).ToList() : null,
        Center = p.TryGetProperty("center", out var c) ? (c[0].GetDouble(), c[1].GetDouble()) : default,
        PitRadius = Num(p, "pit"),
        RimRadius = Num(p, "rim"),
        PitBox = p.TryGetProperty("pitBox", out var b) ? (b[0].GetDouble(), b[1].GetDouble(), b[2].GetDouble(), b[3].GetDouble()) : default,
    };

    private static List<Route> Routes(JsonElement a)
        => a.EnumerateArray().Select(r => new Route(Num(r, "x"), Num(r, "y"), Num(r, "h"), Segments(r.GetProperty("s")))).ToList();

    private static List<Segment> Segments(JsonElement a)
        => a.EnumerateArray().Select(s => new Segment(s[0].GetDouble(), s[1].GetDouble(), s[2].GetDouble(), s[3].GetInt32())).ToList();

    /// <summary>Deutscher Text aus der Datei; auf Englisch der eigene englische Eintrag (Feld + „En“) oder die Umsetzung.</summary>
    private static string Text(JsonElement e, string name, Func<string, string> english)
    {
        var german = Str(e, name);
        if (!Lang.IsEnglish) return german;
        return Opt(e, name + "En") ?? english(german);
    }

    /// <summary>Zahlen in englischer Schreibweise: 1.234,5 wird 1,234.5.</summary>
    private static string EnglishNumbers(string german)
        => Regex.Replace(german, @"\d{1,3}(?:\.\d{3})+(?:,\d+)?|\d+,\d+", m => string.Concat(m.Value.Select(ch => ch switch { '.' => ',', ',' => '.', _ => ch })));

    private static string Str(JsonElement e, string name) => e.GetProperty(name).GetString() ?? "";
    private static string? Opt(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetString() : null;
    private static double Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetDouble() : 0;
    private static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.GetBoolean();
    private static List<T> List<T>(JsonElement e, string name, Func<JsonElement, T> read)
        => e.TryGetProperty(name, out var v) ? v.EnumerateArray().Select(read).ToList() : [];
}
