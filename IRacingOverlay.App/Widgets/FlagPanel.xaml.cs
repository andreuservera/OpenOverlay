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
/// doesn't collapse to a single flag. Each box's visual style (checkered pattern, meatball dot, the
/// debris flag's red/yellow diagonal stripes, the blue flag's orange diagonal stripe) matches how
/// that flag actually looks in real racing / iRacing's own flag icons, not just a solid color swatch.
/// </summary>
public partial class FlagPanel : UserControl
{
    private static readonly Color OrangeAccent = Color.FromRgb(0xFF, 0x8C, 0x1A);
    private static readonly Color DebrisYellow = Color.FromRgb(0xE8, 0xC0, 0x00);
    private static readonly Color DebrisRed = Color.FromRgb(0xCC, 0x14, 0x14);

    public FlagPanel()
    {
        InitializeComponent();
    }

    // Purely a dumb renderer: draws exactly the list it's given, in order, with no "what if it's
    // empty" policy of its own — FlagWidget and DashboardWindow each decide when/whether a
    // placeholder "None" entry belongs in that list, since only they know their own edit-mode
    // context (see FlagWidget.Render for why that matters).
    public void UpdateState(IReadOnlyList<FlagState> flags)
    {
        FlagsStack.Children.Clear();
        foreach (var flag in flags)
        {
            FlagsStack.Children.Add(BuildBox(flag));
        }
    }

    private static Border BuildBox(FlagState state)
    {
        // The overlay Grid is what actually gets clipped: it, not the outer Border, is what the
        // rotated blue stripe would otherwise spill out of once the box is narrower than the
        // stripe's fixed 280px design width (e.g. inside the dashboard's 280px-wide flag column).
        var overlay = new Grid { ClipToBounds = true };

        if (state.Style == FlagVisualStyle.BlueWithOrangeStripe)
        {
            overlay.Children.Add(new Rectangle
            {
                Fill = new SolidColorBrush(OrangeAccent),
                Width = 280,
                Height = 34,
                RenderTransform = new RotateTransform(-25),
                RenderTransformOrigin = new Point(0.5, 0.5),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        // DockPanel instead of a horizontal StackPanel: a StackPanel measures its stacking-direction
        // children with infinite available width, so TextWrapping never actually kicks in and long
        // labels ("BLUE — CAR BEHIND") get cut off at the container edge. DockPanel's non-docked
        // (filling) child gets a real, finite measured width, so wrapping works.
        var content = new DockPanel
        {
            LastChildFill = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (state.Style == FlagVisualStyle.Meatball)
        {
            var dot = new Ellipse
            {
                Width = 24,
                Height = 24,
                Fill = new SolidColorBrush(OrangeAccent),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(dot, Dock.Left);
            content.Children.Add(dot);
        }

        if (state.Style == FlagVisualStyle.Checkered)
        {
            var checker = BuildCheckerPattern();
            DockPanel.SetDock(checker, Dock.Left);
            content.Children.Add(checker);
        }

        var text = new TextBlock
        {
            Text = state.Name,
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = ParseBrush(state.ForegroundColor),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (state.Style == FlagVisualStyle.DebrisStripes)
        {
            // The striped background clashes with plain text in either color, so give the label its
            // own solid backing plate rather than fight for contrast against alternating red/yellow.
            text.Foreground = Brushes.White;
            content.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 3),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = text,
            });
        }
        else
        {
            content.Children.Add(text);
        }

        overlay.Children.Add(content);

        var background = state.Style == FlagVisualStyle.DebrisStripes
            ? BuildDiagonalStripeBrush(DebrisYellow, DebrisRed)
            : ParseBrush(state.BackgroundColor);

        var box = new Border
        {
            Background = background,
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 6),
            Child = overlay,
        };

        // Flag colors are semantic (must match iRacing's real flag colors regardless of theme) so
        // Background stays hardcoded above, but the box's chrome — border and corner style — follows
        // the Dashboard theme same as every other panel (SetResourceReference is the code-behind
        // equivalent of a XAML DynamicResource binding).
        box.SetResourceReference(Border.BorderBrushProperty, "Theme.PanelBorder");
        box.SetResourceReference(Border.BorderThicknessProperty, "Theme.PanelBorderThickness");
        box.SetResourceReference(Border.CornerRadiusProperty, "Theme.PanelCornerRadius");

        return box;
    }

    private static UniformGrid BuildCheckerPattern()
    {
        var checker = new UniformGrid { Rows = 4, Columns = 4, Width = 48, Height = 48, Margin = new Thickness(0, 0, 12, 0) };
        for (var i = 0; i < 16; i++)
        {
            var row = i / 4;
            var col = i % 4;
            var isBlack = (row + col) % 2 == 0;
            checker.Children.Add(new Rectangle { Fill = isBlack ? Brushes.Black : Brushes.White });
        }

        return checker;
    }

    /// <summary>Repeating diagonal stripes (the real-world "surface"/debris flag pattern) via a
    /// short-vector LinearGradientBrush with SpreadMethod=Repeat — the standard WPF technique for a
    /// tiled diagonal stripe, without needing an image asset.</summary>
    private static Brush BuildDiagonalStripeBrush(Color a, Color b)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0.18, 0.18),
            SpreadMethod = GradientSpreadMethod.Repeat,
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
        };
        brush.GradientStops.Add(new GradientStop(a, 0.0));
        brush.GradientStops.Add(new GradientStop(a, 0.5));
        brush.GradientStops.Add(new GradientStop(b, 0.5));
        brush.GradientStops.Add(new GradientStop(b, 1.0));
        return brush;
    }

    private static Brush ParseBrush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
}
