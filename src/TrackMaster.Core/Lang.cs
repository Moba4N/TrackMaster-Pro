using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace TrackMaster.Core;

public enum Language { Deutsch, English }

/// <summary>
/// Sprache der Oberfläche. Die Texte stehen im Quelltext auf Deutsch; <see cref="T"/> und <see cref="F"/> schlagen sie in der
/// englischen Übersetzung (Lang.en.json) nach. Fehlt eine Übersetzung, bleibt der deutsche Text stehen.
/// Die Wahl liegt in sprache.txt im Datenordner; ohne Wahl gilt die Sprache des Systems (Deutsch für de, sonst Englisch).
/// </summary>
public static class Lang
{
    private static Language? _current;
    private static Dictionary<string, string>? _english;

    public static string SettingsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrackMaster Pro", "sprache.txt");

    /// <summary>Gewählte Sprache; beim ersten Zugriff aus der Einstellung bzw. der Systemsprache bestimmt.</summary>
    public static Language Current
    {
        get => _current ??= Load();
        set => _current = value;
    }

    public static bool IsEnglish => Current == Language.English;

    private static Language Load()
    {
        try
        {
            if (File.Exists(SettingsFile) && Enum.TryParse<Language>(File.ReadAllText(SettingsFile).Trim(), out var saved)) return saved;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? Language.Deutsch : Language.English;
    }

    /// <summary>Speichert die Wahl; sie gilt nach dem nächsten Start.</summary>
    public static void Save(Language language)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, language.ToString());
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static Dictionary<string, string> English
    {
        get
        {
            if (_english is not null) return _english;
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TrackMaster.Core.Lang.en.json");
            _english = stream is null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
            return _english;
        }
    }

    /// <summary>Übersetzt einen festen Text.</summary>
    public static string T(string german)
        => !IsEnglish || string.IsNullOrEmpty(german) ? german : English.TryGetValue(german, out var en) ? en : german;

    /// <summary>Übersetzt einen Text mit Platzhaltern: nachgeschlagen wird die Vorlage ({0}, {1:0.0} …), dann eingesetzt.</summary>
    public static string F(FormattableString text)
    {
        if (!IsEnglish) return text.ToString(CultureInfo.CurrentCulture);
        var format = English.TryGetValue(text.Format, out var en) ? en : text.Format;
        try { return string.Format(CultureInfo.CurrentCulture, format, text.GetArguments()); }
        catch (FormatException) { return text.ToString(CultureInfo.CurrentCulture); }
    }

    /// <summary>Alle Übersetzungen (für Tests).</summary>
    public static IReadOnlyDictionary<string, string> EnglishTexts => English;
}
