using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace IRacingOverlay.App.Overlay;

/// <summary>
/// Base window for every floating overlay widget: borderless, transparent, always-on-top, and
/// draggable-only-while-editing. In "locked" mode (the default once positioned) clicks pass
/// straight through to iRacing behind it via WS_EX_TRANSPARENT; in edit mode the window can be
/// dragged/resized and a derived XAML's edit-mode border (bound to <see cref="IsEditMode"/>) shows.
/// </summary>
public abstract class OverlayWindowBase : Window, INotifyPropertyChanged
{
    private const int WmLButtonDown = 0x0201;
    private const int WmSizing = 0x0214;

    // How close to the bottom-right corner (device pixels) counts as "the resize grip" rather than
    // a drag.
    private const int ResizeGripMargin = 24;

    private readonly string _widgetName;
    private bool _isEditMode;

    protected OverlayWindowBase(string widgetName, double defaultLeft = 100, double defaultTop = 100)
    {
        _widgetName = widgetName;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var saved = WidgetLayoutStore.Get(_widgetName);
        if (saved is not null)
        {
            Left = saved.Left;
            Top = saved.Top;
            Width = saved.Width;
            Height = saved.Height;
            _isEditMode = false;
            HasSavedLayout = true;
        }
        else
        {
            // No saved position yet — start editable so the user can place it the first time.
            // Window.Left/Top default to NaN until first shown; give them real values up front
            // since IsEditMode can flip (and try to persist Left/Top) before Show() is ever called.
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = defaultLeft;
            Top = defaultTop;
            _isEditMode = true;
            HasSavedLayout = false;
        }

        SourceInitialized += (_, _) =>
        {
            ApplyClickThrough();
            if (PresentationSource.FromVisual(this) is HwndSource hwndSource)
            {
                hwndSource.AddHook(WndProc);
            }
        };
        Closing += (_, _) => SaveLayout();
    }

    /// <summary>True once this widget has a persisted position/size from a previous run.</summary>
    public bool HasSavedLayout { get; private set; }

    /// <summary>Override to lock the resize grip to a fixed width/height ratio (width divided by
    /// height) — used by widgets like Cockpit where the design only reads correctly at one
    /// proportion. Null (the default) leaves resizing free, like every other widget.</summary>
    protected virtual double? FixedAspectRatio => null;

    public bool IsEditMode
    {
        get => _isEditMode;
        set
        {
            if (_isEditMode == value)
            {
                return;
            }

            _isEditMode = value;
            ResizeMode = value ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize;
            ApplyClickThrough();
            if (!value)
            {
                SaveLayout();
            }

            OnPropertyChanged();
        }
    }

    // Both dragging and resizing are handled directly off the raw window message rather than
    // WPF's routed mouse events (Window.DragMove(), the ResizeGrip/Thumb control) — see
    // NativeMethods.BeginNativeDrag for why: on some windows, WPF's managed input pipeline
    // silently never raises the routed event at all, even though the raw message demonstrably
    // reaches the window (confirmed with a lower-level message hook). Handing both operations
    // to the OS's own native move/resize loop sidesteps that entirely, for every widget.
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmLButtonDown && _isEditMode)
        {
            if (IsOverResizeGrip(hwnd, lParam))
            {
                NativeMethods.BeginNativeResize(hwnd);
            }
            else
            {
                NativeMethods.BeginNativeDrag(hwnd);
            }

            handled = true;
            return IntPtr.Zero;
        }

        // WM_SIZING fires continuously *during* a native resize, with lParam pointing at the
        // proposed window rect — the standard Win32 mechanism for constraining a resize (min/max
        // size, fixed aspect ratio, etc.) before it's actually applied. Only relevant for widgets
        // that opt into FixedAspectRatio; every other widget resizes freely as before.
        if (msg == WmSizing && FixedAspectRatio is { } ratio)
        {
            var rect = Marshal.PtrToStructure<NativeMethods.Rect>(lParam);
            var width = rect.Right - rect.Left;
            rect.Bottom = rect.Top + (int)Math.Round(width / ratio);
            Marshal.StructureToPtr(rect, lParam, true);
            handled = true;
            return new IntPtr(1);
        }

        return IntPtr.Zero;
    }

    // clientX/clientY come straight off WM_LBUTTONDOWN's lParam, so they're already in this
    // window's own device pixels — deliberately NOT converted through WPF's ActualWidth/Height (a
    // DIU-based, DPI-dependent value that turned out to disagree with real screen pixels enough on
    // a secondary monitor to make every resize click miss the window entirely undetected).
    private static bool IsOverResizeGrip(IntPtr hwnd, IntPtr lParam)
    {
        var raw = lParam.ToInt64();
        var clientX = unchecked((short)(raw & 0xFFFF));
        var clientY = unchecked((short)((raw >> 16) & 0xFFFF));
        return NativeMethods.IsNearBottomRightCorner(hwnd, clientX, clientY, ResizeGripMargin);
    }

    private void ApplyClickThrough()
    {
        if (PresentationSource.FromVisual(this) is HwndSource hwndSource)
        {
            NativeMethods.SetClickThrough(hwndSource.Handle, clickThrough: !_isEditMode);
        }
    }

    private void SaveLayout()
    {
        // Left/Top/Width/Height can still be NaN in edge cases (e.g. closed before ever shown);
        // System.Text.Json throws on NaN, so skip persisting rather than crash.
        if (double.IsNaN(Left) || double.IsNaN(Top) || double.IsNaN(Width) || double.IsNaN(Height))
        {
            return;
        }

        WidgetLayoutStore.Save(_widgetName, new WidgetLayout(Left, Top, Width, Height));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
