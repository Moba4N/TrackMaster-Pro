using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrackMaster.Core;

/// <summary>
/// Dateiformat für Ausstattungsmodelle (*.tmmodell, JSON, Kennung „modell-1“) und für Modellsammlungen (ZIP mit mehreren *.tmmodell).
/// Dieselben Daten stehen in Plänen unter „models“, wenn eigene Modelle verwendet werden.
/// </summary>
public static class ModelFile
{
    public const string Extension = ".tmmodell";
    public const string FormatId = "modell-1";

    internal sealed class BodyDto
    {
        [JsonPropertyName("shape")] public string Shape { get; set; } = "Quader";
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
        [JsonPropertyName("z"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double Z { get; set; }
        [JsonPropertyName("w")] public double W { get; set; }
        [JsonPropertyName("d")] public double D { get; set; }
        [JsonPropertyName("h")] public double H { get; set; }
        [JsonPropertyName("rot"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double Rot { get; set; }
        [JsonPropertyName("material")] public string Material { get; set; } = "Putz";
        [JsonPropertyName("color"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Color { get; set; }
        [JsonPropertyName("top"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Top { get; set; }
        [JsonPropertyName("openingW"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double OpeningW { get; set; }
        [JsonPropertyName("openingH"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double OpeningH { get; set; }
        [JsonPropertyName("points"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<double[]>? Points { get; set; }
        [JsonPropertyName("heights"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<double>? Heights { get; set; }
        [JsonPropertyName("vertices"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<double[]>? Vertices { get; set; }
        [JsonPropertyName("faces"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<int[]>? Faces { get; set; }
        [JsonPropertyName("group"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Group { get; set; }
    }

    internal sealed class ModelDto
    {
        [JsonPropertyName("format")] public string Format { get; set; } = FormatId;
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("category"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Category { get; set; }
        [JsonPropertyName("description"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Description { get; set; }
        [JsonPropertyName("snap"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Snap { get; set; }
        [JsonPropertyName("mainMaterial"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? MainMaterial { get; set; }
        [JsonPropertyName("unit")] public string Unit { get; set; } = "m";
        [JsonPropertyName("bodies")] public List<BodyDto> Bodies { get; set; } = [];
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static double R(double v) => Math.Round(v, 4);
    private static double F(double v, double fallback = 0) => double.IsFinite(v) ? v : fallback;

    internal static ModelDto ToDto(ModelDefinition m) => new()
    {
        Id = m.Id, Name = m.Name,
        Category = string.IsNullOrWhiteSpace(m.Category) ? null : m.Category,
        Description = string.IsNullOrWhiteSpace(m.Description) ? null : m.Description,
        Snap = m.Snap == ModelSnap.Frei ? null : m.Snap.ToString(),
        MainMaterial = m.MainMaterial?.ToString(),
        Bodies = m.Bodies.Select(b => new BodyDto
        {
            Shape = b.Shape.ToString(), X = R(b.X), Y = R(b.Y), Z = R(b.Z), W = R(b.W), D = R(b.D), H = R(b.H), Rot = R(b.Rotation),
            Material = b.Material.ToString(), Color = string.IsNullOrWhiteSpace(b.Color) ? null : b.Color,
            Top = Math.Abs(b.Top - 1) < 1e-9 ? null : R(b.Top), OpeningW = R(b.OpeningW), OpeningH = R(b.OpeningH),
            Points = b.Points.Count == 0 ? null : b.Points.Select(p => new[] { R(p.X), R(p.Y) }).ToList(),
            Heights = b.Heights.Count == 0 ? null : b.Heights.Select(R).ToList(),
            Vertices = b.Vertices.Count == 0 ? null : b.Vertices.Select(v => new[] { R(v.X), R(v.Y), R(v.Z) }).ToList(),
            Faces = b.Faces.Count == 0 ? null : b.Faces.Select(f => (int[])f.Clone()).ToList(),
            Group = b.Group,
        }).ToList(),
    };

    internal static ModelDefinition FromDto(ModelDto d)
    {
        if (d.Format != FormatId) throw new InvalidDataException(Lang.T("Unbekanntes Format der Modelldatei."));
        var scale = d.Unit switch { "cm" => 0.01, "mm" => 0.001, _ => 1.0 };
        var m = new ModelDefinition
        {
            Id = string.IsNullOrWhiteSpace(d.Id) ? NewId(d.Name) : d.Id.Trim(),
            Name = string.IsNullOrWhiteSpace(d.Name) ? Lang.T("Modell") : d.Name.Trim(),
            Category = d.Category?.Trim() ?? "",
            Description = d.Description?.Trim() ?? "",
            Snap = Enum.TryParse<ModelSnap>(d.Snap, true, out var snap) ? snap : ModelSnap.Frei,
            MainMaterial = Enum.TryParse<ModelMaterial>(d.MainMaterial, true, out var mm) ? mm : null,
        };
        foreach (var b in d.Bodies.Take(5000))
        {
            if (!Enum.TryParse<BodyShape>(b.Shape, true, out var shape)) continue;
            var body = new ModelBody
            {
                Shape = shape, X = F(b.X) * scale, Y = F(b.Y) * scale, Z = F(b.Z) * scale,
                W = Math.Abs(F(b.W, 1)) * scale, D = Math.Abs(F(b.D, 1)) * scale, H = F(b.H, 1) * scale, Rotation = F(b.Rot),
                Material = Enum.TryParse<ModelMaterial>(b.Material, true, out var mat) ? mat : ModelMaterial.Putz,
                Color = ModelBody.ParseColor(b.Color) is { } c ? ModelBody.ToHex(c) : null,
                Top = Math.Clamp(F(b.Top ?? 1, 1), 0, 4),
                OpeningW = Math.Max(0, F(b.OpeningW)) * scale, OpeningH = Math.Max(0, F(b.OpeningH)) * scale,
                Points = (b.Points ?? []).Where(p => p.Length >= 2 && double.IsFinite(p[0]) && double.IsFinite(p[1])).Select(p => (p[0] * scale, p[1] * scale)).ToList(),
                Heights = (b.Heights ?? []).Select(h => F(h) * scale).ToList(),
                Vertices = (b.Vertices ?? []).Where(p => p.Length >= 3 && p.All(double.IsFinite)).Select(p => (p[0] * scale, p[1] * scale, p[2] * scale)).ToList(),
                Group = Math.Max(0, b.Group),
            };
            body.Faces = (b.Faces ?? []).Where(f => f is { Length: >= 3 } && f.All(i => i >= 0 && i < body.Vertices.Count)).ToList();
            m.Bodies.Add(body);
        }
        return m;
    }

    /// <summary>Neue Kennung für ein eigenes Modell aus dem Namen.</summary>
    public static string NewId(string name)
    {
        var slug = new string((name ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (slug.Length > 30) slug = slug[..30];
        return $"eigen.{(slug.Length == 0 ? "modell" : slug)}.{Guid.NewGuid().ToString("N")[..8]}";
    }

    public static string ToJson(ModelDefinition m) => JsonSerializer.Serialize(ToDto(m), Options);

    public static ModelDefinition FromJson(string json)
    {
        ModelDto? dto;
        try { dto = JsonSerializer.Deserialize<ModelDto>(json); }
        catch (JsonException e) { throw new InvalidDataException(Lang.T("Die Datei ist kein TrackMaster-Modell."), e); }
        if (dto is null) throw new InvalidDataException(Lang.T("Die Datei ist kein TrackMaster-Modell."));
        return FromDto(dto);
    }

    /// <summary>Mehrere Modelle als ZIP (je Modell eine *.tmmodell-Datei).</summary>
    public static byte[] ToZip(IEnumerable<ModelDefinition> models)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in models)
            {
                var name = SafeFileName(m.Name);
                var file = name;
                for (var i = 2; !used.Add(file); i++) file = $"{name} ({i})";
                using var w = new StreamWriter(zip.CreateEntry(file + Extension).Open(), new UTF8Encoding(false));
                w.Write(ToJson(m));
            }
        }
        return ms.ToArray();
    }

    /// <summary>Liest alle Modelle aus einem ZIP; fehlerhafte Einträge werden übersprungen und gezählt.</summary>
    public static List<ModelDefinition> FromZip(byte[] data, out int skipped)
    {
        skipped = 0;
        var list = new List<ModelDefinition>();
        using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
        foreach (var e in zip.Entries.Where(e => e.FullName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)).Take(2000))
        {
            try
            {
                if (e.Length > 4_000_000) { skipped++; continue; }
                using var r = new StreamReader(e.Open(), Encoding.UTF8);
                list.Add(FromJson(r.ReadToEnd()));
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException) { skipped++; }
        }
        return list;
    }

    /// <summary>Liest eine Modelldatei oder ein ZIP mit Modellen (am Inhalt erkannt).</summary>
    public static List<ModelDefinition> Read(byte[] data, out int skipped)
    {
        skipped = 0;
        if (data.Length > 1 && data[0] == 'P' && data[1] == 'K') return FromZip(data, out skipped);
        return [FromJson(Encoding.UTF8.GetString(data))];
    }

    public static string SafeFileName(string name)
    {
        var bad = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var s = new string((name ?? "").Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
        return s.Length == 0 ? "Modell" : s;
    }

    /// <summary>Für den Plan: Modelle als kompakte Liste (ohne Einrückung).</summary>
    internal static List<ModelDto>? ForPlan(LayoutPlan plan)
    {
        var used = plan.Scenery.Where(s => s.IsModel).Select(s => s.Model!).ToHashSet();
        var list = plan.Models.Values.Where(m => used.Contains(m.Id)).Select(ToDto).ToList();
        return list.Count == 0 ? null : list;
    }
}
