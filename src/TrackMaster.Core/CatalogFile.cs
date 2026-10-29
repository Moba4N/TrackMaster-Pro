using System.Text.Json;
using System.Text.RegularExpressions;

namespace TrackMaster.Core;

/// <summary>
/// Liest den Gleiskatalog. Der eigene Katalog (Gleiskatalog.json, im Programm eingebettet) ist nach Herstellerangaben
/// erfasst (Erzeuger: tools/katalog). Systeme, die dort noch fehlen, kommen aus der Datendatei gleisdaten.json
/// (Ordner Gleisdaten neben dem Programm, eigene Datei unter der GPL, wird nur gelesen), solange es sie gibt.
/// Texte stehen auf Deutsch in den Dateien; englische Namen und Daten stehen dort, wo die Übersetzung nicht aus Lang.en.json folgt.
/// </summary>
public static partial class Catalog
{
    /// <summary>
    /// Öffnet die GPL-Datendatei. Unter Windows liegt sie im Programmordner; ohne Angabe gilt <see cref="DataPath"/>.
    /// Android setzt hier das App-Asset ein, bevor der Katalog zum ersten Mal gebraucht wird.
    /// </summary>
    public static Func<Stream>? OpenData { get; set; }

    /// <summary>Hinweis für „Über das Programm“: Herkunft und Lizenz der Gleisdaten.</summary>
    public static string DataNotice => Lang.T("Gleisdaten nach Herstellerangaben. Noch nicht neu erfasste Systeme stammen aus einer eigenen Datei unter der GNU GPL Version 2 (Ordner Gleisdaten, mit Lizenztext; Maße aus der Gleisbibliothek von XTrackCAD).");

    public static string DataPath => Path.Combine(AppContext.BaseDirectory, "Gleisdaten", "gleisdaten.json");

    private static IReadOnlyList<Gauge> Load()
    {
        using var own = typeof(Catalog).Assembly.GetManifestResourceStream("TrackMaster.Core.Gleiskatalog.json")!;
        var gauges = Read(own);
        Stream? extra;
        try { extra = OpenData?.Invoke() ?? (File.Exists(DataPath) ? File.OpenRead(DataPath) : null); }
        catch (IOException) { extra = null; }
        if (extra is null) return gauges;
        using (extra) return Merge(gauges, Read(extra));
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

    /// <summary>
    /// Eigene Systeme ersetzen die gleichnamigen der GPL-Datei (an deren Stelle in der Reihenfolge); übrige GPL-Systeme bleiben.
    /// Teile der GPL-Datei, die im eigenen System fehlen, bleiben im Hintergrund, damit ältere Pläne weiter öffnen.
    /// </summary>
    private static List<Gauge> Merge(List<Gauge> own, List<Gauge> gpl) => own.Select(g =>
    {
        var other = gpl.FirstOrDefault(x => x.Id == g.Id)?.Systems ?? [];
        var systems = other.Select(o => g.Systems.FirstOrDefault(s => s.Id == o.Id) is { } s ? WithLegacy(s, o) : o)
            .Concat(g.Systems.Where(s => other.All(o => o.Id != s.Id))).ToList();
        return new Gauge
        {
            Id = g.Id, Scale = g.Scale, TrackWidth = g.TrackWidth, DefaultBoardWidth = g.DefaultBoardWidth, DefaultBoardHeight = g.DefaultBoardHeight,
            ClearanceHeight = g.ClearanceHeight, FlexStockLength = g.FlexStockLength, Systems = systems,
        };
    }).ToList();

    private static TrackSystem WithLegacy(TrackSystem s, TrackSystem old)
    {
        var known = s.Pieces.Concat(s.Legacy).Select(p => p.Number).ToHashSet();
        var legacy = old.Pieces.Concat(old.Legacy).Where(p => known.Add(p.Number)).ToList();
        // Drehscheiben und Schiebebühnen ohne eigenen Eintrag bleiben in der Bibliothek sichtbar.
        var tables = old.Pieces.Where(p => p.IsTable && legacy.Contains(p)).ToList();
        return new TrackSystem
        {
            Id = s.Id, Name = s.Name, Verified = s.Verified, ThreeRail = s.ThreeRail, FlexNumber = s.FlexNumber, FlexLength = s.FlexLength,
            MoreFlex = s.MoreFlex, Source = s.Source, Pieces = [.. s.Pieces, .. tables], Legacy = [.. s.Legacy, .. legacy.Except(tables)], Aliases = s.Aliases,
        };
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
