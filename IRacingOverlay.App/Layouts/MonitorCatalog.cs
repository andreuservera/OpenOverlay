namespace IRacingOverlay.App.Layouts;

/// <summary>A connected monitor. <see cref="DevicePath"/> and <see cref="EdidId"/> identify it
/// (see <see cref="MonitorRef"/>); <see cref="GdiDeviceName"/> ("\\.\DISPLAY1") ties it to the
/// WinForms <c>Screen</c> it is, and changes whenever Windows renumbers displays, so it is never
/// stored. Bounds are in desktop pixels.</summary>
public sealed record DisplayMonitor(
    string DevicePath,
    string? EdidId,
    string FriendlyName,
    string GdiDeviceName,
    int Left,
    int Top,
    int Width,
    int Height,
    bool IsPrimary)
{
    public MonitorRef ToRef() => new(DevicePath, EdidId, FriendlyName, Width, Height);
}

/// <summary>Where the monitors come from: Windows in the app, a fixed list in tests.</summary>
public interface IMonitorSource
{
    IReadOnlyList<DisplayMonitor> GetMonitors();
}

/// <summary>How a stored monitor was found among the connected ones.</summary>
public enum MonitorMatch
{
    /// <summary>The same monitor on the same connector.</summary>
    DevicePath,

    /// <summary>The only connected monitor of the same make and model, e.g. after a port swap.</summary>
    Edid,

    /// <summary>Not found: the primary monitor stands in, and the user should be told.</summary>
    PrimaryFallback,
}

public sealed record MonitorResolution(DisplayMonitor Monitor, MonitorMatch Match)
{
    public bool IsFallback => Match == MonitorMatch.PrimaryFallback;
}

/// <summary>The connected monitors, and which of them a layout's <see cref="MonitorRef"/> means.</summary>
public sealed class MonitorCatalog(IMonitorSource source)
{
    public IReadOnlyList<DisplayMonitor> Monitors() => source.GetMonitors();

    /// <summary>Null only when no monitor is reported at all.</summary>
    public MonitorResolution? Resolve(MonitorRef target) => Resolve(target, source.GetMonitors());

    /// <summary>
    /// The exact device path first. Failing that, the EDID id, but only when exactly one connected
    /// monitor has it: two identical monitors can't be told apart, and guessing would put the layout
    /// on the wrong one without saying so. Otherwise the primary monitor, flagged as a fallback.
    /// </summary>
    public static MonitorResolution? Resolve(MonitorRef target, IReadOnlyList<DisplayMonitor> monitors)
    {
        if (monitors.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(target.DevicePath) &&
            monitors.FirstOrDefault(m => string.Equals(m.DevicePath, target.DevicePath, StringComparison.OrdinalIgnoreCase)) is { } exact)
        {
            return new(exact, MonitorMatch.DevicePath);
        }

        if (!string.IsNullOrEmpty(target.EdidId))
        {
            var sameModel = monitors.Where(m => string.Equals(m.EdidId, target.EdidId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (sameModel.Count == 1)
            {
                return new(sameModel[0], MonitorMatch.Edid);
            }
        }

        return new(monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0], MonitorMatch.PrimaryFallback);
    }

    /// <summary>The EDID id embedded in a monitor device path:
    /// <c>\\?\DISPLAY#PHLC347#5&amp;378f1b5b&amp;0&amp;UID24834#{e6f07b5f-…}</c> gives "PHLC347".
    /// Null for anything else, such as the "\\.\DISPLAY1" names the fallback source reports.</summary>
    public static string? EdidIdFromDevicePath(string? devicePath)
    {
        var parts = devicePath?.Split('#');
        return parts is { Length: >= 3 } && parts[0].EndsWith("DISPLAY", StringComparison.OrdinalIgnoreCase) && parts[1].Length > 0
            ? parts[1].ToUpperInvariant()
            : null;
    }
}
