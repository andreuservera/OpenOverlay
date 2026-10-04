using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IRacingOverlay.App.ViewModels;

/// <summary>Every column a driver table can show. Doubles as the persistence key and as the
/// checkbox <c>Tag</c> in the control panel, so the three stay in sync by construction.</summary>
public enum DriverTableColumn
{
    Position,
    CarNumber,
    Driver,
    LastPitStop,
    TireCompound,
    IRating,
    IRatingDelta,
    License,
    Lap,
    LastLap,
    BestLap,
    Gap,
}

/// <summary>Which table a settings instance belongs to. Also the prefix its values are stored
/// under, so the two widgets configure independently.</summary>
public enum DriverTable
{
    Standings,
    Relative,
}

/// <summary>
/// How a driver table renders: which columns are on, and how much of the field it shows around the
/// player. One class for both widgets so a column added here appears in both, with the handful of
/// genuinely table-specific settings documented as such.
/// </summary>
public sealed class DriverTableOptions : INotifyPropertyChanged
{
    /// <summary>One car is the floor because the focus block always has to be able to hold the
    /// player. There is deliberately no ceiling: asking for more cars than the session has just
    /// shows everyone, since the builders clamp to what exists.</summary>
    public const int MinFocusSize = 1;
    public const int DefaultStandingsFocusSize = 7;
    public const int DefaultRelativeFocusSize = 4;

    private bool _showPosition = true;
    private bool _showCarNumber = true;
    private bool _showDriver = true;
    private bool _showLastPitStop;
    private bool _showTireCompound = true;
    private bool _showIRating = true;
    private bool _showIRatingDelta = true;
    private bool _showLicense = true;
    private bool _showLap = true;
    private bool _showLastLap = true;
    private bool _showBestLap = true;
    private bool _showGap = true;
    private bool _showClassName = true;
    private bool _showSof = true;
    private bool _showColumnHeaders = true;
    private bool _showMulticlass = true;
    private bool _showSessionLaps = true;
    private bool _showSessionTime = true;
    private int _focusSize;

    public DriverTableOptions(DriverTable table)
    {
        Table = table;
        _focusSize = table == DriverTable.Standings ? DefaultStandingsFocusSize : DefaultRelativeFocusSize;
    }

    public DriverTable Table { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool ShowPosition
    {
        get => _showPosition;
        set => SetField(ref _showPosition, value);
    }

    public bool ShowCarNumber
    {
        get => _showCarNumber;
        set => SetField(ref _showCarNumber, value);
    }

    public bool ShowDriver
    {
        get => _showDriver;
        set => SetField(ref _showDriver, value);
    }

    /// <summary>Lap and pit-lane time of the driver's most recent stop. Off by default.</summary>
    public bool ShowLastPitStop
    {
        get => _showLastPitStop;
        set => SetField(ref _showLastPitStop, value);
    }

    /// <summary>The tyre each car is on, as a ringed compound letter, next to the last pit stop.</summary>
    public bool ShowTireCompound
    {
        get => _showTireCompound;
        set => SetField(ref _showTireCompound, value);
    }

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

    /// <summary>Shows the session being run (RACE, QUALIFYING, PRACTICE) next to the panel title.
    /// Named for what it first showed, the player's class, so saved settings and layouts carry over.</summary>
    public bool ShowClassName
    {
        get => _showClassName;
        set => SetField(ref _showClassName, value);
    }

    /// <summary>Shows the lobby's strength of field in the header.</summary>
    public bool ShowSof
    {
        get => _showSof;
        set => SetField(ref _showSof, value);
    }

    /// <summary>The row of column names (POS, DRIVER, GAP…) above the table. Hidden, the widget
    /// gets that much shorter.</summary>
    public bool ShowColumnHeaders
    {
        get => _showColumnHeaders;
        set => SetField(ref _showColumnHeaders, value);
    }

    /// <summary>Standings only. Splits the widget into one block per car class, each with its own
    /// header and pinned podium. Relative is a picture of the track around you, where splitting by
    /// class would hide exactly the traffic you need to see.</summary>
    public bool ShowMulticlass
    {
        get => _showMulticlass;
        set => SetField(ref _showMulticlass, value);
    }

    /// <summary>Footer: the player's lap out of the session's laps — estimated from their pace in a
    /// timed race. One value for the whole table, so a footer rather than a column.</summary>
    public bool ShowSessionLaps
    {
        get => _showSessionLaps;
        set
        {
            SetField(ref _showSessionLaps, value);
            OnPropertyChanged(nameof(ShowFooter));
        }
    }

    /// <summary>Footer: elapsed session time over the session's length.</summary>
    public bool ShowSessionTime
    {
        get => _showSessionTime;
        set
        {
            SetField(ref _showSessionTime, value);
            OnPropertyChanged(nameof(ShowFooter));
        }
    }

    public bool ShowFooter => _showSessionLaps || _showSessionTime;

    /// <summary>How much of the field to show around the player. Standings reads it as the total
    /// size of the block below the pinned podium; Relative reads it as the number of cars on each
    /// side. The two are genuinely different questions, which is why the defaults differ.</summary>
    public int FocusSize
    {
        get => _focusSize;
        set
        {
            var clamped = Math.Max(MinFocusSize, value);
            if (_focusSize == clamped)
            {
                return;
            }

            _focusSize = clamped;
            OnPropertyChanged();
        }
    }

    public bool IsVisible(DriverTableColumn column) => column switch
    {
        DriverTableColumn.Position => ShowPosition,
        DriverTableColumn.CarNumber => ShowCarNumber,
        DriverTableColumn.Driver => ShowDriver,
        DriverTableColumn.LastPitStop => ShowLastPitStop,
        DriverTableColumn.TireCompound => ShowTireCompound,
        DriverTableColumn.IRating => ShowIRating,
        DriverTableColumn.IRatingDelta => ShowIRatingDelta,
        DriverTableColumn.License => ShowLicense,
        DriverTableColumn.Lap => ShowLap,
        DriverTableColumn.LastLap => ShowLastLap,
        DriverTableColumn.BestLap => ShowBestLap,
        _ => ShowGap,
    };

    public void SetVisible(DriverTableColumn column, bool visible)
    {
        switch (column)
        {
            case DriverTableColumn.Position: ShowPosition = visible; break;
            case DriverTableColumn.CarNumber: ShowCarNumber = visible; break;
            case DriverTableColumn.Driver: ShowDriver = visible; break;
            case DriverTableColumn.LastPitStop: ShowLastPitStop = visible; break;
            case DriverTableColumn.TireCompound: ShowTireCompound = visible; break;
            case DriverTableColumn.IRating: ShowIRating = visible; break;
            case DriverTableColumn.IRatingDelta: ShowIRatingDelta = visible; break;
            case DriverTableColumn.License: ShowLicense = visible; break;
            case DriverTableColumn.Lap: ShowLap = visible; break;
            case DriverTableColumn.LastLap: ShowLastLap = visible; break;
            case DriverTableColumn.BestLap: ShowBestLap = visible; break;
            default: ShowGap = visible; break;
        }
    }

    private void SetField(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
