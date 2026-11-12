using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace IRacingOverlay.Coaching.Ui;

/// <summary>
/// The Coaching page's texts, in the app's language. The texts live in the app's language files
/// (keys starting "coaching."), like every other text of the app; the host hands this class its
/// lookup and says when the language changes (<see cref="Use"/>, <see cref="Changed"/>), so this
/// assembly needs nothing from the app's. Without a host a key reads as itself.
/// </summary>
public sealed class CoachingText : INotifyPropertyChanged
{
    private Func<string, string>? _lookup;

    private CoachingText()
    {
    }

    public static CoachingText Instance { get; } = new();

    /// <summary>Raised after the language changed, once the bound texts have been told.</summary>
    public static event Action? LanguageChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The language's culture, for dates.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.InvariantCulture;

    /// <summary>A text by key, for bindings (<c>{loc:T coaching.refresh}</c>).</summary>
    public string this[string key] => T(key);

    /// <summary>Sets the lookup (key → text in the current language; an unknown key comes back as
    /// itself) and the language code ("es", "en"…).</summary>
    public static void Use(Func<string, string> lookup, string language)
    {
        Instance._lookup = lookup;
        Culture = CultureFor(language);
    }

    /// <summary>The language changed: every bound text and the open page are worded again.</summary>
    public static void Changed(string language)
    {
        Culture = CultureFor(language);
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke();
    }

    public static string T(string key) => Instance._lookup?.Invoke(key) ?? key;

    /// <summary>The text for <paramref name="key"/>, or null when the language files don't have it.</summary>
    public static string? TryT(string key)
    {
        var text = T(key);
        return text == key ? null : text;
    }

    /// <summary>The text with its {0}, {1}… filled in; numbers are formatted by the caller.</summary>
    public static string F(string key, params object?[] args) => string.Format(CultureInfo.InvariantCulture, T(key), args);

    private static CultureInfo CultureFor(string language)
    {
        try
        {
            return CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}

/// <summary><c>Text="{loc:T coaching.refresh}"</c>: a Coaching page text, bound so it follows the language.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = CoachingText.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
