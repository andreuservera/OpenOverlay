using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

/// <summary>
/// Places a driver table's session information (session type, SOF, laps, clock…) into the six slots
/// around it — three above, three below — wherever the options put each one. Several elements in
/// one slot sit side by side, in <see cref="TableInfoElement"/> order. Shared by Standings and
/// Relative; the panel hands over its slot panels and its element visuals once, then this follows
/// whatever options instance the panel is given.
/// </summary>
internal sealed class TableInfoBands
{
    private const double Gap = 12;

    private readonly IReadOnlyDictionary<TableSlot, Panel> _slots;
    private readonly IReadOnlyDictionary<TableInfoElement, FrameworkElement> _elements;
    private DriverTableOptions? _options;

    public TableInfoBands(IReadOnlyDictionary<TableSlot, Panel> slots, IReadOnlyDictionary<TableInfoElement, FrameworkElement> elements)
    {
        _slots = slots;
        _elements = elements;
    }

    /// <summary>Starts following <paramref name="options"/> (and stops following the previous ones).
    /// Weak, so a thrown-away preview panel isn't kept alive by the long-lived shared options.</summary>
    public void Follow(DriverTableOptions options)
    {
        if (_options is { } old)
        {
            PropertyChangedEventManager.RemoveHandler(old, OnOptionChanged, string.Empty);
        }

        _options = options;
        PropertyChangedEventManager.AddHandler(options, OnOptionChanged, string.Empty);
        Arrange();
    }

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Only what moves or shows an element: a column toggle shouldn't rebuild the bands.
        if (e.PropertyName is nameof(DriverTableOptions.InfoSlots) or nameof(DriverTableOptions.InfoShown) or nameof(DriverTableOptions.ShowClassName) or
            nameof(DriverTableOptions.ShowSof) or nameof(DriverTableOptions.ShowSessionLaps) or nameof(DriverTableOptions.ShowSessionTime))
        {
            Arrange();
        }
    }

    private void Arrange()
    {
        if (_options is not { } options)
        {
            return;
        }

        foreach (var slot in _slots.Values)
        {
            slot.Children.Clear();
        }

        foreach (var (element, visual) in _elements)
        {
            if (!options.IsShown(element))
            {
                continue;
            }

            // Out of wherever it was (the panel's holder, or another slot) and into its slot.
            (visual.Parent as Panel)?.Children.Remove(visual);
            var slot = _slots[options.SlotOf(element)];
            visual.Margin = new Thickness(slot.Children.Count == 0 ? 0 : Gap, 0, 0, 0);
            slot.Children.Add(visual);
        }
    }
}
