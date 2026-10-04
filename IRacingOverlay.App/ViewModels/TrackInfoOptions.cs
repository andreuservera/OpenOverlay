using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// What the Track &amp; session bar shows. Every field toggles independently; the group and
/// separator flags are derived from them, so the bar closes up around whatever is hidden.
/// </summary>
public sealed class TrackInfoOptions : INotifyPropertyChanged
{
    private static readonly string[] Derived =
    [
        nameof(ShowHeader), nameof(ShowConditions), nameof(ShowTiming),
        nameof(ShowFirstSeparator), nameof(ShowSecondSeparator),
    ];

    private bool _showTrackName = true;
    private bool _showSession = true;
    private bool _showAirTemp = true;
    private bool _showTrackTemp = true;
    private bool _showWind = true;
    private bool _showHumidity = true;
    private bool _showTrackUsage = true;
    private bool _showTimeLeft = true;
    private bool _showLap = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool ShowTrackName { get => _showTrackName; set => Set(ref _showTrackName, value); }

    public bool ShowSession { get => _showSession; set => Set(ref _showSession, value); }

    public bool ShowAirTemp { get => _showAirTemp; set => Set(ref _showAirTemp, value); }

    public bool ShowTrackTemp { get => _showTrackTemp; set => Set(ref _showTrackTemp, value); }

    public bool ShowWind { get => _showWind; set => Set(ref _showWind, value); }

    public bool ShowHumidity { get => _showHumidity; set => Set(ref _showHumidity, value); }

    public bool ShowTrackUsage { get => _showTrackUsage; set => Set(ref _showTrackUsage, value); }

    public bool ShowTimeLeft { get => _showTimeLeft; set => Set(ref _showTimeLeft, value); }

    public bool ShowLap { get => _showLap; set => Set(ref _showLap, value); }

    /// <summary>Track name and session type.</summary>
    public bool ShowHeader => ShowTrackName || ShowSession;

    /// <summary>Temperatures, wind, humidity and track usage.</summary>
    public bool ShowConditions => ShowAirTemp || ShowTrackTemp || ShowWind || ShowHumidity || ShowTrackUsage;

    /// <summary>Time left and lap.</summary>
    public bool ShowTiming => ShowTimeLeft || ShowLap;

    public bool ShowFirstSeparator => ShowHeader && (ShowConditions || ShowTiming);

    public bool ShowSecondSeparator => ShowConditions && ShowTiming;

    private void Set(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
        foreach (var derived in Derived)
        {
            OnPropertyChanged(derived);
        }
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
