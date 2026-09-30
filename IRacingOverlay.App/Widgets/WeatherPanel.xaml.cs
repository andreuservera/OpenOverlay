using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

/// <summary>Live weather: temperatures, relative wind, humidity and the sky. Same API for the
/// floating widget and the control-panel preview.</summary>
public partial class WeatherPanel : UserControl
{
    // Same reasoning as FuelCalculatorPanel: the XAML bindings latch onto whatever object this
    // returns, so swapping in the persisted instance has to be a DependencyProperty change.
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(WeatherOptions), typeof(WeatherPanel),
        new PropertyMetadata(new WeatherOptions(), (d, e) => ((WeatherPanel)d).OnOptionsChanged(e)));

    private WeatherState _last = WeatherState.Empty;
    private long _nextCompassMs;

    public WeatherPanel()
    {
        InitializeComponent();
        PropertyChangedEventManager.AddHandler(Options, OnOptionChanged, string.Empty);
        Render(animate: false);
    }

    public WeatherOptions Options
    {
        get => (WeatherOptions)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    /// <summary>Minimum time between wind-dial redraws on live updates; 0 = every update, animated.</summary>
    public int CompassIntervalMs { get; set; }

    public void UpdateState(WeatherState state)
    {
        _last = state;
        Render(animate: true);
    }

    private void Render(bool animate)
    {
        var state = _last;

        AirTempText.Text = state.AirTempDisplay;
        AirUnitText.Text = state.TemperatureUnit;
        TrackTempText.Text = state.TrackTempDisplay;
        TrackUnitText.Text = state.TemperatureUnit;
        WindSpeedText.Text = state.WindSpeedDisplay;
        WindUnitText.Text = state.WindSpeedUnit;
        HumidityText.Text = state.HumidityDisplay;

        ForecastIcon.Condition = state.Condition;
        ForecastIcon.ToolTip = state.ConditionDescription;
        ConditionText.Text = state.ConditionLabel;
        RainChanceText.Text = state.RainChanceDisplay;
        var rainBrush = RiskBrush(state.RainRisk);
        RainChanceText.Foreground = rainBrush;
        RainFill.Fill = rainBrush;
        RainFillScale.ScaleX = Math.Clamp((state.RainChancePct ?? 0) / 100, 0, 1);
        WetnessText.Text = state.TrackWetnessDisplay;
        WetnessText.Foreground = RiskBrush(state.TrackWetnessRisk);

        WindDial.ToolTip = state.WindDirectionDescription;
        // A hidden dial isn't animated at all; it snaps into place when it's shown again.
        if (!Options.ShowWindArrow)
        {
            return;
        }

        if (animate && CompassIntervalMs > 0)
        {
            // Half a 10 Hz tick of slack, so 500 ms lands on every 5th tick despite timer jitter.
            var now = Environment.TickCount64;
            if (now < _nextCompassMs - 50)
            {
                return;
            }

            _nextCompassMs = now + CompassIntervalMs;
            animate = false;
        }

        WindDial.Point(state.WindFromRelativeDeg, state.HeadingDeg, animate);
    }

    private static Brush RiskBrush(RainRisk? risk) => risk switch
    {
        RainRisk.Low => StatePalette.Positive,
        RainRisk.Medium => StatePalette.Info,
        RainRisk.High => StatePalette.Critical,
        _ => StatePalette.TextMuted,
    };

    private void OnOptionsChanged(DependencyPropertyChangedEventArgs e)
    {
        // Weak, so a thrown-away preview panel isn't kept alive by the long-lived shared options.
        if (e.OldValue is WeatherOptions old)
        {
            PropertyChangedEventManager.RemoveHandler(old, OnOptionChanged, string.Empty);
        }

        if (e.NewValue is WeatherOptions current)
        {
            PropertyChangedEventManager.AddHandler(current, OnOptionChanged, string.Empty);
        }

        Render(animate: false);
    }

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e) => Render(animate: false);
}
