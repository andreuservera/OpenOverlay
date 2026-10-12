using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using IRacingOverlay.App.Diagnostics;

namespace IRacingOverlay.App.Localization;

/// <summary>A language the app ships: its code (the file name, "es") and its name in itself ("Español").</summary>
public sealed record LanguageInfo(string Code, string NativeName);

/// <summary>
/// Every text the control panel shows, in the chosen language.
///
/// Each language is one JSON file in Assets/Locales — a flat map from key to text — embedded in the
/// exe and found by name, so adding a language is adding a file and nothing else. English is the
/// source of truth: a key a translation lacks reads in English, and a key nobody has reads as itself,
/// so a missing text shows up on screen instead of as an empty label or a crash.
///
/// Only text changes with the language. Numbers keep being formatted invariantly everywhere, and
/// logs and the diagnostics report stay in English so they can be read by whoever gets them.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    /// <summary>The language every key is written in first, and the fallback for the rest.</summary>
    public const string SourceLanguage = "en";

    /// <summary>What the language preference holds to follow Windows.</summary>
    public const string Automatic = "auto";

    /// <summary>The key, in each file, of the language's name in itself.</summary>
    internal const string NameKey = "_language";

    private const string ResourcePrefix = "OpenOverlay.Locales.";
    private const string ResourceSuffix = ".json";

    private static readonly Dictionary<string, string> Source = Load(SourceLanguage) ?? [];
    private static readonly HashSet<string> ReportedMissing = [];
    private static readonly Dictionary<string, (string Language, IReadOnlyList<string> Texts)> Lists = [];

    private static Dictionary<string, string>? _reverse;
    private static string? _reverseLanguage;

    private Dictionary<string, string> _texts = Source;

    private Loc()
    {
    }

    public static Loc Instance { get; } = new();

    /// <summary>Raised after the language changes, for whatever builds its texts in code.</summary>
    public static event Action? Changed;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The languages embedded in this build, by file name.</summary>
    public static IReadOnlyList<LanguageInfo> Available { get; } = Discover();

    /// <summary>The language in use, as a code from <see cref="Available"/>.</summary>
    public static string Current { get; private set; } = SourceLanguage;

    /// <summary>For XAML bindings (<c>{loc:T key}</c>): the text, in the current language.</summary>
    public string this[string key] => Lookup(key);

    /// <summary>The text for <paramref name="key"/> in the current language.</summary>
    public static string T(string key) => Instance.Lookup(key);

    /// <summary>The text in English whatever the language, for logs and reports.</summary>
    public static string English(string key) => Source.GetValueOrDefault(key) ?? key;

    /// <summary>The text for <paramref name="key"/> with its {0}, {1}… filled in.</summary>
    public static string F(string key, params object?[] args) =>
        string.Format(CultureInfo.InvariantCulture, T(key), args);

    /// <summary>A text shown in the current language, back in English — for what is saved under a
    /// heading's name (a folded section), so it stays put across languages. Text that is not one of
    /// the current language's comes back as is.</summary>
    public static string ToEnglish(string text)
    {
        if (Current == SourceLanguage)
        {
            return text;
        }

        lock (Lists)
        {
            if (_reverse is null || _reverseLanguage != Current)
            {
                _reverse = [];
                foreach (var (key, value) in Instance._texts)
                {
                    _reverse.TryAdd(value, key);
                }

                _reverseLanguage = Current;
            }

            return _reverse.TryGetValue(text, out var found) ? English(found) : text;
        }
    }

    /// <summary>The texts for <paramref name="keys"/>, as one list that stays the same instance until
    /// the language changes — what a ComboBox needs, since a new list drops its selection.</summary>
    public static IReadOnlyList<string> List(params string[] keys)
    {
        var id = string.Join('|', keys);
        lock (Lists)
        {
            if (!Lists.TryGetValue(id, out var cached) || cached.Language != Current)
            {
                cached = (Current, keys.Select(T).ToList());
                Lists[id] = cached;
            }

            return cached.Texts;
        }
    }

    /// <summary><c>key.one</c> for one, <c>key.other</c> otherwise, with the count as {0}.</summary>
    public static string Plural(string key, long count, params object?[] args) =>
        F(count == 1 ? $"{key}.one" : $"{key}.other", [count, .. args]);

    /// <summary>Switches to <paramref name="preference"/> — a language code, or <see cref="Automatic"/>
    /// for the language Windows uses — and refreshes every text bound to it.</summary>
    public static void SetLanguage(string preference)
    {
        var code = Resolve(preference, CultureInfo.CurrentUICulture);
        if (code == Current)
        {
            return;
        }

        Instance._texts = code == SourceLanguage ? Source : Load(code) ?? Source;
        Current = code;
        AppLog.Info("Localization", $"Language set to {code}");
        // "Item[]" is how an indexer announces that every index changed.
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));
        Changed?.Invoke();
    }

    /// <summary>The language a preference means on a machine running <paramref name="culture"/>: the
    /// preference itself if it is a language this build has, else Windows' language if it has that,
    /// else English.</summary>
    internal static string Resolve(string? preference, CultureInfo culture)
    {
        if (preference is not null && preference != Automatic && Has(preference))
        {
            return preference;
        }

        var windows = culture.TwoLetterISOLanguageName;
        return Has(windows) ? windows : SourceLanguage;
    }

    /// <summary>A language file's texts, or null if this build has no such language.</summary>
    internal static Dictionary<string, string>? Load(string code)
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream(ResourcePrefix + code + ResourceSuffix);
        if (stream is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
        }
        catch (JsonException e)
        {
            AppLog.Error("Localization", $"Language file {code} is not valid JSON", e);
            return null;
        }
    }

    private static bool Has(string code) => Available.Any(language => language.Code == code);

    private static List<LanguageInfo> Discover() =>
        typeof(Loc).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            .Select(name => name[ResourcePrefix.Length..^ResourceSuffix.Length])
            .Select(code => new LanguageInfo(code, (code == SourceLanguage ? Source : Load(code))?.GetValueOrDefault(NameKey) ?? code))
            .OrderBy(language => language.Code != SourceLanguage)
            .ThenBy(language => language.NativeName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private string Lookup(string key)
    {
        if (_texts.TryGetValue(key, out var text) || Source.TryGetValue(key, out text))
        {
            return text;
        }

        lock (ReportedMissing)
        {
            if (ReportedMissing.Add(key))
            {
                AppLog.Warn("Localization", $"No text for key {key}");
            }
        }

        return key;
    }
}
