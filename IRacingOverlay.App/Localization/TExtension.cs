using System.Windows.Data;
using System.Windows.Markup;

namespace IRacingOverlay.App.Localization;

/// <summary>
/// <c>Text="{loc:T search.placeholder}"</c>: a text from the language files, bound rather than looked
/// up once, so it changes the moment the language does.
/// </summary>
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
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
