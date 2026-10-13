using System.Windows;
using System.Windows.Controls;

namespace IRacingOverlay.App.Overlay;

/// <summary>
/// The frame every widget sits in (docs/widgets-style.md §3): the panel background, its 1px edge
/// and 8px corners, and an optional header strip with the widget's name on the left and the live
/// dot on the right. Its look lives in Themes/DesignTokens.xaml; panels use it in place of a
/// <c>Border</c> styled <c>OverlayPanel</c>, and their <c>Padding</c> pads the content under the
/// header.
///
/// Whether the header shows is the widget's own setting (<see cref="WidgetHeaderStore"/>), set once
/// on the window — or on whatever presenter a preview draws the panel in — through the inherited
/// <see cref="ShowHeaderProperty"/>, so a panel never has to know where it is being drawn. A frame
/// without a <see cref="Title"/> never shows one (Track &amp; session is a single bar).
/// </summary>
public sealed class WidgetFrame : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(WidgetFrame), new PropertyMetadata(null));

    /// <summary>The widget's name, drawn in capitals in the header.</summary>
    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty ShowHeaderProperty = DependencyProperty.RegisterAttached(
        "ShowHeader", typeof(bool), typeof(WidgetFrame),
        new FrameworkPropertyMetadata(WidgetHeaderStore.Default, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetShowHeader(DependencyObject element) => (bool)element.GetValue(ShowHeaderProperty);

    public static void SetShowHeader(DependencyObject element, bool value) => element.SetValue(ShowHeaderProperty, value);
}
