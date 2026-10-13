using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class TrackInfoPanel : UserControl
{
    private static readonly Brush UsageEmpty = StatePalette.TrackEmpty;
    private static readonly Brush UsageClean = StatePalette.Info;
    private static readonly Brush UsageLow = StatePalette.Positive;
    private static readonly Brush UsageMedium = StatePalette.Warning;
    private static readonly Brush UsageHigh = StatePalette.Critical;

    // Every reading is a cell: 12 either side, 7 above and below, a 1px rule on its left.
    private static readonly Thickness CellPadding = new(12, 7, 12, 7);

    // Swapping in the persisted instance is a DependencyProperty change, and the bar is laid out
    // again for it.
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(TrackInfoOptions), typeof(TrackInfoPanel),
        new PropertyMetadata(new TrackInfoOptions(), (d, e) => ((TrackInfoPanel)d).OnOptionsChanged(e)));

    private readonly Dictionary<TrackInfoField, FrameworkElement> _fields;

    public TrackInfoPanel()
    {
        InitializeComponent();
        _fields = new Dictionary<TrackInfoField, FrameworkElement>
        {
            [TrackInfoField.TrackName] = FieldTrackName,
            [TrackInfoField.Session] = SessionLabelText,
            [TrackInfoField.AirTemp] = FieldAirTemp,
            [TrackInfoField.TrackTemp] = FieldTrackTemp,
            [TrackInfoField.Wind] = FieldWind,
            [TrackInfoField.Humidity] = FieldHumidity,
            [TrackInfoField.TrackUsage] = FieldTrackUsage,
            [TrackInfoField.TimeLeft] = FieldTimeLeft,
            [TrackInfoField.Lap] = FieldLap,
        };
        PropertyChangedEventManager.AddHandler(Options, OnOptionChanged, string.Empty);
        LayoutBar();
        Loaded += (_, _) => HoldWidths();
    }

    /// <summary>
    /// The readings that change while driving, each with its widest possible value in either unit
    /// system. Each keeps that width whatever it shows, so the bar never changes size as a decimal
    /// or a digit comes and goes (22°C ↔ 22.5°C, 10:00 → 9:59, E ↔ WNW) — a widget that grows and
    /// shrinks moves everything after it, and can't be placed against anything else on screen. The
    /// dashes shown without data get the same room, so the widget is the same size either way.
    /// </summary>
    private (TextBlock Text, string[] Widest)[] HeldReadings =>
    [
        (AirTempText, ["-88.8°F", "-88.8°C"]),
        (TrackTempText, ["-88.8°F", "-88.8°C"]),
        (WindText, ["88.8 km/h WNW", "88.8 mph WNW"]),
        (HumidityText, ["100%"]),
        (TrackUsageText, ["MODERATELY HIGH"]),
        (TimeRemainingText, ["8:88:88"]),
        (LapText, ["88/~88.8", "888/888"]),
    ];

    private void HoldWidths()
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var (text, widest) in HeldReadings)
        {
            var typeface = new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch);
            text.MinWidth = widest.Max(sample => new FormattedText(
                sample, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, text.FontSize, Brushes.White, pixelsPerDip)
                .WidthIncludingTrailingWhitespace);
        }
    }

    public TrackInfoOptions Options
    {
        get => (TrackInfoOptions)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    /// <summary>Puts the visible fields into the bar in the options' order, with a separator
    /// wherever the group changes (see <see cref="TrackInfoOptions.Bar"/>).</summary>
    private void LayoutBar()
    {
        Bar.Children.Clear();
        foreach (var (field, _) in Options.Bar())
        {
            if (Bar.Children.Count > 0)
            {
                Bar.Children.Add(new Rectangle { Style = (Style)FindResource("BarSeparator") });
            }

            var element = _fields[field];
            element.Margin = CellPadding;
            Bar.Children.Add(element);
        }
    }

    private void OnOptionsChanged(DependencyPropertyChangedEventArgs e)
    {
        // Weak, so a thrown-away preview panel isn't kept alive by the long-lived shared options.
        if (e.OldValue is TrackInfoOptions old)
        {
            PropertyChangedEventManager.RemoveHandler(old, OnOptionChanged, string.Empty);
        }

        if (e.NewValue is TrackInfoOptions current)
        {
            PropertyChangedEventManager.AddHandler(current, OnOptionChanged, string.Empty);
        }

        LayoutBar();
    }

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e) => LayoutBar();

    public void UpdateState(TrackInfoState state)
    {
        TrackNameText.Text = state.TrackNameDisplay;
        SessionLabelText.Text = state.SessionLabelDisplay;
        AirTempText.Text = state.AirTempDisplay;
        SkyIcon.Condition = state.Condition;
        TrackTempText.Text = state.TrackTempDisplay;
        WindText.Text = state.WindDisplay;
        HumidityText.Text = state.HumidityDisplay;
        TrackUsageText.Text = state.TrackUsageDisplay;
        SetUsageBar(state.TrackUsageLevel);
        TimeRemainingText.Text = state.TimeRemainingDisplay;
        LapText.Text = state.LapDisplay;
    }

    private void SetUsageBar(int? level)
    {
        // Unknown states leave every segment dark; "clean" is level 0 and still lights the first one.
        var filled = level is { } value ? value + 1 : 0;
        var fill = level switch
        {
            <= 1 => UsageClean,
            <= 3 => UsageLow,
            <= 5 => UsageMedium,
            _ => UsageHigh,
        };

        for (var i = 0; i < TrackUsageBar.Children.Count; i++)
        {
            ((Rectangle)TrackUsageBar.Children[i]).Fill = i < filled ? fill : UsageEmpty;
        }
    }
}
