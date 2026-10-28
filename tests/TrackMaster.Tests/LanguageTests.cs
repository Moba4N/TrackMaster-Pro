using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using TrackMaster.Core;
using Xunit;

namespace TrackMaster.Tests;

/// <summary>Die Tests prüfen deutsche Texte; die Systemsprache des Rechners darf das nicht ändern.</summary>
internal static class GermanForTests
{
    [ModuleInitializer]
    internal static void Init() => Lang.Current = Language.Deutsch;
}

[CollectionDefinition("Sprache", DisableParallelization = true)]
public class LanguageCollection;

[Collection("Sprache")]
public class LanguageTests
{
    private static readonly Regex Placeholder = new(@"\{(\d+)(:[^}]*)?\}");

    // Nur die Nummer zählt; das Datumsformat darf sich mit der Sprache ändern
    private static string Index(Match m) => m.Groups[1].Value;

    [Fact]
    public void Translations_keep_all_placeholders()
    {
        var wrong = new List<string>();
        foreach (var (key, value) in Lang.EnglishTexts)
        {
            var a = Placeholder.Matches(key).Select(Index).Order().ToList();
            var b = Placeholder.Matches(value).Select(Index).Order().ToList();
            if (!a.SequenceEqual(b)) wrong.Add($"{key} → {value}");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
        Assert.True(Lang.EnglishTexts.Count > 800);
    }

    [Fact]
    public void English_texts_are_used_and_numbers_use_points()
    {
        try
        {
            Lang.Current = Language.English;
            Assert.Equal("Cancel", Lang.T("Abbrechen"));
            Assert.Equal("1.5", Fmt.Num(1.5, 1));
            Assert.Equal(2.5, Fmt.Parse("2.5"));
            var issues = PlanCheck.Run(SamplePlan.Build());
            Assert.NotEmpty(issues);
            // Kategorien sind übersetzt: kein deutscher Schlüssel bleibt stehen
            Assert.All(issues, i => Assert.False(Lang.EnglishTexts.ContainsKey(i.Category), i.Category));
        }
        finally { Lang.Current = Language.Deutsch; }
    }
}
