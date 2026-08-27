using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IRacingOverlay.App.ViewModels;

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
    private FuelAverageSource _averageSource = FuelAverageSource.AllSession;
    private double _marginLiters;
    private double _marginLaps = 1;

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

    /// <summary>True when at least one of the four per-lap usage figures is on — the row's shared
    /// header/label strip is pointless when they're all off.</summary>
    public bool ShowAnyUsage => ShowLastLap || ShowAverage || ShowMinimum || ShowMaximum;

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
