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

    public void UpdateState(WeatherState state)
    {
        _last = state;
        Render(animate: true);
    }

    private void Render(bool animate)
    {
        var state = _last;
        var units = Options.Units;
        var temperatureUnit = WeatherState.TemperatureUnit(units);

        AirTempText.Text = state.AirTempDisplay(units);
        AirUnitText.Text = temperatureUnit;
        TrackTempText.Text = state.TrackTempDisplay(units);
        TrackUnitText.Text = temperatureUnit;
        WindSpeedText.Text = state.WindSpeedDisplay(units);
        WindUnitText.Text = WeatherState.WindSpeedUnit(units);
        HumidityText.Text = state.HumidityDisplay;

        ForecastIcon.Condition = state.Condition;
        ForecastIcon.ToolTip = state.ConditionDescription;
        RainChanceText.Text = state.RainChanceDisplay;
        RainChanceText.Foreground = state.RainRisk switch
        {
            RainRisk.Low => StatePalette.Positive,
            RainRisk.Medium => StatePalette.Info,
            RainRisk.High => StatePalette.Critical,
            _ => StatePalette.TextMuted,
        };

        WindDial.ToolTip = state.WindDirectionDescription;
        // A hidden dial isn't animated at all; it snaps into place when it's shown again.
        if (Options.ShowWindArrow)
        {
            WindDial.Point(state.WindFromRelativeDeg, state.HeadingDeg, animate);
        }
    }

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
