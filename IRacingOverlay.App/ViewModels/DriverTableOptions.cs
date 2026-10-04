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
    /// <summary>The car's make, as its logo. Off by default.</summary>
    CarBrand,
}

/// <summary>Session information shown around a driver table, above or below it rather than as a
/// column. New elements go at the end, with a default slot in <see cref="DriverTableOptions"/>.</summary>
public enum TableInfoElement
{
    SessionType,
    Sof,
    SessionLaps,
    SessionTime,
    BrakeBias,
    AirTemp,
    TrackTemp,
    Humidity,
    Incidents,
}

/// <summary>Where a <see cref="TableInfoElement"/> sits: a corner or the middle, above or below the table.</summary>
public enum TableSlot
{
    TopLeft,
    TopCenter,
    TopRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
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
    private bool _showCarBrand;
    private bool _showClassName = true;
    private bool _showSof = true;
    private bool _showColumnHeaders = true;
    private bool _showMulticlass = true;
    private bool _showSessionLaps = true;
    private bool _showSessionTime = true;
    private int _focusSize;
    private IReadOnlyList<DriverTableColumn> _columnOrder = DriverTableColumnLayout.DefaultOrder;
    private readonly Dictionary<TableInfoElement, TableSlot> _infoSlots = Enum.GetValues<TableInfoElement>()
        .ToDictionary(element => element, DefaultSlot);
    // Shown elements beyond the original four, which keep their own switches. Off until chosen, so
    // adding an element never changes a table someone has already set up.
    private readonly HashSet<TableInfoElement> _shownExtras = [];
    private bool _showClassDrivers;
    private bool _showClassSof;
    private DriverTableColumnLayout? _columns;

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

    /// <summary>The make of each car, as its logo (or a monogram where the app has none). Off by default.</summary>
    public bool ShowCarBrand
    {
        get => _showCarBrand;
        set => SetField(ref _showCarBrand, value);
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

    /// <summary>Whether anything is shown above / below the table, so an empty band takes no room.</summary>
    public bool ShowTopInfo => Enum.GetValues<TableInfoElement>().Any(element => IsShown(element) && !IsBottom(SlotOf(element)));

    public bool ShowFooter => Enum.GetValues<TableInfoElement>().Any(element => IsShown(element) && IsBottom(SlotOf(element)));

    /// <summary>Where each element went before it could be moved: the session type and the laps on
    /// the left, the SOF and the clock on the right.</summary>
    public static TableSlot DefaultSlot(TableInfoElement element) => element switch
    {
        TableInfoElement.SessionType => TableSlot.TopLeft,
        TableInfoElement.Sof => TableSlot.TopRight,
        TableInfoElement.SessionLaps => TableSlot.BottomLeft,
        TableInfoElement.SessionTime => TableSlot.BottomRight,
        // Conditions gather in the middle of the top band when switched on.
        _ => TableSlot.TopCenter,
    };

    /// <summary>The elements that keep the switch they had before they could be moved; every other
    /// one is shown or hidden through <see cref="SetShown"/> alone.</summary>
    public static bool HasOwnSwitch(TableInfoElement element) => element <= TableInfoElement.SessionTime;

    public static bool IsBottom(TableSlot slot) => slot >= TableSlot.BottomLeft;

    public TableSlot SlotOf(TableInfoElement element) => _infoSlots[element];

    public void SetSlot(TableInfoElement element, TableSlot slot)
    {
        if (!Enum.IsDefined(slot) || _infoSlots[element] == slot)
        {
            return;
        }

        _infoSlots[element] = slot;
        OnPropertyChanged(nameof(InfoSlots));
        OnPropertyChanged(nameof(ShowTopInfo));
        OnPropertyChanged(nameof(ShowFooter));
    }

    /// <summary>Every element's slot; changes (as a whole) whenever an element moves.</summary>
    public IReadOnlyDictionary<TableInfoElement, TableSlot> InfoSlots => _infoSlots;

    public bool IsShown(TableInfoElement element) => element switch
    {
        TableInfoElement.SessionType => ShowClassName,
        TableInfoElement.Sof => ShowSof,
        TableInfoElement.SessionLaps => ShowSessionLaps,
        TableInfoElement.SessionTime => ShowSessionTime,
        _ => _shownExtras.Contains(element),
    };

    public void SetShown(TableInfoElement element, bool shown)
    {
        switch (element)
        {
            case TableInfoElement.SessionType: ShowClassName = shown; break;
            case TableInfoElement.Sof: ShowSof = shown; break;
            case TableInfoElement.SessionLaps: ShowSessionLaps = shown; break;
            case TableInfoElement.SessionTime: ShowSessionTime = shown; break;
            default:
                if (shown ? _shownExtras.Add(element) : _shownExtras.Remove(element))
                {
                    OnPropertyChanged(nameof(InfoShown));
                    OnPropertyChanged(nameof(ShowTopInfo));
                    OnPropertyChanged(nameof(ShowFooter));
                }

                break;
        }
    }

    /// <summary>The elements shown beyond the original four; changes whenever one is switched.</summary>
    public IReadOnlyCollection<TableInfoElement> InfoShown => _shownExtras;

    /// <summary>Width of the slot after the last column where Standings marks each class's fastest
    /// lap with a stopwatch; Relative has no such slot.</summary>
    public double FastestLapMarkWidth => ShowFastestLapMark ? 24 : 0;

    public bool ShowFastestLapMark => Table == DriverTable.Standings;

    /// <summary>Standings, split by class: the number of drivers in each class, with a helmet, in
    /// the class's title bar.</summary>
    public bool ShowClassDrivers
    {
        get => _showClassDrivers;
        set => SetField(ref _showClassDrivers, value);
    }

    /// <summary>Standings, split by class: each class's own strength of field in its title bar.</summary>
    public bool ShowClassSof
    {
        get => _showClassSof;
        set => SetField(ref _showClassSof, value);
    }

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

    /// <summary>The order the columns appear in, left to right. iRating stands for its delta too:
    /// the two share a badge and always move together. Assigning normalises the order (see
    /// <see cref="DriverTableColumnLayout.Normalize"/>).</summary>
    public IReadOnlyList<DriverTableColumn> ColumnOrder
    {
        get => _columnOrder;
        set
        {
            var order = DriverTableColumnLayout.Normalize(value);
            if (order.SequenceEqual(_columnOrder))
            {
                return;
            }

            _columnOrder = order;
            _columns = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Columns));
        }
    }

    /// <summary>Each column's slot and width for the current order and visibility; what the header
    /// and the rows bind their grid to.</summary>
    public DriverTableColumnLayout Columns => _columns ??= new DriverTableColumnLayout(_columnOrder, IsVisible);

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
        DriverTableColumn.CarBrand => ShowCarBrand,
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
            case DriverTableColumn.CarBrand: ShowCarBrand = visible; break;
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
        // A column switched on or off changes the widths the grid is laid out with, and an element
        // shown or hidden can empty or fill the band above or below the table.
        _columns = null;
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(ShowTopInfo));
        OnPropertyChanged(nameof(ShowFooter));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
