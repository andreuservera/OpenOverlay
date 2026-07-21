using System.Windows.Controls;
using System.Windows.Media;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class DeltaPanel : UserControl
{
    private static readonly Brush FasterBrush = new SolidColorBrush(Color.FromRgb(0x30, 0xC0, 0x30));
    private static readonly Brush SlowerBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x30, 0x30));
    private static readonly Brush NeutralBrush = Brushes.White;

    public DeltaPanel()
    {
        InitializeComponent();
    }

    public void UpdateState(DeltaState state)
    {
        LabelText.Text = state.ReferenceLabel;
        DeltaText.Text = state.Display;
        DeltaText.Foreground = !state.IsValid ? NeutralBrush : state.DeltaSeconds <= 0 ? FasterBrush : SlowerBrush;
    }
}
