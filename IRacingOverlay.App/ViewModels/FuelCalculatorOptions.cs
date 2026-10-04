using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IRacingOverlay.App.ViewModels;

/// <summary>A readout cell of the Fuel Calculator: the per-lap usage figures, then the strategy
/// ones. Each group is reordered on its own; the two never mix.</summary>
public enum FuelCell
{
    LastLap,
    Average,
    Minimum,
    Maximum,
    FuelRemaining,
    FuelToFinish,
    Refuel,
}

/// <summary>Which group of cells goes first.</summary>
public enum FuelGroupOrder
{
    /// <summary>As the widget has always done: usage first in the wide strip, strategy first
    /// stacked, where what you act on leads.</summary>
    Auto,
    UsageFirst,
    StrategyFirst,
}

/// <summary>
/// Per-section toggles and calculation settings for the Fuel Calculator widget. Every block can be
/// switched off independently, so the same panel serves both as a one-line "laps left" readout and
/// as a full strategy box without needing two implementations.
/// </summary>
public sealed class FuelCalculatorOptions : INotifyPropertyChanged
{
    private bool _showFuelBar = true;
    private bool _showFuelRemaining = true;
    private bool _showLastLap = true;
    private bool _showAverage = true;
    private bool _showMinimum;
    private bool _showMaximum;
    private bool _showLapsRemaining = true;
    private bool _showFuelToFinish = true;
    private bool _showRefuel = true;
    private bool _vertical;
    private FuelAverageSource _averageSource = FuelAverageSource.AllSession;
    private double _marginLiters;
    private double _marginLaps = 1;
    private IReadOnlyList<FuelCell> _usageOrder = UsageCells;
    private IReadOnlyList<FuelCell> _strategyOrder = StrategyCells;
    private FuelGroupOrder _firstGroup;

    /// <summary>The usage cells in their default order.</summary>
    public static readonly IReadOnlyList<FuelCell> UsageCells = [FuelCell.LastLap, FuelCell.Average, FuelCell.Minimum, FuelCell.Maximum];

    /// <summary>The strategy cells in their default order.</summary>
    public static readonly IReadOnlyList<FuelCell> StrategyCells = [FuelCell.FuelRemaining, FuelCell.FuelToFinish, FuelCell.Refuel];

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool ShowFuelBar
    {
        get => _showFuelBar;
        set => SetField(ref _showFuelBar, value);
    }

    public bool ShowFuelRemaining
    {
        get => _showFuelRemaining;
        set => SetField(ref _showFuelRemaining, value);
    }

    public bool ShowLastLap
    {
        get => _showLastLap;
        set => SetField(ref _showLastLap, value);
    }

    public bool ShowAverage
    {
        get => _showAverage;
        set => SetField(ref _showAverage, value);
    }

    public bool ShowMinimum
    {
        get => _showMinimum;
        set => SetField(ref _showMinimum, value);
    }

    public bool ShowMaximum
    {
        get => _showMaximum;
        set => SetField(ref _showMaximum, value);
    }

    public bool ShowLapsRemaining
    {
        get => _showLapsRemaining;
        set => SetField(ref _showLapsRemaining, value);
    }

    public bool ShowFuelToFinish
    {
        get => _showFuelToFinish;
        set => SetField(ref _showFuelToFinish, value);
    }

    public bool ShowRefuel
    {
        get => _showRefuel;
        set => SetField(ref _showRefuel, value);
    }

    /// <summary>Stacked tall and narrow (headline on top, cells two to a row) instead of the default
    /// wide strip.</summary>
    public bool Vertical
    {
        get => _vertical;
        set => SetField(ref _vertical, value);
    }

    /// <summary>True when at least one of the four per-lap usage figures is on — the row's shared
    /// header/label strip is pointless when they're all off.</summary>
    public bool ShowAnyUsage => ShowLastLap || ShowAverage || ShowMinimum || ShowMaximum;

    /// <summary>The usage cells left to right (or top to bottom). Assigning keeps only usage cells,
    /// once each, and appends any missing in their default place.</summary>
    public IReadOnlyList<FuelCell> UsageOrder
    {
        get => _usageOrder;
        set => SetOrder(ref _usageOrder, Normalize(value, UsageCells));
    }

    /// <summary>The strategy cells in order; normalised like <see cref="UsageOrder"/>.</summary>
    public IReadOnlyList<FuelCell> StrategyOrder
    {
        get => _strategyOrder;
        set => SetOrder(ref _strategyOrder, Normalize(value, StrategyCells));
    }

    public FuelGroupOrder FirstGroup
    {
        get => _firstGroup;
        set
        {
            if (_firstGroup == value || !Enum.IsDefined(value))
            {
                return;
            }

            _firstGroup = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UsageOnTop));
        }
    }

    /// <summary>Whether the usage group comes before the strategy group, in this orientation.</summary>
    public bool UsageOnTop => _firstGroup switch
    {
        FuelGroupOrder.UsageFirst => true,
        FuelGroupOrder.StrategyFirst => false,
        _ => !_vertical,
    };

    public bool IsVisible(FuelCell cell) => cell switch
    {
        FuelCell.LastLap => ShowLastLap,
        FuelCell.Average => ShowAverage,
        FuelCell.Minimum => ShowMinimum,
        FuelCell.Maximum => ShowMaximum,
        FuelCell.FuelRemaining => ShowFuelRemaining,
        FuelCell.FuelToFinish => ShowFuelToFinish,
        _ => ShowRefuel,
    };

    public void SetVisible(FuelCell cell, bool visible)
    {
        switch (cell)
        {
            case FuelCell.LastLap: ShowLastLap = visible; break;
            case FuelCell.Average: ShowAverage = visible; break;
            case FuelCell.Minimum: ShowMinimum = visible; break;
            case FuelCell.Maximum: ShowMaximum = visible; break;
            case FuelCell.FuelRemaining: ShowFuelRemaining = visible; break;
            case FuelCell.FuelToFinish: ShowFuelToFinish = visible; break;
            default: ShowRefuel = visible; break;
        }
    }

    private static IReadOnlyList<FuelCell> Normalize(IEnumerable<FuelCell> order, IReadOnlyList<FuelCell> group)
    {
        var result = order.Where(group.Contains).Distinct().ToList();
        result.AddRange(group.Where(cell => !result.Contains(cell)));
        return result;
    }

    private void SetOrder(ref IReadOnlyList<FuelCell> field, IReadOnlyList<FuelCell> order, [CallerMemberName] string? propertyName = null)
    {
        if (order.SequenceEqual(field))
        {
            return;
        }

        field = order;
        OnPropertyChanged(propertyName);
    }

    public FuelAverageSource AverageSource
    {
        get => _averageSource;
        set
        {
            if (_averageSource == value)
            {
                return;
            }

            _averageSource = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Flat reserve held back on top of the computed need — covers a slow lap, a splash of
    /// fuel left in the tank at the flag, or simply not trusting the average.</summary>
    public double MarginLiters
    {
        get => _marginLiters;
        set => SetField(ref _marginLiters, value);
    }

    /// <summary>Reserve expressed in laps instead of liters; scales itself with consumption, so it
    /// stays meaningful across cars without retuning. Added on top of <see cref="MarginLiters"/>.</summary>
    public double MarginLaps
    {
        get => _marginLaps;
        set => SetField(ref _marginLaps, value);
    }

    private void SetField(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);

        // The usage row's own visibility is derived from the four usage toggles, so it has to be
        // re-evaluated whenever any of them flips.
        if (propertyName == nameof(Vertical))
        {
            OnPropertyChanged(nameof(UsageOnTop));
        }

        if (propertyName is nameof(ShowLastLap) or nameof(ShowAverage) or nameof(ShowMinimum) or nameof(ShowMaximum))
        {
            OnPropertyChanged(nameof(ShowAnyUsage));
        }
    }

    private void SetField(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        if (field.Equals(value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
