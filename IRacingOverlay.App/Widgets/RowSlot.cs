using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using IRacingOverlay.App.Diagnostics;

namespace IRacingOverlay.App.Widgets;

/// <summary>
/// One fixed line of a driver table, holding whichever row object occupies it this tick. The
/// tables get brand-new row objects every tick; putting each straight into the ItemsControl's
/// collection (even with Replace instead of Reset) re-attaches the line's container and re-resolves
/// every binding on it. Keeping the slots stable and swapping only <see cref="Value"/> leaves the
/// visual tree in place and just hands the template a new DataContext — measured at about a fifth
/// of the UI-thread time per Relative tick, with far less garbage.
/// </summary>
public sealed class RowSlot : INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs ValueChanged = new(nameof(Value));

    private object? _value;

    public object? Value
    {
        get => _value;
        set
        {
            if (ReferenceEquals(_value, value))
            {
                return;
            }

            _value = value;
            PropertyChanged?.Invoke(this, ValueChanged);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Points the slots at <paramref name="rows"/>, adding or removing slots only when the
    /// row count changes. A slot whose new row would draw exactly like the one it already shows is
    /// left alone, so WPF re-evaluates nothing for it — measured live, nearly every Standings row
    /// arrives unchanged from one tick to the next. <paramref name="perfTable"/> names the table for
    /// the temporary PerfProbe count of those unchanged rows.</summary>
    public static void Sync(ObservableCollection<RowSlot> slots, IReadOnlyList<object> rows, string? perfTable = null)
    {
        var tally = perfTable is not null && PerfProbe.Enabled;
        var unchanged = 0;
        List<string>? differences = null;
        for (var i = 0; i < rows.Count; i++)
        {
            if (i >= slots.Count)
            {
                slots.Add(new RowSlot { Value = rows[i] });
                continue;
            }

            var difference = FirstDifference(slots[i].Value, rows[i]);
            if (difference is null)
            {
                unchanged++;
                continue;
            }

            if (tally)
                (differences ??= []).Add(difference);
            slots[i].Value = rows[i];
        }

        while (slots.Count > rows.Count)
            slots.RemoveAt(slots.Count - 1);

        if (tally)
            PerfProbe.TallyRows(perfTable!, rows.Count, unchanged, differences);
    }

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> DataProperties = new();

    /// <summary>
    /// Null when <paramref name="next"/> would draw exactly like <paramref name="shown"/>; otherwise
    /// the first property that differs ("type" when they are different kinds of row). Compares the
    /// properties the builders set (init or set accessors): every display getter is computed from
    /// those. Uses each value's own Equals, so a reference type without value equality counts as
    /// different — when in doubt, the row is treated as changed. RowSlotTests guards the assumption
    /// that rows hold no state outside those properties.
    /// </summary>
    internal static string? FirstDifference(object? shown, object? next)
    {
        if (ReferenceEquals(shown, next))
            return null;
        if (shown is null || next is null || shown.GetType() != next.GetType())
            return "type";

        var properties = DataProperties.GetOrAdd(next.GetType(), type => type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.SetMethod is not null && p.GetIndexParameters().Length == 0)
            .ToArray());
        foreach (var property in properties)
        {
            if (!Equals(property.GetValue(shown), property.GetValue(next)))
                return property.Name;
        }

        return null;
    }
}
