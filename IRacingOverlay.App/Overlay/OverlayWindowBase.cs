using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;

namespace IRacingOverlay.App.Overlay;

/// <summary>
/// Base window for every floating overlay widget: borderless, transparent, always-on-top, and
/// draggable-only-while-editing. In "locked" mode (the default once positioned) clicks pass
/// straight through to iRacing behind it via WS_EX_TRANSPARENT; in edit mode the window can be
/// dragged and a derived XAML's edit-mode border (bound to <see cref="IsEditMode"/>) shows.
///
/// The window is never resizable. Its size is entirely a function of its content: SizeToContent
/// makes it wrap whatever <see cref="ScalablePanel"/> reports at the current <see cref="ScaleLevel"/>,
/// so the only way to change a widget's size is the +/- control, and the frame can never end up
/// smaller than what it has to display. Position is the one thing the user places by hand, and the
/// only thing persisted per widget.
/// </summary>
public abstract class OverlayWindowBase : Window, INotifyPropertyChanged
{
    private const int WmLButtonDown = 0x0201;

    private readonly string _widgetName;
    private bool _isEditMode;
    private ScalablePanel? _scaler;
    private double _widgetOpacity;

    protected OverlayWindowBase(string widgetName, double defaultLeft = 100, double defaultTop = 100)
    {
        _widgetName = widgetName;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        // Reappearing after "hide outside car" must never pull focus off iRacing: a widget coming
        // back as the green flag drops would otherwise steal the keyboard mid-lap.
        ShowActivated = false;
        // The widget is exactly as big as its content, at every scale level and in both modes. No
        // derived XAML sets Width/Height any more, so there is no saved size to restore, nothing to
        // drift, and no way for a stored size to disagree with what the content actually needs.
        SizeToContent = SizeToContent.WidthAndHeight;

        _widgetOpacity = WidgetOpacityStore.Get(_widgetName);

        WindowStartupLocation = WindowStartupLocation.Manual;
        var saved = WidgetLayoutStore.Get(_widgetName);
        if (saved is not null)
        {
            Left = saved.Left;
            Top = saved.Top;
            _isEditMode = false;
            HasSavedLayout = true;
        }
        else
        {
            // No saved position yet — start editable so the user can place it the first time.
            // Window.Left/Top default to NaN until first shown; give them real values up front
            // since IsEditMode can flip (and try to persist Left/Top) before Show() is ever called.
            Left = defaultLeft;
            Top = defaultTop;
            _isEditMode = true;
            HasSavedLayout = false;
        }

        SourceInitialized += (_, _) =>
        {
            ApplyClickThrough();
            ConstrainToScreen();
            if (PresentationSource.FromVisual(this) is HwndSource hwndSource)
            {
                hwndSource.AddHook(WndProc);
            }
        };
        // Scaling up near an edge would otherwise push half the widget off the monitor, where it is
        // both unreadable and impossible to grab again.
        SizeChanged += (_, _) => ConstrainToScreen();
        Closing += (_, _) => SaveLayout();

        ApplyOpacity();
    }

    /// <summary>How visible a widget is allowed to get while the layout is being edited. A widget
    /// left at 0% is invisible by design once racing, but it still has to be findable and draggable
    /// on the screen where it lives — otherwise the only way back is the control panel, and the user
    /// has no idea where the thing they are moving actually is.</summary>
    private const double EditModeMinimumOpacity = 0.4;

    /// <summary>True once this widget has a persisted position from a previous run.</summary>
    public bool HasSavedLayout { get; private set; }

    /// <summary>
    /// The widget's own opacity, 0 to 1. Applied to the window rather than to any one brush, so it
    /// reaches everything the widget draws — panel background, borders, text, chips, bars and icons
    /// alike — in one place, and keeps working for widgets added later without them having to know
    /// the feature exists.
    /// </summary>
    public double WidgetOpacity
    {
        get => _widgetOpacity;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (_widgetOpacity.Equals(clamped))
            {
                return;
            }

            _widgetOpacity = clamped;
            WidgetOpacityStore.Save(_widgetName, clamped);
            ApplyOpacity();
            OnPropertyChanged();
        }
    }

    private void ApplyOpacity() =>
        Opacity = _isEditMode ? Math.Max(_widgetOpacity, EditModeMinimumOpacity) : _widgetOpacity;

    /// <summary>
    /// The widget's step on the shared size ladder, reached through whichever <see cref="ScalablePanel"/>
    /// the derived XAML wraps its content in. Exposed here so the control panel can drive size for
    /// every widget through one property instead of each widget having to surface its own scaler —
    /// the +/- control on the widget itself writes the same value.
    ///
    /// Assigning before the panel has loaded is a deliberate no-op: <see cref="ScalablePanel"/>
    /// restores its level from <see cref="ScaleLevelStore"/> on load, so callers persist there first
    /// and this only has to catch panels that are already on screen.
    /// </summary>
    public ScaleLevel ScaleLevel
    {
        get => Scaler?.Level ?? ScaleLevels.Default;
        set
        {
            if (Scaler is { } scaler)
            {
                scaler.Level = value;
            }
        }
    }

    private ScalablePanel? Scaler => _scaler ??= FindScalablePanel(this);

    private static ScalablePanel? FindScalablePanel(DependencyObject root)
    {
        if (root is ScalablePanel panel)
        {
            return panel;
        }

        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject node && FindScalablePanel(node) is { } found)
            {
                return found;
            }
        }

        return null;
    }

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
            ApplyClickThrough();
            ApplyOpacity();
            if (!value)
            {
                SaveLayout();
            }

            OnPropertyChanged();
        }
    }

    // Dragging is handled directly off the raw window message rather than WPF's routed mouse events
    // (Window.DragMove()) — see NativeMethods.BeginNativeDrag for why: on some windows, WPF's
    // managed input pipeline silently never raises the routed event at all, even though the raw
    // message demonstrably reaches the window (confirmed with a lower-level message hook). Handing
    // the move to the OS's own native drag loop sidesteps that entirely, for every widget.
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmLButtonDown && _isEditMode)
        {
            if (IsOverInteractiveElement(lParam))
            {
                // e.g. ScalablePanel's +/- buttons: let WPF's own routed Button.Click fire instead
                // of hijacking the click into a window drag.
                return IntPtr.Zero;
            }

            NativeMethods.BeginNativeDrag(hwnd);
            handled = true;
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    // This hit-test genuinely needs WPF's own device->DIU transform: we're asking "is there a Button
    // here in the visual tree," a question posed in WPF's coordinate space rather than against a raw
    // Win32 rect.
    private bool IsOverInteractiveElement(IntPtr lParam)
    {
        if (PresentationSource.FromVisual(this) is not HwndSource hwndSource)
        {
            return false;
        }

        var raw = lParam.ToInt64();
        var clientX = unchecked((short)(raw & 0xFFFF));
        var clientY = unchecked((short)((raw >> 16) & 0xFFFF));
        var point = hwndSource.CompositionTarget.TransformFromDevice.Transform(new Point(clientX, clientY));

        var hit = System.Windows.Media.VisualTreeHelper.HitTest(this, point)?.VisualHit;
        while (hit is not null)
        {
            if (hit is System.Windows.Controls.Primitives.ButtonBase)
            {
                return true;
            }

            hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
        }

        return false;
    }

    /// <summary>
    /// Keeps a widget reachable without ever relocating one the user deliberately placed.
    ///
    /// Bounds come from the whole virtual desktop, not from "the monitor this window is on". The
    /// earlier version asked Screen.FromHandle, which returns whichever monitor holds the *largest
    /// slice* of the window — so a widget parked near the right-hand edge of the primary monitor,
    /// overhanging slightly onto the next one, was reported as belonging to the secondary monitor
    /// and the clamp below then dutifully snapped it fully onto that monitor. The moved position was
    /// saved on close, so the widget migrated for good. Reported live as "widgets near the right
    /// edge appear on my second monitor every time I open the overlay".
    ///
    /// Clamping against the virtual desktop instead leaves a widget overhanging a monitor boundary
    /// alone — that's a legitimate placement — while still keeping it from ending up somewhere with
    /// no screen at all. SystemParameters reports these already in DIUs, so there is no
    /// device-pixel conversion left to get wrong either.
    /// </summary>
    private void ConstrainToScreen()
    {
        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        var right = left + SystemParameters.VirtualScreenWidth;
        var bottom = top + SystemParameters.VirtualScreenHeight;

        // The ceiling on the scale ladder is ultimately the screen: a widget whose content would be
        // larger than the desktop at XL is measured against this instead, so it degrades to "as big
        // as will fit" rather than running off the edge. Panels that can genuinely produce unbounded
        // content (a 60-car standings table) hit this rather than the ladder.
        MaxWidth = right - left;
        MaxHeight = bottom - top;

        if (double.IsNaN(Left) || double.IsNaN(Top) || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        // Max applied last, so a widget as large as the desktop pins to the top-left corner — where
        // its title and its size control are — instead of hanging off that edge too.
        Left = Math.Max(left, Math.Min(Left, right - ActualWidth));
        Top = Math.Max(top, Math.Min(Top, bottom - ActualHeight));
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
        // Left/Top can still be NaN in edge cases (e.g. closed before ever shown); System.Text.Json
        // throws on NaN, so skip persisting rather than crash.
        if (double.IsNaN(Left) || double.IsNaN(Top))
        {
            return;
        }

        WidgetLayoutStore.Save(_widgetName, new WidgetLayout(Left, Top));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
