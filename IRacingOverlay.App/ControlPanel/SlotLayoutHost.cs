using IRacingOverlay.App.Layouts;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// Layouts acting on the real widgets, through the same slot properties and codecs the widget pages
/// use. Every change therefore goes where a change made by hand would: the live options, the
/// settings stores, the window if it exists.
/// </summary>
internal sealed class SlotLayoutHost(Func<string, WidgetSlot> slotOf, Func<IReadOnlyDictionary<string, IWidgetConfigCodec>> codecs)
    : ILayoutWidgetHost
{
    public WidgetSnapshot Capture(string type)
    {
        var slot = slotOf(type);
        // The window knows best where it is (it may have been dragged since it last saved);
        // without one, the store is where it will open.
        (double Left, double Top)? position = slot.Window is { } window && !double.IsNaN(window.Left) && !double.IsNaN(window.Top)
            ? (window.Left, window.Top)
            : WidgetLayoutStore.Get(type) is { } saved ? (saved.Left, saved.Top) : null;

        return new WidgetSnapshot(
            type,
            slot.IsEnabled,
            position?.Left,
            position?.Top,
            slot.Scale,
            slot.Opacity,
            slot.HideOutsideCar,
            codecs()[type].Read());
    }

    public void Apply(LayoutWidget widget, double left, double top)
    {
        var slot = slotOf(widget.Type);
        codecs()[widget.Type].Apply(widget.Config);
        slot.Scale = widget.Scale;
        slot.Opacity = widget.Opacity;
        slot.HideOutsideCar = widget.HideOutsideCar;
        // Placed before it is switched on, so it never appears somewhere else first.
        slot.MoveTo(left, top);
        slot.IsEnabled = true;
    }

    public void Restore(WidgetSnapshot snapshot)
    {
        var slot = slotOf(snapshot.Type);
        // Switched off first if it was off, so it doesn't visibly jump back before disappearing.
        if (!snapshot.Enabled)
        {
            slot.IsEnabled = false;
        }

        codecs()[snapshot.Type].Apply(snapshot.Config);
        slot.Scale = snapshot.Scale;
        slot.Opacity = snapshot.Opacity;
        slot.HideOutsideCar = snapshot.HideOutsideCar;
        if (snapshot.Left is { } left && snapshot.Top is { } top)
        {
            slot.MoveTo(left, top);
        }
        else
        {
            slot.ForgetPosition();
        }

        slot.IsEnabled = snapshot.Enabled;
    }
}
