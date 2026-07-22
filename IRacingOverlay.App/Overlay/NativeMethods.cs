using System.Runtime.InteropServices;

namespace IRacingOverlay.App.Overlay;

internal static class NativeMethods
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));

    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 0x2;
    private const int HtBottomRight = 0x11;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    /// <summary>True if (clientX, clientY) — straight off a WM_LBUTTONDOWN lParam, so already in
    /// this window's own device pixels — falls within marginPx of its bottom-right corner. Deriving
    /// the window's size from GetClientRect (also device pixels) rather than WPF's ActualWidth/
    /// Height avoids a DPI round-trip entirely — comparing a DIU-converted point against
    /// UI-Automation-reported bounds turned out to disagree with real screen pixels on a secondary
    /// monitor, which is exactly what made resize clicks land outside the window undetected.</summary>
    public static bool IsNearBottomRightCorner(IntPtr hWnd, int clientX, int clientY, int marginPx)
    {
        GetClientRect(hWnd, out var rect);
        return clientX >= rect.Right - marginPx && clientY >= rect.Bottom - marginPx;
    }

    /// <summary>
    /// Starts a native window drag exactly as if the user had clicked a title bar — the standard
    /// technique for making a borderless (WindowStyle=None) window draggable, and more reliable here
    /// than WPF's own <c>Window.DragMove()</c>: on one specific widget, DragMove/WM_SYSCOMMAND+SC_MOVE
    /// silently did nothing even though the raw WM_LBUTTONDOWN demonstrably reached the window
    /// (confirmed with a native message hook) — WPF's routed MouseLeftButtonDown event never fired for
    /// it, for reasons that didn't trace back to any app-level XAML/content/z-order difference from
    /// every other (working) widget. Releasing capture and re-posting the click as a non-client
    /// caption click hands the whole drag over to the OS's own window-move loop, sidestepping
    /// whatever was swallowing it in WPF's managed input pipeline.
    /// </summary>
    public static void BeginNativeDrag(IntPtr hWnd)
    {
        ReleaseCapture();
        SendMessage(hWnd, WmNcLButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
    }

    /// <summary>
    /// Starts a native bottom-right-corner resize, the same way as <see cref="BeginNativeDrag"/> but
    /// for resizing — WPF's own ResizeGrip/Thumb control depends on the exact same routed mouse-event
    /// pipeline that silently doesn't fire for some windows, so it's just as unreliable as
    /// Window.DragMove() was. Handing the whole resize over to the OS's native loop this way still
    /// generates WM_SIZING messages the caller can intercept to enforce a fixed aspect ratio.
    /// Unlike BeginNativeDrag, this needs the real current cursor position in lParam — the OS anchors
    /// the resize to it, and a resize (unlike a caption drag) silently no-ops if that anchor doesn't
    /// land near the corner it's supposed to be resizing from.
    /// </summary>
    public static void BeginNativeResize(IntPtr hWnd)
    {
        ReleaseCapture();
        GetCursorPos(out var cursor);
        var lParam = new IntPtr((cursor.Y << 16) | (cursor.X & 0xFFFF));
        SendMessage(hWnd, WmNcLButtonDown, new IntPtr(HtBottomRight), lParam);
    }

    /// <summary>
    /// Makes the window pass all mouse input through to whatever is behind it (iRacing).
    /// Only toggles WS_EX_TRANSPARENT — WS_EX_LAYERED is left alone since WPF's AllowsTransparency
    /// already manages that bit for the window's alpha blending.
    /// </summary>
    public static void SetClickThrough(IntPtr hWnd, bool clickThrough)
    {
        var style = GetWindowLongPtr(hWnd, GwlExStyle).ToInt64();
        style = clickThrough ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLongPtr(hWnd, GwlExStyle, new IntPtr(style));
    }

    /// <summary>Layout of the RECT Win32 passes via WM_SIZING's lParam — the window's proposed
    /// screen bounds during a native resize, before it's actually applied.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
