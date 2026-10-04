using System.ComponentModel;
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

    // Gaps between neighbouring fields: the track name and session sit close, like a title; every
    // other pair of fields a little further apart; a separator gets room on both sides.
    private const double HeaderGap = 11;
    private const double FieldGap = 21;
    private const double SeparatorGap = 18;

    // Swapping in the persisted instance is a DependencyProperty change, and the bar is laid out
    // again for it.
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(TrackInfoOptions), typeof(TrackInfoPanel),
        new PropertyMetadata(new TrackInfoOptions(), (d, e) => ((TrackInfoPanel)d).OnOptionsChanged(e)));

    private readonly Dictionary<TrackInfoField, FrameworkElement> _fields;
    private readonly Dictionary<FrameworkElement, Thickness> _baseMargins = [];

    public TrackInfoPanel()
    {
        InitializeComponent();
        _fields = new Dictionary<TrackInfoField, FrameworkElement>
        {
            [TrackInfoField.TrackName] = TrackNameText,
            [TrackInfoField.Session] = SessionLabelText,
            [TrackInfoField.AirTemp] = FieldAirTemp,
            [TrackInfoField.TrackTemp] = FieldTrackTemp,
            [TrackInfoField.Wind] = FieldWind,
            [TrackInfoField.Humidity] = FieldHumidity,
            [TrackInfoField.TrackUsage] = FieldTrackUsage,
            [TrackInfoField.TimeLeft] = FieldTimeLeft,
            [TrackInfoField.Lap] = FieldLap,
        };
        foreach (var element in _fields.Values)
        {
            _baseMargins[element] = element.Margin;
        }

        PropertyChangedEventManager.AddHandler(Options, OnOptionChanged, string.Empty);
        LayoutBar();
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
        var first = true;
        foreach (var (field, separatorBefore) in Options.Bar())
        {
            var element = _fields[field];
            var gap = 0.0;
            if (separatorBefore)
            {
                Bar.Children.Add(new Rectangle { Style = (Style)FindResource("BarSeparator"), Margin = new Thickness(SeparatorGap, 2, 0, 2) });
                gap = SeparatorGap;
            }
            else if (!first)
            {
                gap = TrackInfoOptions.GroupOf(field) == TrackInfoGroup.Header ? HeaderGap : FieldGap;
            }

            var margin = _baseMargins[element];
            element.Margin = new Thickness(margin.Left + gap, margin.Top, margin.Right, margin.Bottom);
            Bar.Children.Add(element);
            first = false;
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
