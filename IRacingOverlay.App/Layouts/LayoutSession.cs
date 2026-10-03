using IRacingOverlay.App.Diagnostics;

namespace IRacingOverlay.App.Layouts;

/// <summary>What a layout needs from the individual widgets: read how one is set up, set it up from
/// a layout entry, and put it back. The control panel implements it over its widget slots; tests
/// over plain records.</summary>
public interface ILayoutWidgetHost
{
    /// <summary>How the widget is set up right now, everything a close has to restore.</summary>
    WidgetSnapshot Capture(string type);

    /// <summary>Configures the widget from a layout entry, places it at a desktop position in WPF
    /// units, and switches it on.</summary>
    void Apply(LayoutWidget widget, double left, double top);

    /// <summary>Puts the widget back exactly as it was captured, switched on or off included.</summary>
    void Restore(WidgetSnapshot snapshot);
}

/// <summary>
/// Where a layout lands on the desktop: the monitor it opens on, whether its positions are scaled
/// to that monitor's resolution, and the WPF unit size (physical pixels per WPF unit) the widget
/// windows are positioned in.
/// </summary>
public sealed record LayoutPlacement(DisplayMonitor Monitor, bool ScalePositions, double PixelsPerDip)
{
    /// <summary>A layout position, in pixels of the layout's resolution, as a window position.</summary>
    public (double Left, double Top) ToDesktop(Layout layout, double x, double y)
    {
        var scaleX = ScalePositions ? (double)Monitor.Width / layout.Width : 1;
        var scaleY = ScalePositions ? (double)Monitor.Height / layout.Height : 1;
        var dip = PixelsPerDip > 0 ? PixelsPerDip : 1;
        return (
            Math.Round((Monitor.Left + x * scaleX) / dip, 2),
            Math.Round((Monitor.Top + y * scaleY) / dip, 2));
    }
}

/// <summary>
/// Opening and closing layouts.
///
/// Opening captures how each widget the layout controls is set up, then sets it up from the layout
/// and switches it on; closing puts back what was captured. Widgets the layout doesn't contain, or
/// contains but hides, are never touched. The capture is saved with the store, so a layout left
/// open when the app exits can still be closed properly after a restart — and nothing has to be
/// re-applied on startup, because applying a layout went through the widgets' own settings, which
/// they restore by themselves.
/// </summary>
internal sealed class LayoutSession(LayoutStore store, ILayoutWidgetHost host)
{
    public OpenLayoutState? Current => store.Open;

    /// <summary>Opens a layout, closing the one already open first.</summary>
    public void Open(Layout layout, LayoutPlacement placement)
    {
        if (store.Open is { } open && open.LayoutId != layout.Id)
        {
            Close();
        }

        Apply(layout, placement, store.Open?.LayoutId == layout.Id ? store.Open.Snapshot : []);
        AppLog.Activity("Layouts", $"Opened layout \"{layout.Name}\" on {placement.Monitor.FriendlyName}");
    }

    /// <summary>
    /// Applies the open layout again after it was edited and saved. Widgets it no longer controls
    /// (removed or hidden) are restored; newly added ones are captured first; the original capture
    /// of the rest is kept, so closing still returns to how things were before the first open.
    /// </summary>
    public void Reapply(Layout layout, LayoutPlacement placement)
    {
        if (store.Open is { } open && open.LayoutId == layout.Id)
        {
            Apply(layout, placement, open.Snapshot);
        }
    }

    public void Close()
    {
        if (store.Open is not { } open)
        {
            return;
        }

        // Last captured first, so a restore never depends on the order things were applied in.
        foreach (var snapshot in open.Snapshot.Reverse())
        {
            host.Restore(snapshot);
        }

        store.SetOpen(null);
        AppLog.Activity("Layouts", "Closed the open layout");
    }

    private void Apply(Layout layout, LayoutPlacement placement, IReadOnlyList<WidgetSnapshot> captured)
    {
        var controlled = layout.Widgets.Where(widget => widget.Visible).ToList();
        var snapshot = new List<WidgetSnapshot>();

        foreach (var previous in captured)
        {
            if (controlled.Any(widget => widget.Type == previous.Type))
            {
                snapshot.Add(previous);
            }
            else
            {
                host.Restore(previous);
            }
        }

        // Captured, then saved, before anything changes: if applying fails halfway, a close still
        // knows how to put every widget back.
        foreach (var widget in controlled.Where(widget => snapshot.All(entry => entry.Type != widget.Type)))
        {
            snapshot.Add(host.Capture(widget.Type));
        }

        store.SetOpen(new OpenLayoutState(layout.Id, snapshot, placement.ScalePositions));

        foreach (var widget in controlled.OrderBy(widget => widget.ZIndex))
        {
            var (left, top) = placement.ToDesktop(layout, widget.X, widget.Y);
            host.Apply(widget, left, top);
        }
    }
}
