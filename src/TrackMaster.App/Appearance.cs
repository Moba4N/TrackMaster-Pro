using System.IO;
using System.Text.Json;
using TrackMaster.Core;
using TrackMaster.Licensing;

namespace TrackMaster.App;

/// <summary>
/// Gewählte Darstellung (Design, Schrift, Schriftgröße). Wird vor dem ersten Zugriff auf <see cref="Theme"/> geladen;
/// eine Änderung wirkt nach einem Neustart des Fensters (siehe MainWindow.ApplyAppearance).
/// </summary>
public static class Appearance
{
    private static string FilePath => Path.Combine(AppFolders.Data, "darstellung.json");

    public static AppearanceSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(FilePath)) is { } s) Current = s;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
    }

    /// <summary>Für Bildschirmfotos: Darstellung setzen, ohne sie zu speichern.</summary>
    public static void Use(AppearanceSettings settings) => Current = settings;

    public static void Save(AppearanceSettings settings)
    {
        Current = settings;
        try
        {
            Directory.CreateDirectory(AppFolders.Data);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
