using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrackMaster.Core;

/// <summary>
/// Dateiformat *.trackmaster.json – dasselbe wie in der Browser-Version, Dateien lassen sich hin und her öffnen.
/// </summary>
public static class PlanSerializer
{
    public const string FileExtension = ".trackmaster.json";
    /// <summary>
    /// Version des Dateiformats. 2: Beschriftungen, Maßlinien, Drehscheiben und Schiebebühnen. Ältere Programmversionen
    /// überspringen, was sie nicht kennen; neuere Dateien öffnet dieses Programm mit einem Hinweis.
    /// </summary>
    public const int CurrentFormat = 2;

    private sealed class FileDto
    {
        [JsonPropertyName("app")] public string App { get; set; } = "TrackMaster Pro";
        [JsonPropertyName("format")] public int Format { get; set; } = CurrentFormat;
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("gauge")] public string? Gauge { get; set; }
        [JsonPropertyName("board")] public BoardDto? Board { get; set; }
        [JsonPropertyName("vehicle"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Vehicle { get; set; }
        [JsonPropertyName("pieces")] public List<PieceDto>? Pieces { get; set; }
        [JsonPropertyName("scenery"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<SceneryDto>? Scenery { get; set; }
        [JsonPropertyName("feeders"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<FeederDto>? Feeders { get; set; }
        [JsonPropertyName("isolations"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<IsolationDto>? Isolations { get; set; }
        [JsonPropertyName("signals"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<SignalDto>? Signals { get; set; }
    }

    private sealed class SceneryDto
    {
        [JsonPropertyName("id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Id { get; set; }
        [JsonPropertyName("kind")] public string Kind { get; set; } = "";
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("points"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<double[]>? Points { get; set; }
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
        [JsonPropertyName("w")] public double W { get; set; }
        [JsonPropertyName("d")] public double D { get; set; }
        [JsonPropertyName("rot")] public double Rot { get; set; }
        [JsonPropertyName("h")] public double H { get; set; }
    }

    private sealed class FeederDto
    {
        [JsonPropertyName("id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Id { get; set; }
        [JsonPropertyName("piece")] public int Piece { get; set; }
        [JsonPropertyName("circuit")] public string? Circuit { get; set; }
    }

    private sealed class IsolationDto
    {
        [JsonPropertyName("id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Id { get; set; }
        [JsonPropertyName("piece")] public int Piece { get; set; }
        [JsonPropertyName("end")] public int End { get; set; }
    }

    private sealed class SignalDto
    {
        [JsonPropertyName("id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Id { get; set; }
        [JsonPropertyName("piece")] public int Piece { get; set; }
        [JsonPropertyName("end")] public int End { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
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
    }

    private sealed class ModuleDto
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
        [JsonPropertyName("w")] public double W { get; set; }
        [JsonPropertyName("d")] public double D { get; set; }
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
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>
    /// Speichert den Plan. Gleise behalten in der Datei ihre Kennung, weil Einspeisungen, Trennstellen und Signale darauf verweisen;
    /// die Browser-Version liest die zusätzlichen Felder nicht und übernimmt nur die Gleise.
    /// </summary>
    /// <param name="withIds">Für Rückgängig-Schritte: auch Kennungen von Gelände und Elektrik behalten, damit die Auswahl erhalten bleibt.</param>
    public static string ToJson(LayoutPlan plan, bool withIds = false)
    {
        var needIds = withIds || plan.Feeders.Count > 0 || plan.Isolations.Count > 0 || plan.Signals.Count > 0;
        var dto = new FileDto
        {
            Name = plan.Name,
            Gauge = plan.GaugeId,
            Board = new BoardDto
            {
                W = plan.BoardWidth, H = plan.BoardHeight,
                Shape = plan.BoardShape == BoardShape.Rechteck ? null : plan.BoardShape.ToString(),
                Depth = plan.BoardShape == BoardShape.Rechteck ? null : plan.BoardDepth,
                Turn = plan.BoardShape == BoardShape.Rechteck || plan.BoardTurn == 0 ? null : plan.BoardTurn,
                LegLeft = plan.BoardShape == BoardShape.U && plan.BoardLegLeft > 0 ? plan.BoardLegLeft : null,
                LegRight = plan.BoardShape == BoardShape.U && plan.BoardLegRight > 0 ? plan.BoardLegRight : null,
                Modules = plan.BoardShape == BoardShape.Module && plan.Modules.Count > 0
                    ? plan.Modules.Select(m => new ModuleDto { Name = m.Name, X = Math.Round(m.X, 3), Y = Math.Round(m.Y, 3), W = Math.Round(m.Width, 3), D = Math.Round(m.Depth, 3) }).ToList()
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
            }).ToList(),
            Scenery = plan.Scenery.Count == 0 ? null : plan.Scenery.Select(s => new SceneryDto
            {
                Id = withIds ? s.Id : null,
                Kind = s.Kind.ToString().ToLowerInvariant(),
                Name = s.Name,
                Points = s.IsArea || s.Kind == SceneryKind.Dimension ? s.Points.Select(q => new[] { Math.Round(q.X, 2), Math.Round(q.Y, 2) }).ToList() : null,
                X = Math.Round(s.X, 2), Y = Math.Round(s.Y, 2), W = s.Width, D = s.Depth, Rot = s.Rotation, H = s.Height,
            }).ToList(),
            Feeders = plan.Feeders.Count == 0 ? null : plan.Feeders.Select(f => new FeederDto { Id = withIds ? f.Id : null, Piece = f.PieceId, Circuit = f.Circuit }).ToList(),
            Isolations = plan.Isolations.Count == 0 ? null : plan.Isolations.Select(i => new IsolationDto { Id = withIds ? i.Id : null, Piece = i.PieceId, End = i.Index }).ToList(),
            Signals = plan.Signals.Count == 0 ? null : plan.Signals.Select(g => new SignalDto
                { Id = withIds ? g.Id : null, Piece = g.PieceId, End = g.Index, Type = g.Type.ToString(), Name = g.Name, Addr = g.Address }).ToList(),
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>Liest einen Plan. Unbekannte Gleise werden übersprungen und in <paramref name="skipped"/> gezählt.</summary>
    public static LayoutPlan FromJson(string json, out int skipped) => FromJson(json, out skipped, out _);

    /// <param name="newer">Die Datei stammt aus einer neueren Programmversion (Formatversion höher als <see cref="CurrentFormat"/>).</param>
    public static LayoutPlan FromJson(string json, out int skipped, out bool newer)
    {
        FileDto? dto;
        try { dto = JsonSerializer.Deserialize<FileDto>(json); }
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
        };
        foreach (var m in dto.Board?.Modules ?? [])
            if (m.W > 0 && m.D > 0 && double.IsFinite(m.X) && double.IsFinite(m.Y))
                plan.Modules.Add(new BoardModule { Name = m.Name ?? "", X = m.X, Y = m.Y, Width = m.W, Depth = m.D });
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
                Height = p.Z, Grade = double.IsFinite(p.Grade) ? p.Grade : 0, Tunnel = p.Tunnel, Address = p.Addr,
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
                     .Concat((dto.Isolations ?? []).Select(x => x.Id)).Concat((dto.Signals ?? []).Select(x => x.Id)))
            if (w is { } v) plan.NextId = Math.Max(plan.NextId, v + 1);
        foreach (var s in dto.Scenery ?? [])
        {
            if (!Enum.TryParse<SceneryKind>(s.Kind, true, out var kind)) continue;
            var item = new SceneryItem
            {
                Id = Id(s.Id), Kind = kind, Name = s.Name ?? SceneryItem.KindName(kind), X = s.X, Y = s.Y, Width = s.W, Depth = s.D, Rotation = s.Rot, Height = s.H,
                Points = (s.Points ?? []).Where(q => q.Length >= 2).Select(q => (q[0], q[1])).ToList(),
            };
            if (item.IsArea && item.Points.Count < 3) continue;
            if (kind == SceneryKind.Dimension && item.Points.Count != 2) continue;
            plan.Scenery.Add(item);
        }
        foreach (var f in dto.Feeders ?? [])
            if (plan.Find(f.Piece) is not null)
                plan.Feeders.Add(new Feeder { Id = Id(f.Id), PieceId = f.Piece, Circuit = string.IsNullOrWhiteSpace(f.Circuit) ? "Stromkreis 1" : f.Circuit! });
        foreach (var i in dto.Isolations ?? [])
            if (plan.Find(i.Piece) is { } ip && i.End >= 0 && i.End < ip.WorldConnectors().Count)
                plan.Isolations.Add(new Isolation { Id = Id(i.Id), PieceId = i.Piece, Index = i.End });
        foreach (var g in dto.Signals ?? [])
            if (plan.Find(g.Piece) is { } gp && g.End >= 0 && g.End < gp.WorldConnectors().Count)
                plan.Signals.Add(new Signal
                {
                    Id = Id(g.Id), PieceId = g.Piece, Index = g.End, Name = g.Name ?? "", Address = g.Addr,
                    Type = Enum.TryParse<SignalType>(g.Type, true, out var t) ? t : SignalType.Hauptsignal,
                });
        return plan;
    }
}
