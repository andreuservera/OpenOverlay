using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
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

        SourceInitialized += (_, _) => ApplyClickThrough();
        MouseLeftButtonDown += (_, e) =>
        {
            if (_isEditMode)
            {
                DragMove();
            }
        };
        Closing += (_, _) => SaveLayout();
    }

    /// <summary>True once this widget has a persisted position/size from a previous run.</summary>
    public bool HasSavedLayout { get; private set; }

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
