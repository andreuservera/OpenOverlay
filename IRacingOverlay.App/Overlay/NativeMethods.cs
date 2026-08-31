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

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

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
}
