using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IRacingOverlay.App.ViewModels;

/// <summary>A field of the Track &amp; session bar.</summary>
public enum TrackInfoField
{
    TrackName,
    Session,
    AirTemp,
    TrackTemp,
    Wind,
    Humidity,
    TrackUsage,
    TimeLeft,
    Lap,
}

/// <summary>The kind of thing a field says, which decides where the bar draws a separator.</summary>
public enum TrackInfoGroup
{
    /// <summary>Track name and session type.</summary>
    Header,

    /// <summary>Temperatures, wind, humidity and track usage.</summary>
    Conditions,

    /// <summary>Time left and lap.</summary>
    Timing,
}

/// <summary>
/// What the Track &amp; session bar shows, and in what order. Every field toggles independently and
/// can go anywhere; the bar draws a separator wherever two neighbouring fields belong to different
/// groups, so related fields kept together still read as a group.
/// </summary>
public sealed class TrackInfoOptions : INotifyPropertyChanged
{
    /// <summary>The order the bar has always had, and where any field missing from a saved order goes.</summary>
    public static readonly IReadOnlyList<TrackInfoField> DefaultOrder = Enum.GetValues<TrackInfoField>();

    private readonly HashSet<TrackInfoField> _hidden = [];
    private IReadOnlyList<TrackInfoField> _fieldOrder = DefaultOrder;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool ShowTrackName { get => IsVisible(TrackInfoField.TrackName); set => SetVisible(TrackInfoField.TrackName, value); }

    public bool ShowSession { get => IsVisible(TrackInfoField.Session); set => SetVisible(TrackInfoField.Session, value); }

    public bool ShowAirTemp { get => IsVisible(TrackInfoField.AirTemp); set => SetVisible(TrackInfoField.AirTemp, value); }

    public bool ShowTrackTemp { get => IsVisible(TrackInfoField.TrackTemp); set => SetVisible(TrackInfoField.TrackTemp, value); }

    public bool ShowWind { get => IsVisible(TrackInfoField.Wind); set => SetVisible(TrackInfoField.Wind, value); }

    public bool ShowHumidity { get => IsVisible(TrackInfoField.Humidity); set => SetVisible(TrackInfoField.Humidity, value); }

    public bool ShowTrackUsage { get => IsVisible(TrackInfoField.TrackUsage); set => SetVisible(TrackInfoField.TrackUsage, value); }

    public bool ShowTimeLeft { get => IsVisible(TrackInfoField.TimeLeft); set => SetVisible(TrackInfoField.TimeLeft, value); }

    public bool ShowLap { get => IsVisible(TrackInfoField.Lap); set => SetVisible(TrackInfoField.Lap, value); }

    /// <summary>The fields left to right. Assigning normalises it: repeats and unknown values are
    /// dropped, and any field missing is appended where the default order has it.</summary>
    public IReadOnlyList<TrackInfoField> FieldOrder
    {
        get => _fieldOrder;
        set
        {
            var order = Normalize(value);
            if (order.SequenceEqual(_fieldOrder))
            {
                return;
            }

            _fieldOrder = order;
            OnPropertyChanged();
        }
    }

    public bool IsVisible(TrackInfoField field) => !_hidden.Contains(field);

    public void SetVisible(TrackInfoField field, bool visible)
    {
        if ((visible ? _hidden.Remove(field) : _hidden.Add(field)))
        {
            OnPropertyChanged($"Show{field}");
        }
    }

    public static TrackInfoGroup GroupOf(TrackInfoField field) => field switch
    {
        TrackInfoField.TrackName or TrackInfoField.Session => TrackInfoGroup.Header,
        TrackInfoField.TimeLeft or TrackInfoField.Lap => TrackInfoGroup.Timing,
        _ => TrackInfoGroup.Conditions,
    };

    /// <summary>The visible fields in order, each marked with whether a separator goes before it:
    /// between neighbours of different groups, never before the first.</summary>
    public IReadOnlyList<(TrackInfoField Field, bool SeparatorBefore)> Bar()
    {
        var bar = new List<(TrackInfoField, bool)>();
        TrackInfoGroup? previous = null;
        foreach (var field in _fieldOrder.Where(IsVisible))
        {
            var group = GroupOf(field);
            bar.Add((field, previous is { } before && before != group));
            previous = group;
        }

        return bar;
    }

    public static IReadOnlyList<TrackInfoField> Normalize(IEnumerable<TrackInfoField> order)
    {
        var result = order.Where(field => Enum.IsDefined(field)).Distinct().ToList();
        result.AddRange(DefaultOrder.Where(field => !result.Contains(field)));
        return result;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
