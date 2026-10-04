using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IRacingOverlay.App.ViewModels;

/// <summary>A block of the Pedal trace widget, laid out left to right in the order the options give.</summary>
public enum PedalTraceElement
{
    Gear,
    Speed,
    Steering,
    Trace,
    Pedals,
}

/// <summary>
/// What the Pedal trace widget shows, and in what order: the gear, the speed, the steering wheel, the
/// trace and the pedal bars. Every block toggles independently and can go anywhere in the strip; the
/// trace takes whatever width is left.
/// </summary>
public sealed class PedalTraceOptions : INotifyPropertyChanged
{
    /// <summary>Gear, speed and wheel on the left, then the trace, then the bars with their
    /// percentages: the reading order of a broadcast input overlay.</summary>
    public static readonly IReadOnlyList<PedalTraceElement> DefaultOrder = Enum.GetValues<PedalTraceElement>();

    private readonly HashSet<PedalTraceElement> _hidden = [];
    private IReadOnlyList<PedalTraceElement> _elementOrder = DefaultOrder;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool ShowGear { get => IsVisible(PedalTraceElement.Gear); set => SetVisible(PedalTraceElement.Gear, value); }

    public bool ShowSpeed { get => IsVisible(PedalTraceElement.Speed); set => SetVisible(PedalTraceElement.Speed, value); }

    public bool ShowSteering { get => IsVisible(PedalTraceElement.Steering); set => SetVisible(PedalTraceElement.Steering, value); }

    public bool ShowTrace { get => IsVisible(PedalTraceElement.Trace); set => SetVisible(PedalTraceElement.Trace, value); }

    public bool ShowPedals { get => IsVisible(PedalTraceElement.Pedals); set => SetVisible(PedalTraceElement.Pedals, value); }

    /// <summary>The blocks left to right. Assigning normalises it: repeats and unknown values are
    /// dropped, and any block missing is appended where the default order has it.</summary>
    public IReadOnlyList<PedalTraceElement> ElementOrder
    {
        get => _elementOrder;
        set
        {
            var order = Normalize(value);
            if (order.SequenceEqual(_elementOrder))
            {
                return;
            }

            _elementOrder = order;
            OnPropertyChanged();
        }
    }

    public bool IsVisible(PedalTraceElement element) => !_hidden.Contains(element);

    public void SetVisible(PedalTraceElement element, bool visible)
    {
        if (visible ? _hidden.Remove(element) : _hidden.Add(element))
        {
            OnPropertyChanged($"Show{element}");
        }
    }

    /// <summary>The visible blocks, in order.</summary>
    public IEnumerable<PedalTraceElement> Strip() => _elementOrder.Where(IsVisible);

    public static IReadOnlyList<PedalTraceElement> Normalize(IEnumerable<PedalTraceElement> order)
    {
        var result = order.Where(element => Enum.IsDefined(element)).Distinct().ToList();
        result.AddRange(DefaultOrder.Where(element => !result.Contains(element)));
        return result;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
