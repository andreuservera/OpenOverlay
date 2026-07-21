using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class FlagPanel : UserControl
{
    public FlagPanel()
    {
        InitializeComponent();

        for (var i = 0; i < 16; i++)
        {
            var row = i / 4;
            var col = i % 4;
            var isBlack = (row + col) % 2 == 0;
            CheckerPattern.Children.Add(new Rectangle { Fill = isBlack ? Brushes.Black : Brushes.White });
        }
    }

    public void UpdateState(FlagState state)
    {
        FlagBorder.Background = ParseBrush(state.BackgroundColor);
        FlagText.Foreground = ParseBrush(state.ForegroundColor);
        FlagText.Text = state.Name;
        MeatballDot.Visibility = state.IsMeatball ? Visibility.Visible : Visibility.Collapsed;
        CheckerPattern.Visibility = state.IsCheckered ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Brush ParseBrush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
}
