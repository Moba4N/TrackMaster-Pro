using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrackMaster.Core;

/// <summary>
/// Dateiformat *.trackmaster.json – dasselbe wie in der Browser-Version, Dateien lassen sich hin und her öffnen.
/// </summary>
public static class PlanSerializer
{
    public const string FileExtension = ".trackmaster.json";

    private sealed class FileDto
    {
        [JsonPropertyName("app")] public string App { get; set; } = "TrackMaster Pro";
        [JsonPropertyName("format")] public int Format { get; set; } = 1;
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("gauge")] public string? Gauge { get; set; }
        [JsonPropertyName("board")] public BoardDto? Board { get; set; }
        [JsonPropertyName("pieces")] public List<PieceDto>? Pieces { get; set; }
    }

    private sealed class BoardDto
    {
        [JsonPropertyName("w")] public double W { get; set; }
        [JsonPropertyName("h")] public double H { get; set; }
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
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <param name="withIds">Für Rückgängig-Schritte: Kennungen behalten, damit die Auswahl erhalten bleibt.</param>
    public static string ToJson(LayoutPlan plan, bool withIds = false)
    {
        var dto = new FileDto
        {
            Name = plan.Name,
            Gauge = plan.GaugeId,
            Board = new BoardDto { W = plan.BoardWidth, H = plan.BoardHeight },
            Pieces = plan.Pieces.Select(p => new PieceDto
            {
                Id = withIds ? p.Id : null,
                Key = p.Key,
                X = Math.Round(p.X, 3),
                Y = Math.Round(p.Y, 3),
                Rot = Math.Round(p.Rotation, 4),
                Flip = p.Flipped,
                Z = p.Height,
            }).ToList(),
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>Liest einen Plan. Unbekannte Gleise werden übersprungen und in <paramref name="skipped"/> gezählt.</summary>
    public static LayoutPlan FromJson(string json, out int skipped)
    {
        FileDto? dto;
        try { dto = JsonSerializer.Deserialize<FileDto>(json); }
        catch (JsonException e) { throw new InvalidDataException("Die Datei ist kein TrackMaster-Plan.", e); }
        if (dto is null || dto.App != "TrackMaster Pro" || dto.Pieces is null)
            throw new InvalidDataException("Die Datei ist kein TrackMaster-Plan.");

        var gauge = Catalog.GetGauge(dto.Gauge ?? "H0");
        var plan = new LayoutPlan
        {
            Name = string.IsNullOrWhiteSpace(dto.Name) ? "Gleisplan" : dto.Name!,
            GaugeId = gauge.Id,
            BoardWidth = dto.Board is { W: > 0 } ? dto.Board.W : gauge.DefaultBoardWidth,
            BoardHeight = dto.Board is { H: > 0 } ? dto.Board.H : gauge.DefaultBoardHeight,
        };
        skipped = 0;
        foreach (var p in dto.Pieces)
        {
            if (p.Key is null || Catalog.Find(p.Key) is null) { skipped++; continue; }
            var piece = new PlacedPiece { Id = p.Id ?? plan.NextId, Key = p.Key, X = p.X, Y = p.Y, Rotation = p.Rot, Flipped = p.Flip, Height = p.Z };
            plan.Pieces.Add(piece);
            plan.NextId = Math.Max(plan.NextId, piece.Id + 1);
        }
        return plan;
    }
}
