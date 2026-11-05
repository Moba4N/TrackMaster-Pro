using System.Text.Json;

namespace TrackMaster.Core;

/// <summary>
/// Eigene Modellbibliothek als Ordner mit *.tmmodell-Dateien (Windows: Dokumente\TrackMaster Pro\Modelle).
/// Je Modell eine Datei, benannt nach dem Modellnamen; zugeordnet wird über die Kennung in der Datei.
/// </summary>
public sealed class ModelStore(string folder)
{
    public string Folder { get; } = folder;

    /// <summary>Standardordner in den Dokumenten des Benutzers.</summary>
    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TrackMaster Pro", "Modelle");

    private readonly Dictionary<string, string> _files = [];

    /// <summary>Liest alle Modelle des Ordners; unlesbare Dateien werden übersprungen und gezählt.</summary>
    public List<ModelDefinition> LoadAll(out int skipped)
    {
        skipped = 0;
        _files.Clear();
        var list = new List<ModelDefinition>();
        if (!Directory.Exists(Folder)) return list;
        foreach (var file in Directory.EnumerateFiles(Folder, "*" + ModelFile.Extension).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Take(2000))
        {
            try
            {
                if (new FileInfo(file).Length > 4_000_000) { skipped++; continue; }
                var m = ModelFile.FromJson(File.ReadAllText(file));
                if (m.IsBuiltin) m.Id = ModelFile.NewId(m.Name);
                if (_files.ContainsKey(m.Id)) { skipped++; continue; }
                _files[m.Id] = file;
                list.Add(m);
            }
            catch (Exception e) when (e is InvalidDataException or JsonException or IOException or UnauthorizedAccessException) { skipped++; }
        }
        return list;
    }

    /// <summary>Speichert ein Modell (vorhandene Datei derselben Kennung wird ersetzt, bei neuem Namen umbenannt).</summary>
    public string Save(ModelDefinition model)
    {
        Directory.CreateDirectory(Folder);
        var wanted = Path.Combine(Folder, ModelFile.SafeFileName(model.Name) + ModelFile.Extension);
        _files.TryGetValue(model.Id, out var old);
        var target = wanted;
        if (!string.Equals(old, wanted, StringComparison.OrdinalIgnoreCase))
            for (var i = 2; File.Exists(target) || _files.Values.Contains(target, StringComparer.OrdinalIgnoreCase); i++)
                target = Path.Combine(Folder, $"{ModelFile.SafeFileName(model.Name)} ({i}){ModelFile.Extension}");
        File.WriteAllText(target, ModelFile.ToJson(model));
        if (old is not null && !string.Equals(old, target, StringComparison.OrdinalIgnoreCase) && File.Exists(old)) File.Delete(old);
        _files[model.Id] = target;
        return target;
    }

    public void Delete(string id)
    {
        if (_files.Remove(id, out var file) && File.Exists(file)) File.Delete(file);
    }

    /// <summary>Datei eines Modells (null, wenn nicht aus diesem Ordner).</summary>
    public string? FileOf(string id) => _files.GetValueOrDefault(id);
}
