using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Which optional Standings columns are shown — floating-overlay widget only, per the control
/// panel's "Standings columns" section. The full-screen Dashboard's own StandingsPanel instance
/// never gets one of these assigned, so it always renders every column regardless of what's
/// toggled here. POS/car-number/DRIVER aren't included: those are the columns you need to tell
/// drivers apart at all, so they're never optional.
/// </summary>
public sealed class StandingsColumnVisibility : INotifyPropertyChanged
{
    private bool _showIRating = true;
    private bool _showIRatingDelta = true;
    private bool _showLicense = true;
    private bool _showLap = true;
    private bool _showLastLap = true;
    private bool _showBestLap = true;
    private bool _showGap = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool ShowIRating
    {
        get => _showIRating;
        set => SetField(ref _showIRating, value);
    }

    public bool ShowIRatingDelta
    {
        get => _showIRatingDelta;
        set => SetField(ref _showIRatingDelta, value);
    }

    public bool ShowLicense
    {
        get => _showLicense;
        set => SetField(ref _showLicense, value);
    }

    public bool ShowLap
    {
        get => _showLap;
        set => SetField(ref _showLap, value);
    }

    public bool ShowLastLap
    {
        get => _showLastLap;
        set => SetField(ref _showLastLap, value);
    }

    public bool ShowBestLap
    {
        get => _showBestLap;
        set => SetField(ref _showBestLap, value);
    }

    public bool ShowGap
    {
        get => _showGap;
        set => SetField(ref _showGap, value);
    }

    private void SetField(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
