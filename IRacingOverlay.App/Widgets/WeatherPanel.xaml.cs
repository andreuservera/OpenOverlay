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
        var rain = state.RainChancePct is { } pct ? (int)Math.Round(Math.Clamp(pct, 0, 100)) : (int?)null;
        RainChanceText.Foreground = rain is { } level ? RainText[level] : StatePalette.TextMuted;
        RainFill.Fill = rain is { } fill ? RainBar[fill] : StatePalette.TextMuted;
        RainFillScale.ScaleX = (rain ?? 0) / 100.0;
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

    // Rain chance runs from sunshine to rain: yellow when dry, a pale sky in the uncertain middle,
    // blue when it's coming. Every stop clears 4.5:1 against the panel, so the number stays legible.
    private static readonly Color RainNone = Color.FromRgb(0xFF, 0xD2, 0x4D);
    private static readonly Color RainMaybe = Color.FromRgb(0xA8, 0xC8, 0xD8);
    private static readonly Color RainCertain = Color.FromRgb(0x3D, 0x8B, 0xFF);

    private static readonly Brush[] RainText = Enumerable.Range(0, 101).Select(p => Freeze(new SolidColorBrush(RainColor(p / 100.0)))).ToArray();

    // The bar shows the scale it sits on: yellow at its start, the reading's own colour at its end.
    private static readonly Brush[] RainBar = Enumerable.Range(0, 101)
        .Select(p => Freeze(new LinearGradientBrush(RainNone, RainColor(p / 100.0), 0)))
        .ToArray();

    /// <summary>The rain-chance colour for a 0–1 probability.</summary>
    internal static Color RainColor(double fraction)
    {
        var t = Math.Clamp(fraction, 0, 1);
        return t < 0.5 ? Blend(RainNone, RainMaybe, t / 0.5) : Blend(RainMaybe, RainCertain, (t - 0.5) / 0.5);
    }

    private static Color Blend(Color from, Color to, double t) => Color.FromRgb(
        (byte)Math.Round(from.R + ((to.R - from.R) * t)),
        (byte)Math.Round(from.G + ((to.G - from.G) * t)),
        (byte)Math.Round(from.B + ((to.B - from.B) * t)));

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
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
