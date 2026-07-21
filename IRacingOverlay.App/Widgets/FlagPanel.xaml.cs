using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

/// <summary>
/// Renders however many flags are simultaneously active as a stack of boxes — iRacing's own flag
/// panel can show more than one at once (e.g. a caution alongside a blue "car behind" call), so this
/// doesn't collapse to a single flag.
/// </summary>
public partial class FlagPanel : UserControl
{
    public FlagPanel()
    {
        InitializeComponent();
    }

    public void UpdateState(IReadOnlyList<FlagState> flags)
    {
        FlagsStack.Children.Clear();

        if (flags.Count == 0)
        {
            FlagsStack.Children.Add(BuildBox(FlagState.None));
            return;
        }

        foreach (var flag in flags)
        {
            FlagsStack.Children.Add(BuildBox(flag));
        }
    }

    private static Border BuildBox(FlagState state)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        if (state.IsMeatball)
        {
            content.Children.Add(new Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x1A)),
                Margin = new Thickness(0, 0, 8, 0),
            });
        }

        if (state.IsCheckered)
        {
            content.Children.Add(BuildCheckerPattern());
        }

        content.Children.Add(new TextBlock
        {
            Text = state.Name,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = ParseBrush(state.ForegroundColor),
            VerticalAlignment = VerticalAlignment.Center,
        });

        return new Border
        {
            Background = ParseBrush(state.BackgroundColor),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 4),
            Child = content,
        };
    }

    private static UniformGrid BuildCheckerPattern()
    {
        var checker = new UniformGrid { Rows = 4, Columns = 4, Width = 32, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
        for (var i = 0; i < 16; i++)
        {
            var row = i / 4;
            var col = i % 4;
            var isBlack = (row + col) % 2 == 0;
            checker.Children.Add(new Rectangle { Fill = isBlack ? Brushes.Black : Brushes.White });
        }

        return checker;
    }

    private static Brush ParseBrush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
}
