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

    protected OverlayWindowBase(string widgetName)
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
        }
        else
        {
            // No saved position yet — start editable so the user can place it the first time.
            _isEditMode = true;
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

    private void SaveLayout() =>
        WidgetLayoutStore.Save(_widgetName, new WidgetLayout(Left, Top, Width, Height));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
