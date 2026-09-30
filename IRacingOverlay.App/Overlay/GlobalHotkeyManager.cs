using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace IRacingOverlay.App.Overlay;

/// <summary>
/// System-wide hotkeys through Win32 RegisterHotKey, delivered to the Control Panel's window as
/// WM_HOTKEY. Nothing is polled and no keyboard hook is installed: Windows only wakes the app for the
/// exact combinations registered, and every other keystroke reaches iRacing untouched. Works whatever
/// window has focus, including when the Control Panel itself is hidden.
/// </summary>
internal sealed class GlobalHotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModShift = 0x4;
    private const uint ModWin = 0x8;
    private const uint ModNoRepeat = 0x4000;

    private readonly HwndSource _source;
    private readonly Dictionary<int, string> _registered = [];
    private IReadOnlyList<HotkeyBinding> _bindings = [];
    private bool _suspended;

    public GlobalHotkeyManager(Window owner)
    {
        _source = HwndSource.FromHwnd(new WindowInteropHelper(owner).EnsureHandle())!;
        _source.AddHook(WndProc);
    }

    public event Action<string>? Pressed;

    /// <summary>Replaces every registration. Returns the actions Windows refused — already taken by
    /// another app, or reserved by the system.</summary>
    public IReadOnlySet<string> Apply(IReadOnlyList<HotkeyBinding> bindings)
    {
        _bindings = bindings;
        return _suspended ? new HashSet<string>() : RegisterAll();
    }

    /// <summary>Releases every hotkey while a new shortcut is being recorded, so pressing an
    /// existing combination reaches the recorder instead of firing its action.</summary>
    public void Suspend()
    {
        _suspended = true;
        UnregisterAll();
    }

    public IReadOnlySet<string> Resume()
    {
        _suspended = false;
        return RegisterAll();
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
    }

    private HashSet<string> RegisterAll()
    {
        UnregisterAll();
        var failed = new HashSet<string>();
        var id = 1;
        foreach (var binding in _bindings)
        {
            if (!binding.Enabled || binding.Hotkey is not { Problem: null } hotkey)
            {
                continue;
            }

            if (RegisterHotKey(_source.Handle, id, ToNative(hotkey.Modifiers) | ModNoRepeat, (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key)))
            {
                _registered[id++] = binding.Action;
            }
            else
            {
                failed.Add(binding.Action);
            }
        }

        return failed;
    }

    private void UnregisterAll()
    {
        foreach (var id in _registered.Keys)
        {
            UnregisterHotKey(_source.Handle, id);
        }

        _registered.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _registered.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            Pressed?.Invoke(action);
        }

        return IntPtr.Zero;
    }

    private static uint ToNative(ModifierKeys modifiers) =>
        (modifiers.HasFlag(ModifierKeys.Alt) ? ModAlt : 0)
        | (modifiers.HasFlag(ModifierKeys.Control) ? ModControl : 0)
        | (modifiers.HasFlag(ModifierKeys.Shift) ? ModShift : 0)
        | (modifiers.HasFlag(ModifierKeys.Windows) ? ModWin : 0);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
