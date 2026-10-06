using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IRacingOverlay.App.ViewModels;

/// <summary>A readout of the cockpit dashboard. What each one looks like and how much room it takes
/// lives in Widgets/Cockpit/CockpitModules.cs; the declaration order here is the default order.</summary>
public enum CockpitModule
{
    Gear,
    Speed,
    Rpm,
    Abs,
    Fuel,
    Inputs,
    WaterTemp,
    OilTemp,
    BrakeBias,
    TractionControl,
    Incidents,
    Delta,
}

/// <summary>
/// What the cockpit shows: which modules, left to right, plus the two fixed elements — the shift
/// lights across the top and the proximity bars down each side. Shared by the widget, the dashboard
/// and the preview, so a change reaches all three at once.
/// </summary>
public sealed class CockpitOptions : INotifyPropertyChanged
{
    /// <summary>Every module in declaration order.</summary>
    public static readonly IReadOnlyList<CockpitModule> DefaultOrder = Enum.GetValues<CockpitModule>();

    /// <summary>Shown on a fresh install: the old Pit Wall theme's full table, lights and radar on.
    /// Modules added after it start hidden, as they do for a saved configuration.</summary>
    public static readonly IReadOnlyList<CockpitModule> DefaultVisible =
    [
        CockpitModule.Gear, CockpitModule.Speed, CockpitModule.Rpm, CockpitModule.Abs,
        CockpitModule.Fuel, CockpitModule.Inputs, CockpitModule.WaterTemp, CockpitModule.OilTemp,
    ];

    private readonly HashSet<CockpitModule> _hidden = DefaultOrder.Except(DefaultVisible).ToHashSet();
    private IReadOnlyList<CockpitModule> _moduleOrder = DefaultOrder;
    private bool _showShiftLights = true;
    private bool _showProximityRadar = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Every module, left to right, shown or not. Assigning normalises it: repeats and
    /// unknown values are dropped, and any module missing is appended in default order.</summary>
    public IReadOnlyList<CockpitModule> ModuleOrder
    {
        get => _moduleOrder;
        set
        {
            var order = Normalize(value);
            if (order.SequenceEqual(_moduleOrder))
            {
                return;
            }

            _moduleOrder = order;
            OnPropertyChanged();
        }
    }

    public bool ShowShiftLights
    {
        get => _showShiftLights;
        set
        {
            if (_showShiftLights != value)
            {
                _showShiftLights = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ShowProximityRadar
    {
        get => _showProximityRadar;
        set
        {
            if (_showProximityRadar != value)
            {
                _showProximityRadar = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsVisible(CockpitModule module) => !_hidden.Contains(module);

    public void SetVisible(CockpitModule module, bool visible)
    {
        if (visible ? _hidden.Remove(module) : _hidden.Add(module))
        {
            OnPropertyChanged($"Show{module}");
        }
    }

    /// <summary>The visible modules, in order.</summary>
    public IReadOnlyList<CockpitModule> VisibleModules() => _moduleOrder.Where(IsVisible).ToList();

    /// <summary>
    /// Puts back a saved configuration: the order and which modules were shown. A module the saved
    /// order doesn't name — one added since it was written — goes at the end, hidden, so an update
    /// never changes a dashboard the user already arranged.
    /// </summary>
    public void Load(IEnumerable<CockpitModule> order, IEnumerable<CockpitModule> visible, bool showShiftLights, bool showProximityRadar)
    {
        var shown = visible.ToHashSet();
        ModuleOrder = order.ToList();
        foreach (var module in DefaultOrder)
        {
            SetVisible(module, shown.Contains(module));
        }

        ShowShiftLights = showShiftLights;
        ShowProximityRadar = showProximityRadar;
    }

    /// <summary>Back to <see cref="DefaultOrder"/>, showing <see cref="DefaultVisible"/>.</summary>
    public void Reset() => Load(DefaultOrder, DefaultVisible, showShiftLights: true, showProximityRadar: true);

    public static IReadOnlyList<CockpitModule> Normalize(IEnumerable<CockpitModule> order)
    {
        var result = order.Where(module => Enum.IsDefined(module)).Distinct().ToList();
        result.AddRange(DefaultOrder.Where(module => !result.Contains(module)));
        return result;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
