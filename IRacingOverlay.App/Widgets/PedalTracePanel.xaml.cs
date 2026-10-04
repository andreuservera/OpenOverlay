using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class PedalTracePanel : UserControl
{
    private const double BlockGap = 14;

    // Swapping in the persisted instance is a DependencyProperty change, and the strip is laid out
    // again for it.
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(PedalTraceOptions), typeof(PedalTracePanel),
        new PropertyMetadata(new PedalTraceOptions(), (d, e) => ((PedalTracePanel)d).OnOptionsChanged(e)));

    public static readonly DependencyProperty TraceMinWidthProperty = DependencyProperty.Register(
        nameof(TraceMinWidth), typeof(double), typeof(PedalTracePanel), new PropertyMetadata(300.0));

    private readonly Dictionary<PedalTraceElement, FrameworkElement> _blocks;

    public PedalTracePanel()
    {
        InitializeComponent();
        _blocks = new Dictionary<PedalTraceElement, FrameworkElement>
        {
            [PedalTraceElement.Gear] = GearBlock,
            [PedalTraceElement.Speed] = SpeedBlock,
            [PedalTraceElement.Steering] = SteeringBlock,
            [PedalTraceElement.Trace] = TraceBlock,
            [PedalTraceElement.Pedals] = PedalsBlock,
        };
        PropertyChangedEventManager.AddHandler(Options, OnOptionChanged, string.Empty);
        LayoutStrip();
    }

    public PedalTraceOptions Options
    {
        get => (PedalTraceOptions)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    /// <summary>The trace's own width, which with the other blocks' gives the widget its width:
    /// hiding a block narrows the widget rather than stretching the trace.</summary>
    public double TraceMinWidth
    {
        get => (double)GetValue(TraceMinWidthProperty);
        set => SetValue(TraceMinWidthProperty, value);
    }

    public void UpdateState(PedalTraceState state)
    {
        SetBar(ThrottleFill, state.Throttle);
        SetBar(BrakeFill, state.Brake);
        SetBar(ClutchFill, state.Clutch);
        SetText(ThrottleText, PedalTraceState.Percent(state.Throttle));
        SetText(BrakeText, PedalTraceState.Percent(state.Brake));
        SetText(ClutchText, PedalTraceState.Percent(state.Clutch));
        SetText(GearBlock, state.Gear);
        SetText(SpeedText, state.SpeedDisplay);
        SetText(SpeedUnitText, state.SpeedUnitDisplay);
        WheelRotation.Angle = state.SteeringIconAngle;
        Trace.SetTrace(state);
    }

    /// <summary>Puts the visible blocks into the strip in the options' order; the trace's column
    /// stretches, every other block keeps its own width.</summary>
    private void LayoutStrip()
    {
        Strip.ColumnDefinitions.Clear();
        foreach (var block in _blocks.Values)
        {
            block.Visibility = Visibility.Collapsed;
        }

        var column = 0;
        foreach (var element in Options.Strip())
        {
            var block = _blocks[element];
            Strip.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = element == PedalTraceElement.Trace ? new GridLength(1, GridUnitType.Star) : GridLength.Auto,
            });
            Grid.SetColumn(block, column);
            block.Margin = new Thickness(column == 0 ? 0 : BlockGap, 0, 0, 0);
            block.Visibility = Visibility.Visible;
            column++;
        }
    }

    private void OnOptionsChanged(DependencyPropertyChangedEventArgs e)
    {
        // Weak, so a thrown-away preview panel isn't kept alive by the long-lived shared options.
        if (e.OldValue is PedalTraceOptions old)
        {
            PropertyChangedEventManager.RemoveHandler(old, OnOptionChanged, string.Empty);
        }

        if (e.NewValue is PedalTraceOptions current)
        {
            PropertyChangedEventManager.AddHandler(current, OnOptionChanged, string.Empty);
        }

        LayoutStrip();
    }

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e) => LayoutStrip();

    // Scaled rather than resized, so a pedal moving every frame never triggers a layout pass.
    private static void SetBar(Rectangle fill, double value) =>
        ((ScaleTransform)fill.RenderTransform).ScaleY = Math.Clamp(value, 0, 1);

    // Only when it changes: the widget updates at the high rate, and a new Text measures again.
    private static void SetText(TextBlock block, string text)
    {
        if (block.Text != text)
        {
            block.Text = text;
        }
    }
}
