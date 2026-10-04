using System.Runtime.InteropServices;
using IRacingOverlay.App.Diagnostics;
using Screen = System.Windows.Forms.Screen;

namespace IRacingOverlay.App.Layouts;

/// <summary>
/// The monitors Windows reports, identified through the display configuration API
/// (QueryDisplayConfig): it is the one place that gives each active monitor's device path, which
/// stays the same across reboots and display renumbering, along with its friendly name. Each path is
/// matched to its WinForms <see cref="Screen"/> by GDI device name for bounds and the primary flag.
///
/// If that API fails, the monitors come from <see cref="Screen"/> alone, identified by their
/// "\\.\DISPLAYn" names: worse, since those get renumbered, but enough to keep layouts working.
/// </summary>
internal sealed class SystemMonitorSource : IMonitorSource
{
    public IReadOnlyList<DisplayMonitor> GetMonitors()
    {
        var screens = Screen.AllScreens;
        try
        {
            var targets = QueryTargets();
            if (targets.Count > 0)
            {
                var monitors = new List<DisplayMonitor>();
                foreach (var screen in screens)
                {
                    monitors.Add(targets.TryGetValue(screen.DeviceName, out var target)
                        ? FromScreen(screen, target.DevicePath, target.FriendlyName)
                        : FromScreen(screen, screen.DeviceName, null));
                }

                return monitors;
            }
        }
        catch (Exception e) when (!ExceptionPolicy.IsFatal(e))
        {
            AppLog.Warn("Monitors", "Display configuration unavailable; identifying monitors by display number", e);
        }

        return screens.Select(screen => FromScreen(screen, screen.DeviceName, null)).ToList();
    }

    private static DisplayMonitor FromScreen(Screen screen, string devicePath, string? friendlyName)
    {
        var bounds = screen.Bounds;
        var name = string.IsNullOrWhiteSpace(friendlyName) ? screen.DeviceName.TrimStart('\\', '.') : friendlyName;
        return new DisplayMonitor(
            devicePath,
            MonitorCatalog.EdidIdFromDevicePath(devicePath),
            name,
            screen.DeviceName,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            screen.Primary);
    }

    /// <summary>Active monitors keyed by the GDI device name of the source they show.</summary>
    private static Dictionary<string, (string DevicePath, string FriendlyName)> QueryTargets()
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        DisplayConfigPathInfo[] paths;
        int error;
        do
        {
            error = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount);
            if (error != ErrorSuccess)
            {
                throw new InvalidOperationException($"GetDisplayConfigBufferSizes failed ({error})");
            }

            paths = new DisplayConfigPathInfo[pathCount];
            var modes = new DisplayConfigModeInfo[modeCount];
            error = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            Array.Resize(ref paths, (int)pathCount);
        }
        while (error == ErrorInsufficientBuffer); // The configuration changed between the two calls.

        if (error != ErrorSuccess)
        {
            throw new InvalidOperationException($"QueryDisplayConfig failed ({error})");
        }

        foreach (var path in paths)
        {
            var source = new DisplayConfigSourceDeviceName
            {
                Header = Header(DeviceInfoGetSourceName, Marshal.SizeOf<DisplayConfigSourceDeviceName>(), path.SourceInfo.AdapterId, path.SourceInfo.Id),
            };
            var target = new DisplayConfigTargetDeviceName
            {
                Header = Header(DeviceInfoGetTargetName, Marshal.SizeOf<DisplayConfigTargetDeviceName>(), path.TargetInfo.AdapterId, path.TargetInfo.Id),
            };

            if (DisplayConfigGetDeviceInfo(ref source) != ErrorSuccess ||
                DisplayConfigGetDeviceInfo(ref target) != ErrorSuccess ||
                string.IsNullOrEmpty(target.MonitorDevicePath))
            {
                continue;
            }

            // A cloned display shows one source on several monitors; the first one stands for it.
            result.TryAdd(source.ViewGdiDeviceName, (target.MonitorDevicePath, target.MonitorFriendlyDeviceName));
        }

        return result;
    }

    private static DisplayConfigDeviceInfoHeader Header(int type, int size, Luid adapterId, uint id) =>
        new() { Type = type, Size = (uint)size, AdapterId = adapterId, Id = id };

    private const uint QdcOnlyActivePaths = 0x2;
    private const int DeviceInfoGetSourceName = 1;
    private const int DeviceInfoGetTargetName = 2;
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint pathCount,
        [Out] DisplayConfigPathInfo[] paths,
        ref uint modeCount,
        [Out] DisplayConfigModeInfo[] modes,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName request);

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public int OutputTechnology;
        public int Rotation;
        public int Scaling;
        public uint RefreshRateNumerator;
        public uint RefreshRateDenominator;
        public int ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    /// <summary>Only its size matters here: the mode buffer has to be passed, never read. The
    /// native struct is a 16-byte header and a 48-byte union aligned to 8.</summary>
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct DisplayConfigModeInfo
    {
        public uint InfoType;
        public uint Id;
        public Luid AdapterId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDeviceInfoHeader
    {
        public int Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigTargetDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Flags;
        public int OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string MonitorFriendlyDeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string MonitorDevicePath;
    }
}
