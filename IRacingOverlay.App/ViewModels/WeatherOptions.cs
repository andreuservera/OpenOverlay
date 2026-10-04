using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IRacingOverlay.App.ViewModels;

public enum WeatherGraphicSize
{
    Small,
    Medium,
    Large,
}

/// <summary>
/// What the Weather widget shows and how. Every element toggles independently; the card flags are
/// derived from them, so a card disappears once everything in it is hidden.
/// </summary>
public sealed class WeatherOptions : INotifyPropertyChanged
{
    private static readonly string[] Derived =
    [
        nameof(ShowTrackCard), nameof(ShowAirCard), nameof(ShowRainCard), nameof(ShowWindCard),
    ];

    private bool _showAirTemp = true;
    private bool _showTrackTemp = true;
    private bool _showHumidity = true;
    private bool _showWindSpeed = true;
    private bool _showWindArrow = true;
    private bool _showForecast = true;
    private bool _showRainProbability = true;
    private bool _showTrackWetness = true;
    private bool _showLabels = true;
    private bool _compact;
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

    public bool ShowTrackWetness { get => _showTrackWetness; set => Set(ref _showTrackWetness, value); }

    public bool ShowLabels { get => _showLabels; set => Set(ref _showLabels, value); }

    /// <summary>Cards side by side (the Horizontal orientation) instead of stacked.</summary>
    public bool Compact { get => _compact; set => Set(ref _compact, value); }

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

    /// <summary>The icon beside each card's value, sized to sit next to the number rather than above it.</summary>
    public double IconPixels => _iconSize switch
    {
        WeatherGraphicSize.Small => 24,
        WeatherGraphicSize.Large => 38,
        _ => 30,
    };

    public double ArrowPixels => _arrowSize switch
    {
        WeatherGraphicSize.Small => 46,
        WeatherGraphicSize.Large => 76,
        _ => 58,
    };

    public bool ShowTrackCard => ShowTrackTemp;

    public bool ShowAirCard => ShowAirTemp || ShowForecast || ShowHumidity;

    public bool ShowRainCard => ShowRainProbability || ShowTrackWetness;

    public bool ShowWindCard => ShowWindArrow || ShowWindSpeed;

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
