using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrackMaster.Core;

/// <summary>
/// Dateiformat *.mpr (Inhalt JSON) – dasselbe in Windows, Android und Browser, Dateien lassen sich hin und her öffnen.
/// Ältere Pläne *.trackmaster.json (gleicher Inhalt) werden weiter geöffnet.
/// </summary>
public static partial class PlanSerializer
{
    /// <summary>Endung, unter der alle Versionen Pläne speichern.</summary>
    public const string FileExtension = ".mpr";
    /// <summary>Frühere Endung der Windows- und Android-Version; wird weiter geöffnet, aber nicht mehr geschrieben.</summary>
    public const string LegacyExtension = ".trackmaster.json";

    /// <summary>Hat der Pfad die Endung *.mpr?</summary>
    public static bool IsPlanFile(string path) => path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Dateiname ohne Plan-Endung (*.mpr, *.trackmaster.json, *.json).</summary>
    public static string BaseName(string fileName)
    {
        foreach (var ext in new[] { FileExtension, LegacyExtension, ".json" })
            if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return fileName[..^ext.Length];
        return fileName;
    }

    /// <summary>Pfad mit der Endung *.mpr: ältere Endungen werden ersetzt, fehlende ergänzt.</summary>
    public static string WithPlanExtension(string path) => IsPlanFile(path) ? path : BaseName(path) + FileExtension;
    /// <summary>
    /// Version des Dateiformats. 2: Beschriftungen, Maßlinien, Drehscheiben und Schiebebühnen. 3: Raum, Hintergrundbild,
    /// Rückmelder, Preise und Bestand, gesperrte und gruppierte Gleise. 4: Ausstattungsmodelle (Gebäude mit Modell und Farbe,
    /// entlang des Gleises gebogene Bahnsteige, mitgespeicherte eigene Modelle). 5: Stromart, Polung der Einspeisungen,
    /// selbst verlegte Kabel. Ältere Programmversionen
    /// überspringen, was sie nicht kennen; neuere Dateien öffnet dieses Programm mit einem Hinweis.
    /// </summary>
    public const int CurrentFormat = 5;

    private sealed class FileDto
    {
        [JsonPropertyName("app")] public string App { get; set; } = "TrackMaster Pro";
        [JsonPropertyName("format")] public int Format { get; set; } = CurrentFormat;
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("gauge")] public string? Gauge { get; set; }
        [JsonPropertyName("system"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? System { get; set; }
        [JsonPropertyName("board")] public BoardDto? Board { get; set; }
        [JsonPropertyName("vehicle"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Vehicle { get; set; }
        [JsonPropertyName("pieces")] public List<PieceDto>? Pieces { get; set; }
        [JsonPropertyName("scenery"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<SceneryDto>? Scenery { get; set; }
        [JsonPropertyName("feeders"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<FeederDto>? Feeders { get; set; }
        [JsonPropertyName("isolations"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<IsolationDto>? Isolations { get; set; }
        [JsonPropertyName("signals"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<SignalDto>? Signals { get; set; }
        [JsonPropertyName("levels"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<LevelDto>? Levels { get; set; }
        [JsonPropertyName("activeLevel"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int ActiveLevel { get; set; }
        [JsonPropertyName("benchwork"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public BenchworkSpec? Benchwork { get; set; }
        [JsonPropertyName("frame"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FrameDto? Frame { get; set; }
        [JsonPropertyName("detectors"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<DetectorDto>? Detectors { get; set; }
        [JsonPropertyName("train"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Train { get; set; }
        [JsonPropertyName("prices"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, double>? Prices { get; set; }
        [JsonPropertyName("stock"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, int>? Stock { get; set; }
        [JsonPropertyName("background"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public BackgroundDto? Background { get; set; }
        [JsonPropertyName("models"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<ModelFile.ModelDto>? Models { get; set; }
        [JsonPropertyName("current"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Current { get; set; }
        [JsonPropertyName("operation"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Operation { get; set; }
        [JsonPropertyName("detWires"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? DetWires { get; set; }
    }

    private sealed class FrameDto
    {
        [JsonPropertyName("dir"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Dir { get; set; }
        [JsonPropertyName("spacing")] public double Spacing { get; set; }
        [JsonPropertyName("width")] public double Width { get; set; }
        [JsonPropertyName("thick")] public double Thick { get; set; }
        [JsonPropertyName("base")] public double Base { get; set; }
    }

    private sealed class DetectorDto
    {
        [JsonPropertyName("id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Id { get; set; }
        [JsonPropertyName("piece")] public int Piece { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("addr"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Addr { get; set; }
        [JsonPropertyName("src"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Src { get; set; }
        [JsonPropertyName("auto"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Auto { get; set; }
    }

    private sealed class BackgroundDto
    {
        [JsonPropertyName("mime")] public string? Mime { get; set; }
        [JsonPropertyName("data")] public string? Data { get; set; }
        [JsonPropertyName("pw")] public int PixelWidth { get; set; }
        [JsonPropertyName("ph")] public int PixelHeight { get; set; }
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
        [JsonPropertyName("w")] public double W { get; set; }
        [JsonPropertyName("opacity")] public double Opacity { get; set; } = 0.5;
        [JsonPropertyName("visible")] public bool Visible { get; set; } = true;
    }

    private sealed class LevelDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("h")] public double H { get; set; }
        [JsonPropertyName("visible")] public bool Visible { get; set; } = true;
    }

    private sealed class SceneryDto
    {
        [JsonPropertyName("id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Id { get; set; }
        [JsonPropertyName("kind")] public string Kind { get; set; } = "";
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("points"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<double[]>? Points { get; set; }
        [JsonPropertyName("tracks"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<int>? Tracks { get; set; }
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
        [JsonPropertyName("w")] public double W { get; set; }
        [JsonPropertyName("d")] public double D { get; set; }
        [JsonPropertyName("rot")] public double Rot { get; set; }
        [JsonPropertyName("h")] public double H { get; set; }
        [JsonPropertyName("level"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Level { get; set; }
        [JsonPropertyName("var"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Variant { get; set; }
        [JsonPropertyName("model"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Model { get; set; }
        [JsonPropertyName("color"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Color { get; set; }
        [JsonPropertyName("addr"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Addr { get; set; }
    }

    private sealed class FeederDto
    {
        [JsonPropertyName("id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Id { get; set; }
        [JsonPropertyName("piece")] public int Piece { get; set; }
        [JsonPropertyName("circuit")] public string? Circuit { get; set; }
        [JsonPropertyName("rev"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Rev { get; set; }
        [JsonPropertyName("gnd"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Gnd { get; set; }
        [JsonPropertyName("at"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? At { get; set; }
        [JsonPropertyName("wire"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Wire { get; set; }
        [JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Name { get; set; }
    }

    private sealed class IsolationDto
    {
        [JsonPropertyName("id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Id { get; set; }
        [JsonPropertyName("piece")] public int Piece { get; set; }
        [JsonPropertyName("end")] public int End { get; set; }
        [JsonPropertyName("auto"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Auto { get; set; }
    }

    private sealed class SignalDto
    {
        [JsonPropertyName("id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Id { get; set; }
        [JsonPropertyName("piece")] public int Piece { get; set; }
        [JsonPropertyName("end")] public int End { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("system"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? System { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("addr"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Addr { get; set; }
    }

    private sealed class BoardDto
    {
        [JsonPropertyName("w")] public double W { get; set; }
        [JsonPropertyName("h")] public double H { get; set; }
        [JsonPropertyName("shape"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Shape { get; set; }
        [JsonPropertyName("depth"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Depth { get; set; }
        [JsonPropertyName("turn"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Turn { get; set; }
        [JsonPropertyName("legL"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? LegLeft { get; set; }
        [JsonPropertyName("legR"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? LegRight { get; set; }
        [JsonPropertyName("modules"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<ModuleDto>? Modules { get; set; }
        /// <summary>Eigene Maße je Schenkel als Paare [Tiefe, Länge]; 0 = Standard.</summary>
        [JsonPropertyName("legs"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<double[]>? Legs { get; set; }
    }

    private sealed class ModuleDto
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
        [JsonPropertyName("w")] public double W { get; set; }
        [JsonPropertyName("d")] public double D { get; set; }
        /// <summary>Höhe der Moduloberfläche (z-Achse) in mm; fehlt bei 0.</summary>
        [JsonPropertyName("z"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double Z { get; set; }
        /// <summary>Plattenstärke in mm; fehlt bei 0 (Standard).</summary>
        [JsonPropertyName("t"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double T { get; set; }
    }

    private sealed class PieceDto
    {
        [JsonPropertyName("id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Id { get; set; }
        [JsonPropertyName("key")] public string? Key { get; set; }
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
        [JsonPropertyName("rot")] public double Rot { get; set; }
        [JsonPropertyName("flip")] public bool Flip { get; set; }
        [JsonPropertyName("z")] public double Z { get; set; }
        [JsonPropertyName("grade"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double Grade { get; set; }
        [JsonPropertyName("tunnel"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Tunnel { get; set; }
        [JsonPropertyName("addr"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Addr { get; set; }
        [JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Name { get; set; }
        [JsonPropertyName("level"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Level { get; set; }
        [JsonPropertyName("lock"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Lock { get; set; }
        [JsonPropertyName("group"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Group { get; set; }
    }

    /// <summary>
    /// Hintergrundbilder der Rückgängig-Schritte: Im Verlauf steht nur ein Verweis, damit nicht jeder Schritt das ganze Bild kopiert.
    /// </summary>
    private static readonly Dictionary<string, string> ImageCache = [];
    private const string ImageRef = "#ref:";

    private static string StoreImage(string data)
    {
        lock (ImageCache)
        {
            var key = $"{data.Length}:{(data.Length > 96 ? data.Substring(data.Length / 3, 48) + data[^24..] : data)}";
            if (ImageCache.Count > 16 && !ImageCache.ContainsKey(key)) ImageCache.Clear();
            ImageCache[key] = data;
            return ImageRef + key;
        }
    }

    private static string? LoadImage(string? data)
    {
        if (data is null || !data.StartsWith(ImageRef, StringComparison.Ordinal)) return data;
        lock (ImageCache) return ImageCache.GetValueOrDefault(data[ImageRef.Length..]);
    }

    /// <summary>
    /// Vorab erzeugter Serialisierer für das Dateiformat: kommt ohne Reflexion aus und ist vor allem in der Webversion
    /// (WebAssembly) deutlich schneller – jeder Rückgängig-Schritt speichert den ganzen Plan.
    /// </summary>
    [JsonSerializable(typeof(FileDto))]
    private sealed partial class FileJson : JsonSerializerContext;

    /// <summary>Dateien: eingerückt und gut lesbar.</summary>
    private static readonly FileJson Indented = new(new JsonSerializerOptions { WriteIndented = true });
    /// <summary>Rückgängig-Schritte, Zwischenablage, Wiederherstellung: kompakt (weniger Speicher, schneller).</summary>
    private static readonly FileJson Compact = new(new JsonSerializerOptions());

    /// <summary>
    /// Speichert den Plan. Gleise behalten in der Datei ihre Kennung, weil Einspeisungen, Trennstellen und Signale darauf verweisen;
    /// die Browser-Version liest die zusätzlichen Felder nicht und übernimmt nur die Gleise.
    /// </summary>
    /// <param name="withIds">Für Rückgängig-Schritte: auch Kennungen von Gelände und Elektrik behalten, damit die Auswahl erhalten bleibt.</param>
    public static string ToJson(LayoutPlan plan, bool withIds = false)
    {
        var needIds = withIds || plan.Feeders.Count > 0 || plan.Isolations.Count > 0 || plan.Signals.Count > 0 || plan.Detectors.Count > 0
            || plan.Scenery.Any(s => s.Tracks.Count > 0);
        var dto = new FileDto
        {
            Name = plan.Name,
            Gauge = plan.GaugeId,
            System = plan.SystemId,
            Board = new BoardDto
            {
                W = plan.BoardWidth, H = plan.BoardHeight,
                Shape = plan.BoardShape == BoardShape.Rechteck ? null : plan.BoardShape.ToString(),
                Depth = plan.BoardShape == BoardShape.Rechteck ? null : plan.BoardDepth,
                Turn = plan.BoardShape == BoardShape.Rechteck || plan.BoardTurn == 0 ? null : plan.BoardTurn,
                LegLeft = plan.BoardShape == BoardShape.U && plan.BoardLegLeft > 0 ? plan.BoardLegLeft : null,
                LegRight = plan.BoardShape == BoardShape.U && plan.BoardLegRight > 0 ? plan.BoardLegRight : null,
                Legs = BoardGeometry.HasLegs(plan.BoardShape) && plan.BoardLegs.Any(l => l.Depth > 0 || l.Length > 0)
                    ? plan.BoardLegs.Take(BoardGeometry.LegNames(plan.BoardShape).Count).Select(l => new[] { Math.Round(l.Depth, 1), Math.Round(l.Length, 1) }).ToList()
                    : null,
                Modules = plan.BoardShape == BoardShape.Module && plan.Modules.Count > 0
                    ? plan.Modules.Select(m => new ModuleDto { Name = m.Name, X = Math.Round(m.X, 3), Y = Math.Round(m.Y, 3), W = Math.Round(m.Width, 3), D = Math.Round(m.Depth, 3), Z = Math.Round(m.Height, 1), T = Math.Round(m.Thickness, 1) }).ToList()
                    : null,
            },
            Vehicle = plan.VehicleLength > 0 ? plan.VehicleLength : null,
            Pieces = plan.Pieces.Select(p => new PieceDto
            {
                Id = needIds ? p.Id : null,
                Key = p.Key,
                X = Math.Round(p.X, 3),
                Y = Math.Round(p.Y, 3),
                Rot = Math.Round(p.Rotation, 4),
                Flip = p.Flipped,
                Z = Math.Round(p.Height, 3),
                Grade = Math.Round(p.Grade, 6),
                Tunnel = p.Tunnel,
                Addr = p.Address,
                Name = string.IsNullOrEmpty(p.Name) ? null : p.Name,
                Level = p.Level,
                Lock = p.Locked,
                Group = p.Group,
            }).ToList(),
            Scenery = plan.Scenery.Count == 0 ? null : plan.Scenery.Select(s => new SceneryDto
            {
                // Kabel, aus denen Einspeisungen entstanden sind, behalten ihre Kennung
                Id = withIds || plan.Feeders.Any(f => f.WireId == s.Id) ? s.Id : null,
                Kind = s.Kind.ToString().ToLowerInvariant(),
                Name = s.Name,
                Points = s.IsArea || s.Kind is SceneryKind.Dimension or SceneryKind.Room or SceneryKind.Beam or SceneryKind.Wire || (s.Kind == SceneryKind.Building && s.Points.Count >= 2) || (s.Kind == SceneryKind.Plate && s.Points.Count >= 3) ? s.Points.Select(q => new[] { Math.Round(q.X, 2), Math.Round(q.Y, 2) }).ToList() : null,
                X = Math.Round(s.X, 2), Y = Math.Round(s.Y, 2), W = s.Width, D = s.Depth, Rot = s.Rotation, H = s.Height, Level = s.Level, Variant = s.Variant,
                Model = s.IsModel || s.Kind == SceneryKind.Power && !string.IsNullOrEmpty(s.Model) ? s.Model : null,
                Addr = s.Kind == SceneryKind.Power ? s.Address : null, Color = s.Kind is SceneryKind.Building or SceneryKind.Wire or SceneryKind.Power && ModelBody.ParseColor(s.Color) is not null ? s.Color : null,
                Tracks = (s.IsModel || DetectorWiring.IsGenerated(s) || AutoWiring.IsAuto(s)) && s.Tracks.Count > 0 ? [.. s.Tracks] : null,
            }).ToList(),
            Models = ModelFile.ForPlan(plan),
            Feeders = plan.Feeders.Count == 0 ? null : plan.Feeders.Select(f => new FeederDto { Id = withIds ? f.Id : null, Piece = f.PieceId, Circuit = f.Circuit, Rev = f.Reversed, Gnd = f.Ground, At = Math.Abs(f.At - 0.5) < 1e-9 ? null : Math.Round(f.At, 4), Wire = f.WireId, Name = f.Name.Length > 0 ? f.Name : null }).ToList(),
            Current = plan.Current == CurrentKind.Auto ? null : plan.Current == CurrentKind.Wechselstrom ? "ac" : "dc",
            Operation = plan.Operation == PowerMode.Analog ? "analog" : null,
            DetWires = plan.DetectorWiring ? true : null,
            Isolations = plan.Isolations.Count == 0 ? null : plan.Isolations.Select(i => new IsolationDto { Id = withIds ? i.Id : null, Piece = i.PieceId, End = i.Index, Auto = i.Auto ? true : null }).ToList(),
            Signals = plan.Signals.Count == 0 ? null : plan.Signals.Select(g => new SignalDto
                { Id = withIds ? g.Id : null, Piece = g.PieceId, End = g.Index, Type = g.Type.ToString(), System = g.System == SignalSystem.HV ? null : g.System.ToString(), Name = g.Name, Addr = g.Address }).ToList(),
            Levels = plan.Levels.Count == 1 && plan.Levels[0] is { Height: 0, Visible: true } ? null
                : plan.Levels.Select(l => new LevelDto { Name = l.Name, H = Math.Round(l.Height, 2), Visible = l.Visible }).ToList(),
            ActiveLevel = plan.ActiveLevel,
            Benchwork = plan.Benchwork,
            Frame = plan.Frame is { } fr ? new FrameDto { Dir = fr.Direction == FrameDirection.Laengs ? "laengs" : null, Spacing = fr.Spacing, Width = fr.TrasseWidth, Thick = fr.Thickness, Base = fr.BaseHeight } : null,
            Detectors = plan.Detectors.Count == 0 ? null : plan.Detectors.Select(d => new DetectorDto
                { Id = withIds ? d.Id : null, Piece = d.PieceId, Type = d.Type.ToString(), Name = d.Name, Addr = d.Address, Src = d.Source, Auto = d.Auto ? true : null }).ToList(),
            Train = plan.TrainLength > 0 ? plan.TrainLength : null,
            Prices = plan.Prices.Count == 0 ? null : new Dictionary<string, double>(plan.Prices),
            Stock = plan.Stock.Count == 0 ? null : new Dictionary<string, int>(plan.Stock),
            Background = plan.Background is not { Data.Length: > 0 } bg ? null : new BackgroundDto
            {
                Mime = bg.Mime, Data = withIds ? StoreImage(bg.Data) : bg.Data, PixelWidth = bg.PixelWidth, PixelHeight = bg.PixelHeight,
                X = Math.Round(bg.X, 2), Y = Math.Round(bg.Y, 2), W = Math.Round(bg.Width, 3), Opacity = Math.Round(bg.Opacity, 3), Visible = bg.Visible,
            },
        };
        return JsonSerializer.Serialize(dto, (withIds ? Compact : Indented).FileDto);
    }

    /// <summary>Liest einen Plan. Unbekannte Gleise werden übersprungen und in <paramref name="skipped"/> gezählt.</summary>
    public static LayoutPlan FromJson(string json, out int skipped) => FromJson(json, out skipped, out _);

    /// <param name="newer">Die Datei stammt aus einer neueren Programmversion (Formatversion höher als <see cref="CurrentFormat"/>).</param>
    public static LayoutPlan FromJson(string json, out int skipped, out bool newer)
    {
        FileDto? dto;
        try { dto = JsonSerializer.Deserialize(json, Compact.FileDto); }
        catch (JsonException e) { throw new InvalidDataException(Lang.T("Die Datei ist kein TrackMaster-Plan."), e); }
        if (dto is null || dto.App != "TrackMaster Pro" || dto.Pieces is null)
            throw new InvalidDataException(Lang.T("Die Datei ist kein TrackMaster-Plan."));

        newer = dto.Format > CurrentFormat;
        var gauge = Catalog.GetGauge(dto.Gauge ?? "H0");
        var plan = new LayoutPlan
        {
            Name = string.IsNullOrWhiteSpace(dto.Name) ? Lang.T("Gleisplan") : dto.Name!,
            GaugeId = gauge.Id,
            BoardWidth = dto.Board is { W: > 0 } ? dto.Board.W : gauge.DefaultBoardWidth,
            BoardHeight = dto.Board is { H: > 0 } ? dto.Board.H : gauge.DefaultBoardHeight,
            BoardShape = Enum.TryParse<BoardShape>(dto.Board?.Shape, out var shape) ? shape : BoardShape.Rechteck,
            BoardDepth = dto.Board?.Depth is double depth && depth > 0 ? depth : 600,
            BoardTurn = Math.Clamp(dto.Board?.Turn ?? 0, 0, 3),
            BoardLegLeft = Math.Max(0, dto.Board?.LegLeft ?? 0),
            BoardLegRight = Math.Max(0, dto.Board?.LegRight ?? 0),
            VehicleLength = Math.Clamp(dto.Vehicle ?? 0, 0, 5000),
            TrainLength = dto.Train is double tl && double.IsFinite(tl) ? Math.Clamp(tl, 0, 100000) : 0,
            Current = dto.Current switch { "ac" => CurrentKind.Wechselstrom, "dc" => CurrentKind.Gleichstrom, _ => CurrentKind.Auto },
            Operation = dto.Operation == "analog" ? PowerMode.Analog : PowerMode.Digital,
            DetectorWiring = dto.DetWires == true,
        };
        foreach (var (k, v) in dto.Prices ?? [])
            if (double.IsFinite(v) && v >= 0) plan.Prices[k] = v;
        foreach (var (k, v) in dto.Stock ?? [])
            if (v >= 0) plan.Stock[k] = v;
        if (dto.Background is { } b && LoadImage(b.Data) is { Length: > 0 } img && b.W > 0 && double.IsFinite(b.W) && b.PixelWidth > 0 && b.PixelHeight > 0)
            plan.Background = new PlanBackground
            {
                Data = img, Mime = b.Mime is "image/jpeg" or "image/png" or "image/gif" or "image/webp" ? b.Mime : "image/png",
                PixelWidth = b.PixelWidth, PixelHeight = b.PixelHeight, X = double.IsFinite(b.X) ? b.X : 0, Y = double.IsFinite(b.Y) ? b.Y : 0,
                Width = b.W, Opacity = double.IsFinite(b.Opacity) ? Math.Clamp(b.Opacity, 0.05, 1) : 0.5, Visible = b.Visible,
            };
        if (dto.Levels is { Count: > 0 } levels)
        {
            plan.Levels.Clear();
            foreach (var l in levels.Take(LayoutPlan.MaxLevels))
                plan.Levels.Add(new PlanLevel
                {
                    Name = string.IsNullOrWhiteSpace(l.Name) ? PlanLevel.DefaultName(plan.Levels.Count) : l.Name!,
                    Height = double.IsFinite(l.H) ? Math.Clamp(l.H, -1000, 5000) : 0, Visible = l.Visible,
                });
        }
        plan.ActiveLevel = Math.Clamp(dto.ActiveLevel, 0, plan.Levels.Count - 1);
        plan.SystemId = string.IsNullOrWhiteSpace(dto.System) ? null : dto.System;
        if (dto.Benchwork is { } bench && Enum.IsDefined(bench.LegStyle) && Core.Benchwork.Validate(bench).Count == 0) plan.Benchwork = bench;
        if (dto.Frame is { } fd)
        {
            var frame = new FrameSpec(fd.Dir == "laengs" ? FrameDirection.Laengs : FrameDirection.Quer, fd.Spacing, fd.Width, fd.Thick, fd.Base);
            if (FrameBuild.Valid(frame)) plan.Frame = frame;
        }
        int LevelOf(int level) => Math.Clamp(level, 0, plan.Levels.Count - 1);
        foreach (var l in dto.Board?.Legs ?? [])
            plan.BoardLegs.Add(new BoardLeg(l.Length > 0 && double.IsFinite(l[0]) ? Math.Max(0, l[0]) : 0, l.Length > 1 && double.IsFinite(l[1]) ? Math.Max(0, l[1]) : 0));
        foreach (var m in dto.Board?.Modules ?? [])
            if (m.W > 0 && m.D > 0 && double.IsFinite(m.X) && double.IsFinite(m.Y))
                plan.Modules.Add(new BoardModule { Name = m.Name ?? "", X = m.X, Y = m.Y, Width = m.W, Depth = m.D,
                    Height = double.IsFinite(m.Z) ? Math.Clamp(m.Z, ModuleLayout.MinHeight, ModuleLayout.MaxHeight) : 0,
                    Thickness = double.IsFinite(m.T) ? Math.Clamp(m.T, 0, ModuleLayout.MaxThickness) : 0 });
        if (plan.BoardShape == BoardShape.Module)
        {
            // Modulanlage ohne Module (z. B. von Hand bearbeitet): die Plattenmaße als ein Modul
            if (plan.Modules.Count == 0) plan.Modules.Add(new BoardModule { Name = Lang.T("Modul 1"), Width = plan.BoardWidth, Depth = plan.BoardHeight });
            plan.BoardWidth = Math.Max(ModuleLayout.MinSize, plan.Modules.Max(m => m.X + m.Width));
            plan.BoardHeight = Math.Max(ModuleLayout.MinSize, plan.Modules.Max(m => m.Y + m.Depth));
        }
        skipped = 0;
        // Gleise ohne Kennung bekommen neue Kennungen oberhalb aller in der Datei vorhandenen
        foreach (var p in dto.Pieces)
            if (p.Id is { } v && v > 0) plan.NextId = Math.Max(plan.NextId, v + 1);
        foreach (var p in dto.Pieces)
        {
            if (p.Key is null || Catalog.Find(p.Key) is null) { skipped++; continue; }
            var piece = new PlacedPiece
            {
                Id = p.Id is { } pid && pid > 0 && plan.Find(pid) is null ? pid : plan.NextId, Key = p.Key, X = p.X, Y = p.Y, Rotation = p.Rot, Flipped = p.Flip,
                Height = p.Z, Grade = double.IsFinite(p.Grade) ? p.Grade : 0, Tunnel = p.Tunnel, Address = p.Addr, Name = p.Name ?? "", Level = LevelOf(p.Level),
                Locked = p.Lock, Group = Math.Max(0, p.Group),
            };
            plan.Pieces.Add(piece);
            plan.NextId = Math.Max(plan.NextId, piece.Id + 1);
        }
        var used = new HashSet<int>(plan.Pieces.Select(p => p.Id));
        int Id(int? wanted)
        {
            var id = wanted is { } w && w > 0 && !used.Contains(w) ? w : plan.NextId;
            used.Add(id);
            plan.NextId = Math.Max(plan.NextId, id + 1);
            return id;
        }
        // Erst Kennungen aus der Datei vergeben, damit neue Kennungen sie nicht doppelt belegen
        foreach (var w in (dto.Scenery ?? []).Select(x => x.Id).Concat((dto.Feeders ?? []).Select(x => x.Id))
                     .Concat((dto.Isolations ?? []).Select(x => x.Id)).Concat((dto.Signals ?? []).Select(x => x.Id)).Concat((dto.Detectors ?? []).Select(x => x.Id)))
            if (w is { } v) plan.NextId = Math.Max(plan.NextId, v + 1);
        foreach (var md in dto.Models ?? [])
        {
            try
            {
                var m = ModelFile.FromDto(md);
                if (!m.IsBuiltin) plan.Models[m.Id] = m;
            }
            catch (InvalidDataException) { }
        }
        foreach (var s in dto.Scenery ?? [])
        {
            if (!Enum.TryParse<SceneryKind>(s.Kind, true, out var kind)) continue;
            var item = new SceneryItem
            {
                Id = Id(s.Id), Kind = kind, Name = s.Name ?? SceneryItem.KindName(kind), X = s.X, Y = s.Y, Width = s.W, Depth = s.D, Rotation = s.Rot, Height = s.H, Level = LevelOf(s.Level),
                Variant = kind == SceneryKind.Obstacle && Enum.IsDefined((ObstacleType)s.Variant) || kind == SceneryKind.Plate || kind == SceneryKind.Wire && Enum.IsDefined((WireUse)s.Variant) || kind == SceneryKind.Power && Enum.IsDefined((PowerSourceType)s.Variant) ? s.Variant : 0,
                Points = (s.Points ?? []).Where(q => q.Length >= 2).Select(q => (q[0], q[1])).ToList(),
                Model = kind is SceneryKind.Building or SceneryKind.Power && !string.IsNullOrWhiteSpace(s.Model) ? s.Model.Trim() : null,
                Address = kind == SceneryKind.Power && s.Addr is > 0 and <= 4096 ? s.Addr : null,
                Color = kind is SceneryKind.Building or SceneryKind.Wire or SceneryKind.Power && ModelBody.ParseColor(s.Color) is { } sc ? ModelBody.ToHex(sc) : null,
            };
            if (kind == SceneryKind.Building && item.Points.Count < 2) item.Points = [];
            if (kind == SceneryKind.Building && item.Model is not null && s.Tracks is { Count: > 0 } tracks) item.Tracks = tracks.Distinct().ToList();
            // automatisch verlegtes Rückmelder-Kabel: Gleis des Rückmelders
            // automatisch verlegte Kabel und angelegte Besetztmelder (siehe DetectorWiring)
            if (kind is SceneryKind.Wire or SceneryKind.Power && s.Tracks is [var autoTag]) item.Tracks = [autoTag];
            if ((item.IsArea || kind == SceneryKind.Room) && item.Points.Count < 3) continue;
            if (kind is SceneryKind.Dimension or SceneryKind.Beam && item.Points.Count != 2) continue;
            if (kind == SceneryKind.Wire && item.Points.Count < 2) continue;
            if (kind == SceneryKind.Power)
            {
                item.Points = [];
                if (item.Width <= 0 || item.Depth <= 0) (item.Width, item.Depth) = PowerSupply.Type(item) == PowerSourceType.Verteiler ? PowerSupply.DistributorSize(plan.Gauge) : PowerSupply.Size(plan.Gauge);
            }
            if (kind == SceneryKind.Beam && !BeamLayout.Valid(item)) BeamLayout.Repair(item);
            if (kind == SceneryKind.Plate && (item.Points.Count > 0 || !PlateLayout.Valid(item))) PlateLayout.Repair(item);
            plan.Scenery.Add(item);
        }
        foreach (var f in dto.Feeders ?? [])
            if (plan.Find(f.Piece) is not null)
                plan.Feeders.Add(new Feeder { Id = Id(f.Id), PieceId = f.Piece, Circuit = string.IsNullOrWhiteSpace(f.Circuit) ? "Stromkreis 1" : f.Circuit!, Reversed = f.Rev, Ground = f.Gnd, At = Math.Clamp(f.At ?? 0.5, 0, 1), WireId = f.Wire, Name = f.Name?.Trim() ?? "" });
        foreach (var i in dto.Isolations ?? [])
            if (plan.Find(i.Piece) is { } ip && i.End >= 0 && i.End < ip.WorldConnectors().Count)
                plan.Isolations.Add(new Isolation { Id = Id(i.Id), PieceId = i.Piece, Index = i.End, Auto = i.Auto == true });
        foreach (var g in dto.Signals ?? [])
            if (plan.Find(g.Piece) is { } gp && g.End >= 0 && g.End < gp.WorldConnectors().Count)
                plan.Signals.Add(new Signal
                {
                    Id = Id(g.Id), PieceId = g.Piece, Index = g.End, Name = g.Name ?? "", Address = g.Addr,
                    Type = Enum.TryParse<SignalType>(g.Type, true, out var t) ? t : SignalType.Hauptsignal,
                    System = Enum.TryParse<SignalSystem>(g.System, true, out var sys) ? sys : SignalSystem.HV,
                });
        foreach (var d in dto.Detectors ?? [])
            if (plan.Find(d.Piece) is not null)
                plan.Detectors.Add(new Detector
                {
                    Id = Id(d.Id), PieceId = d.Piece, Name = d.Name ?? "", Address = d.Addr, Source = d.Src?.Trim(), Auto = d.Auto == true,
                    Type = Enum.TryParse<DetectorType>(d.Type, true, out var dt) ? dt : DetectorType.Strom,
                });
        return plan;
    }
}
