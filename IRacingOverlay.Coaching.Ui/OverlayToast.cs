using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace IRacingOverlay.Coaching.Ui;

/// <summary>
/// A short notice over everything, the sim included (like the overlay widgets, it shows over iRacing
/// in borderless or windowed mode): top centre of the main screen, never takes focus or clicks, and
/// fades out after a couple of seconds. For a shortcut pressed while driving.
/// </summary>
public static class OverlayToast
{
    private static Window? _window;
    private static TextBlock? _text;
    private static DispatcherTimer? _timer;

    public static void Show(string message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => Show(message));
            return;
        }

        var window = _window ??= Create();
        _text!.Text = message;
        window.BeginAnimation(UIElement.OpacityProperty, null);
        window.Opacity = 1;
        window.Show();
        window.UpdateLayout();
        var area = SystemParameters.WorkArea;
        window.Left = area.Left + (area.Width - window.ActualWidth) / 2;
        window.Top = area.Top + 48;

        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        _timer.Stop();
        _timer.Tick -= FadeOut;
        _timer.Tick += FadeOut;
        _timer.Start();
    }

    private static void FadeOut(object? sender, EventArgs e)
    {
        _timer?.Stop();
        if (_window is not { } w)
        {
            return;
        }

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(400));
        fade.Completed += (_, _) =>
        {
            if (w.Opacity <= 0.01)
            {
                w.Hide();
            }
        };
        w.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static Window Create()
    {
        _text = new TextBlock
        {
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        };
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            Focusable = false,
            IsHitTestVisible = false,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x16, 0x18, 0x1D)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xD2, 0x4D)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(18, 10, 18, 10),
                Child = _text,
            },
        };

        // Never activated and clicked through, so the sim keeps its focus and input.
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
            SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style | WsExNoActivate | WsExTransparent | WsExToolWindow | WsExTopmost));
        };
        return window;
    }

    private const int GwlExStyle = -20;
    private const long WsExTopmost = 0x00000008;
    private const long WsExTransparent = 0x00000020;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExNoActivate = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
