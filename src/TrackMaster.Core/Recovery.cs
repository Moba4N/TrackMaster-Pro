using System.Text.Json;

namespace TrackMaster.Core;

/// <summary>
/// Automatische Sicherung des geöffneten Plans. Liegt nach einem Absturz oder Stromausfall noch eine Sicherung vor,
/// bietet das Programm beim nächsten Start an, sie wiederherzustellen.
/// </summary>
public sealed class Recovery(string folder)
{
    public sealed record Snapshot(string Json, string? Path, string Name, DateTime Saved);

    private sealed class Dto
    {
        public string? Path { get; set; }
        public string Name { get; set; } = "";
        public DateTime Saved { get; set; }
        public string Plan { get; set; } = "";
    }

    public string FilePath => System.IO.Path.Combine(folder, "sicherung.json");

    /// <summary>Sichert den Plan. Erst in eine Zwischendatei, damit eine abgebrochene Sicherung die letzte gute nicht zerstört.</summary>
    public void Write(LayoutPlan plan, string? path)
    {
        Directory.CreateDirectory(folder);
        var dto = new Dto { Path = path, Name = plan.Name, Saved = DateTime.Now, Plan = PlanSerializer.ToJson(plan, withIds: true) };
        var temp = FilePath + ".neu";
        File.WriteAllText(temp, JsonSerializer.Serialize(dto));
        File.Move(temp, FilePath, overwrite: true);
    }

    /// <summary>Die vorhandene Sicherung, wenn sie sich lesen lässt.</summary>
    public Snapshot? TryLoad()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(FilePath));
            if (dto is null || string.IsNullOrEmpty(dto.Plan)) return null;
            PlanSerializer.FromJson(dto.Plan, out _);
            return new Snapshot(dto.Plan, dto.Path, dto.Name, dto.Saved);
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Nach Speichern oder regulärem Beenden: Sicherung entfernen.</summary>
    public void Clear()
    {
        try { File.Delete(FilePath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>Zuletzt geöffnete Pläne, neueste zuerst; Dateien, die es nicht mehr gibt, fallen heraus.</summary>
public sealed class RecentFiles(string file, int max = 8)
{
    public IReadOnlyList<string> Items
    {
        get
        {
            try { return File.Exists(file) ? File.ReadAllLines(file).Where(l => l.Length > 0 && File.Exists(l)).Take(max).ToList() : []; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
        }
    }

    public void Add(string path)
    {
        try
        {
            var full = System.IO.Path.GetFullPath(path);
            var list = Items.Where(p => !string.Equals(p, full, StringComparison.OrdinalIgnoreCase)).Prepend(full).Take(max).ToList();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            File.WriteAllLines(file, list);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
