using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.Localization;

namespace IRacingOverlay.App.Tests;

/// <summary>
/// What keeps the language files honest. Adding a language is adding a file, so these are what tell
/// a translator what is missing; and a key typed in code that no file has would show up on screen as
/// the key itself, so they check the code's side too. None of them switch the language: the tests run
/// in parallel and the rest read English.
/// </summary>
public partial class LocalizationTests
{
    private static readonly Dictionary<string, string> English = Loc.Load(Loc.SourceLanguage)!;

    public static TheoryData<string> Languages()
    {
        var data = new TheoryData<string>();
        foreach (var language in Loc.Available)
        {
            data.Add(language.Code);
        }

        return data;
    }

    [Fact]
    public void Available_ListsEnglishFirst_AndSpanish()
    {
        Assert.Equal(Loc.SourceLanguage, Loc.Available[0].Code);
        Assert.Contains(Loc.Available, language => language.Code == "es" && language.NativeName == "Español");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryLanguage_HasItsName_AndExactlyTheEnglishKeys(string code)
    {
        var texts = Loc.Load(code);

        Assert.NotNull(texts);
        Assert.False(string.IsNullOrWhiteSpace(texts.GetValueOrDefault(Loc.NameKey)), $"{code}.json has no \"{Loc.NameKey}\"");
        var missing = English.Keys.Except(texts.Keys).ToList();
        var extra = texts.Keys.Except(English.Keys).ToList();
        Assert.True(missing.Count == 0, $"{code}.json lacks: {string.Join(", ", missing)}");
        Assert.True(extra.Count == 0, $"{code}.json has keys English doesn't: {string.Join(", ", extra)}");
        Assert.All(texts, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), $"{code}.json: {pair.Key} is empty"));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryTranslation_FillsInTheSamePlaceholders(string code)
    {
        var texts = Loc.Load(code)!;

        foreach (var (key, english) in English)
        {
            if (texts.TryGetValue(key, out var translated))
            {
                Assert.True(
                    Placeholders(english).SetEquals(Placeholders(translated)),
                    $"{code}.json: {key} has {{n}} placeholders that differ from English");
            }
        }
    }

    [Fact]
    public void EveryKeyWrittenInCode_IsInTheEnglishFile()
    {
        var prefixes = English.Keys.Select(key => key.Split('.')[0]).ToHashSet();
        var unknown = new List<string>();
        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
            var keys = file.EndsWith(".xaml", StringComparison.Ordinal)
                ? XamlKey().Matches(text).Select(match => match.Groups[1].Value)
                // Timing probes and logs name things with dots too; they are not texts.
                : text.Split('\n')
                    .Where(line => !line.Contains("PerfProbe.", StringComparison.Ordinal) && !line.Contains("AppLog.", StringComparison.Ordinal))
                    .SelectMany(line => CodeLiteral().Matches(line).Select(match => match.Groups[1].Value))
                    .Where(key => prefixes.Contains(key.Split('.')[0]));
            unknown.AddRange(keys
                .Where(key => !English.ContainsKey(key) && !English.ContainsKey(key + ".one"))
                .Select(key => $"{Path.GetFileName(file)}: {key}"));
        }

        Assert.True(unknown.Count == 0, "Keys not in en.json: " + string.Join(", ", unknown.Distinct()));
    }

    [Fact]
    public void KeysBuiltInCode_AreInTheEnglishFile()
    {
        List<string> keys =
        [
            .. WidgetCatalog.All.SelectMany(widget => new[] { widget.NameKey, widget.BlurbKey }),
            .. WidgetPresets.All.SelectMany(preset => new[] { $"preset.{preset.Id}.name", $"preset.{preset.Id}.description" }),
            .. Enum.GetValues<WidgetCategory>().Select(category => $"nav.section.{WidgetCatalog.HeadingOf(category)}"),
            "nav.section.SETTINGS",
        ];

        Assert.All(keys, key => Assert.True(English.ContainsKey(key), $"en.json lacks {key}"));
    }

    [Theory]
    [InlineData("auto", "es-ES", "es")]
    [InlineData("auto", "es-MX", "es")]
    [InlineData("auto", "fr-FR", "en")]
    [InlineData("auto", "en-US", "en")]
    [InlineData("es", "en-US", "es")]
    [InlineData("en", "es-ES", "en")]
    [InlineData("xx", "es-ES", "es")]
    [InlineData(null, "de-DE", "en")]
    public void Resolve_PicksTheChoice_ThenWindows_ThenEnglish(string? preference, string culture, string expected) =>
        Assert.Equal(expected, Loc.Resolve(preference, CultureInfo.GetCultureInfo(culture)));

    [Fact]
    public void T_UnknownKey_ReadsAsTheKey() => Assert.Equal("no.such.key", Loc.T("no.such.key"));

    [Fact]
    public void Plural_PicksOneOrOther()
    {
        Assert.Equal(English["diagnostics.errors.one"].Replace("{0}", "1"), Loc.Plural("diagnostics.errors", 1));
        Assert.Equal(English["diagnostics.errors.other"].Replace("{0}", "3"), Loc.Plural("diagnostics.errors", 3));
    }

    private static HashSet<string> Placeholders(string text) =>
        Placeholder().Matches(text).Select(match => match.Value).ToHashSet();

    private static IEnumerable<string> SourceFiles()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var app = Path.Combine(directory.FullName, "IRacingOverlay.App");
            if (Directory.Exists(Path.Combine(app, "Localization")))
            {
                return Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories)
                    .Where(file => (file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".xaml", StringComparison.Ordinal))
                                   && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                   && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
            }
        }

        throw new DirectoryNotFoundException("IRacingOverlay.App not found above the test output folder");
    }

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\{loc:T\s+([^}\s]+)\}")]
    private static partial Regex XamlKey();

    // A plain (not interpolated) literal shaped like a key: "word.word[.word…]", but not a file name.
    [GeneratedRegex(@"(?<![\$@\w])""([a-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)*\.(?!json""|txt""|log""|zip"")[A-Za-z0-9]+)""")]
    private static partial Regex CodeLiteral();
}
