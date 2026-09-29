using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IRacingOverlay.App.ViewModels;

public enum WeatherUnits
{
    Metric,
    Imperial,
}

public enum WeatherGraphicSize
{
    Small,
    Medium,
    Large,
}

/// <summary>
/// What the Weather widget shows and how. Every element toggles independently; the section and
/// divider flags are derived from them, so the layout closes up around whatever is hidden.
/// </summary>
public sealed class WeatherOptions : INotifyPropertyChanged
{
    private static readonly string[] Derived =
    [
        nameof(ShowTemperatures), nameof(ShowWindSection), nameof(ShowForecastSection),
        nameof(ShowFirstDivider), nameof(ShowSecondDivider),
    ];

    private bool _showAirTemp = true;
    private bool _showTrackTemp = true;
    private bool _showHumidity = true;
    private bool _showWindSpeed = true;
    private bool _showWindArrow = true;
    private bool _showForecast = true;
    private bool _showRainProbability = true;
    private bool _showLabels = true;
    private bool _compact;
    private WeatherUnits _units;
    private WeatherGraphicSize _iconSize = WeatherGraphicSize.Medium;
    private WeatherGraphicSize _arrowSize = WeatherGraphicSize.Medium;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool ShowAirTemp { get => _showAirTemp; set => Set(ref _showAirTemp, value); }

    public bool ShowTrackTemp { get => _showTrackTemp; set => Set(ref _showTrackTemp, value); }

    public bool ShowHumidity { get => _showHumidity; set => Set(ref _showHumidity, value); }

    public bool ShowWindSpeed { get => _showWindSpeed; set => Set(ref _showWindSpeed, value); }

    public bool ShowWindArrow { get => _showWindArrow; set => Set(ref _showWindArrow, value); }

    public bool ShowForecast { get => _showForecast; set => Set(ref _showForecast, value); }

    public bool ShowRainProbability { get => _showRainProbability; set => Set(ref _showRainProbability, value); }

    public bool ShowLabels { get => _showLabels; set => Set(ref _showLabels, value); }

    /// <summary>One horizontal strip instead of stacked sections.</summary>
    public bool Compact { get => _compact; set => Set(ref _compact, value); }

    public WeatherUnits Units { get => _units; set => Set(ref _units, value); }

    public WeatherGraphicSize IconSize
    {
        get => _iconSize;
        set
        {
            if (Set(ref _iconSize, value))
            {
                OnPropertyChanged(nameof(IconPixels));
            }
        }
    }

    public WeatherGraphicSize ArrowSize
    {
        get => _arrowSize;
        set
        {
            if (Set(ref _arrowSize, value))
            {
                OnPropertyChanged(nameof(ArrowPixels));
            }
        }
    }

    public double IconPixels => _iconSize switch
    {
        WeatherGraphicSize.Small => 36,
        WeatherGraphicSize.Large => 64,
        _ => 48,
    };

    public double ArrowPixels => _arrowSize switch
    {
        WeatherGraphicSize.Small => 46,
        WeatherGraphicSize.Large => 76,
        _ => 58,
    };

    public bool ShowTemperatures => ShowAirTemp || ShowTrackTemp;

    public bool ShowWindSection => ShowWindArrow || ShowWindSpeed || ShowHumidity;

    public bool ShowForecastSection => ShowForecast || ShowRainProbability;

    public bool ShowFirstDivider => ShowTemperatures && (ShowWindSection || ShowForecastSection);

    public bool ShowSecondDivider => ShowWindSection && ShowForecastSection;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        foreach (var derived in Derived)
        {
            OnPropertyChanged(derived);
        }

        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
