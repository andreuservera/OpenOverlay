using System.Collections.ObjectModel;
using System.ComponentModel;

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
    /// row count changes.</summary>
    public static void Sync(ObservableCollection<RowSlot> slots, IReadOnlyList<object> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (i < slots.Count)
                slots[i].Value = rows[i];
            else
                slots.Add(new RowSlot { Value = rows[i] });
        }

        while (slots.Count > rows.Count)
            slots.RemoveAt(slots.Count - 1);
    }
}
