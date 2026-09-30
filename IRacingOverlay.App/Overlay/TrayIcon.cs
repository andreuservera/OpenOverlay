using System.Runtime.InteropServices;
using System.Windows;
using IRacingOverlay.App.Diagnostics;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace IRacingOverlay.App.Overlay;

public enum TrayStatus
{
    Running,
    OverlaysHidden,
    NoSession,
    Error,
}

/// <summary>
/// The notification-area icon: always present while the app runs, with a status dot over the app
/// icon, a tooltip saying what's going on, double-click to open, and the right-click menu. Only
/// raises events — MainWindow decides what each one does.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    // Windows cuts tray tooltips off at 127 characters.
    private const int MaxTooltipLength = 127;

    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _toggleOverlays;
    private readonly Dictionary<TrayStatus, Drawing.Icon> _icons;
    private TrayStatus? _status;

    public TrayIcon()
    {
        _icons = CreateStatusIcons();

        var menu = new Forms.ContextMenuStrip();
        var open = new Forms.ToolStripMenuItem("Open OpenOverlay", null, Traced("Open OpenOverlay", () => OpenRequested));
        open.Font = new Drawing.Font(open.Font, Drawing.FontStyle.Bold);
        _toggleOverlays = new Forms.ToolStripMenuItem("Hide overlays", null, Traced("Show / hide overlays", () => ToggleOverlaysRequested));
        menu.Items.Add(open);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_toggleOverlays);
        menu.Items.Add(new Forms.ToolStripMenuItem("Restart overlays", null, Traced("Restart overlays", () => RestartOverlaysRequested)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Open configuration folder", null, Traced("Open configuration folder", () => OpenConfigFolderRequested)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Exit", null, Traced("Exit", () => ExitRequested)));

        _icon = new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = _icons[TrayStatus.NoSession],
            Text = "OpenOverlay",
            Visible = true,
        };
        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                OpenRequested?.Invoke();
            }
        };
    }

    public event Action? OpenRequested;
    public event Action? ToggleOverlaysRequested;
    public event Action? RestartOverlaysRequested;
    public event Action? OpenConfigFolderRequested;
    public event Action? ExitRequested;

    public void Update(TrayStatus status, string tooltip, bool overlaysHidden)
    {
        if (_status != status)
        {
            _status = status;
            _icon.Icon = _icons[status];
        }

        var text = tooltip.Length > MaxTooltipLength ? tooltip[..(MaxTooltipLength - 1)] + "…" : tooltip;
        if (_icon.Text != text)
        {
            _icon.Text = text;
        }

        _toggleOverlays.Text = overlaysHidden ? "Show overlays" : "Hide overlays";
    }

    public void ShowNotice(string title, string text) =>
        _icon.ShowBalloonTip(6000, title, text, Forms.ToolTipIcon.Info);

    private static EventHandler Traced(string item, Func<Action?> handler) => (_, _) =>
    {
        AppLog.Activity("Tray", $"Menu: {item}");
        handler()?.Invoke();
    };

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        foreach (var icon in _icons.Values)
        {
            icon.Dispose();
        }
    }

    /// <summary>The app icon with a status dot in the corner, one per state, drawn once.</summary>
    private static Dictionary<TrayStatus, Drawing.Icon> CreateStatusIcons()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/icon.ico"));
        using var stream = resource.Stream;
        using var baseIcon = new Drawing.Icon(stream, 32, 32);
        using var baseBitmap = baseIcon.ToBitmap();

        return Enum.GetValues<TrayStatus>().ToDictionary(status => status, status =>
        {
            using var bitmap = new Drawing.Bitmap(32, 32);
            using (var g = Drawing.Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.DrawImage(baseBitmap, 0, 0, 32, 32);
                using var fill = new Drawing.SolidBrush(DotColor(status));
                using var edge = new Drawing.Pen(Drawing.Color.FromArgb(0x10, 0x13, 0x16), 2.5f);
                var dot = new Drawing.RectangleF(18, 18, 13, 13);
                g.DrawEllipse(edge, dot);
                g.FillEllipse(fill, dot);
            }

            var handle = bitmap.GetHicon();
            try
            {
                using var fromHandle = Drawing.Icon.FromHandle(handle);
                return (Drawing.Icon)fromHandle.Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        });
    }

    private static Drawing.Color DotColor(TrayStatus status) => status switch
    {
        TrayStatus.Running => Drawing.Color.FromArgb(0x34, 0xD3, 0x99),
        TrayStatus.OverlaysHidden => Drawing.Color.FromArgb(0xF5, 0xA5, 0x24),
        TrayStatus.Error => Drawing.Color.FromArgb(0xF0, 0x44, 0x38),
        _ => Drawing.Color.FromArgb(0x8E, 0x99, 0xA5),
    };

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
