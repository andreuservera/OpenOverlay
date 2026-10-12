using System.Windows;
using System.Windows.Controls;

namespace IRacingOverlay.App.ControlPanel;

public partial class PreviewBackdrop : UserControl
{
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(PreviewBackdropMode), typeof(PreviewBackdrop),
        new PropertyMetadata(PreviewBackdropMode.Circuit, (d, _) => ((PreviewBackdrop)d).ApplyMode()));

    public PreviewBackdrop()
    {
        InitializeComponent();
    }

    /// <summary>What the widget is shown over: the circuit, a bright sky, or a checkerboard.</summary>
    public PreviewBackdropMode Mode
    {
        get => (PreviewBackdropMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    private void ApplyMode()
    {
        Sky.Visibility = Mode == PreviewBackdropMode.Sky ? Visibility.Visible : Visibility.Collapsed;
        Checkerboard.Visibility = Mode == PreviewBackdropMode.Checkerboard ? Visibility.Visible : Visibility.Collapsed;
    }
}
