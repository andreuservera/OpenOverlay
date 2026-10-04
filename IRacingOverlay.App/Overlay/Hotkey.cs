using System.Text.Json.Serialization;
using System.Windows.Input;

namespace IRacingOverlay.App.Overlay;

/// <summary>
/// What a global hotkey can do, as stable string ids: they are the persistence keys, so reordering
/// or adding actions never scrambles saved shortcuts. Every widget gets its own show/hide action.
/// </summary>
public static class HotkeyActions
{
    public const string ToggleOverlays = "ToggleOverlays";
    public const string ToggleEditMode = "ToggleEditMode";
    public const string ToggleControlPanel = "ToggleControlPanel";
    public const string ResetLayout = "ResetLayout";
    public const string RestartOverlays = "RestartOverlays";

    /// <summary>Opens the layout chosen in the toolbar, or closes the open one: the toolbar's button.</summary>
    public const string ToggleLayout = "ToggleLayout";
    public const string NextLayout = "NextLayout";
    public const string PreviousLayout = "PreviousLayout";

    /// <summary>Opens the editor on the open layout (or the one chosen in the toolbar).</summary>
    public const string EditLayout = "EditLayout";

    private const string WidgetPrefix = "ToggleWidget.";
    private const string LayoutPrefix = "OpenLayout.";

    public static IReadOnlyList<string> Global { get; } =
        [ToggleOverlays, ToggleEditMode, ToggleControlPanel, RestartOverlays, ResetLayout];

    /// <summary>The layout actions that are not tied to one layout. Unassigned by default.</summary>
    public static IReadOnlyList<string> Layouts { get; } = [ToggleLayout, NextLayout, PreviousLayout, EditLayout];

    public static string ToggleWidget(string widgetKey) => WidgetPrefix + widgetKey;

    /// <summary>A layout's own shortcut. Not saved with the other hotkeys: it is kept on the layout,
    /// so it goes when the layout does.</summary>
    public static string OpenLayout(Guid layoutId) => LayoutPrefix + layoutId.ToString("D");

    public static bool TryGetLayout(string action, out Guid layoutId)
    {
        layoutId = Guid.Empty;
        return action.StartsWith(LayoutPrefix, StringComparison.Ordinal) && Guid.TryParse(action[LayoutPrefix.Length..], out layoutId);
    }

    public static bool TryGetWidget(string action, out string widgetKey)
    {
        widgetKey = action.StartsWith(WidgetPrefix, StringComparison.Ordinal) ? action[WidgetPrefix.Length..] : "";
        return widgetKey.Length > 0;
    }
}

/// <summary>A key plus modifiers, e.g. Ctrl + Shift + F9.</summary>
public sealed record Hotkey(ModifierKeys Modifiers, Key Key)
{
    private static readonly HashSet<(ModifierKeys, Key)> ReservedBySystem =
    [
        (ModifierKeys.Alt, Key.Tab),
        (ModifierKeys.Alt, Key.F4),
        (ModifierKeys.Alt, Key.Escape),
        (ModifierKeys.Control, Key.Escape),
        (ModifierKeys.Alt, Key.Space),
        (ModifierKeys.Control | ModifierKeys.Alt, Key.Delete),
    ];

    [JsonIgnore]
    public string Display => Describe(Modifiers, KeyName(Key));

    /// <summary>"Ctrl + Shift + {key}" — also used for a combination still being pressed.</summary>
    public static string Describe(ModifierKeys modifiers, string key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key);
        return string.Join(" + ", parts);
    }

    /// <summary>Why this combination can't be used as a global hotkey, or null if it can.</summary>
    [JsonIgnore]
    public string? Problem
    {
        get
        {
            if (Key == Key.None || IsModifier(Key))
            {
                return "Add a key to go with the modifiers.";
            }

            // A global hotkey swallows the combination everywhere, so a bare letter or Shift+letter
            // would stop you typing it in any app. Function keys are the exception.
            var hasStrongModifier = (Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0;
            if (!hasStrongModifier && !IsFunctionKey(Key))
            {
                return "Use Ctrl, Alt or Win with this key, or pick a function key (F1–F24).";
            }

            return ReservedBySystem.Contains((Modifiers, Key)) ? "Windows reserves this combination." : null;
        }
    }

    public static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static bool IsFunctionKey(Key key) => key is >= Key.F1 and <= Key.F24;

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => $"Num {key - Key.NumPad0}",
        Key.OemPlus => "+",
        Key.OemMinus => "-",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.Return => "Enter",
        Key.Next => "Page Down",
        Key.Prior => "Page Up",
        _ => key.ToString(),
    };
}

public static class HotkeyDefaults
{
    // Ctrl+Shift+F-keys: no AltGr collision (Ctrl+Alt is AltGr on many layouts, so Ctrl+Alt+E would
    // swallow "€"), nothing iRacing binds by default, and nothing browsers or Windows claim.
    private static readonly Dictionary<string, (Hotkey? Hotkey, bool Enabled)> Global = new()
    {
        [HotkeyActions.ToggleOverlays] = (new Hotkey(ModifierKeys.Control | ModifierKeys.Shift, Key.F9), true),
        [HotkeyActions.ToggleEditMode] = (new Hotkey(ModifierKeys.Control | ModifierKeys.Shift, Key.F10), true),
        [HotkeyActions.ToggleControlPanel] = (new Hotkey(ModifierKeys.Control | ModifierKeys.Shift, Key.F11), true),
        // F8 rather than R: Ctrl+Shift+R is every browser's hard refresh, and a global hotkey would take it.
        [HotkeyActions.RestartOverlays] = (new Hotkey(ModifierKeys.Control | ModifierKeys.Shift, Key.F8), true),
        // Off by default: one stray press would undo every widget placement.
        [HotkeyActions.ResetLayout] = (new Hotkey(ModifierKeys.Control | ModifierKeys.Shift, Key.F12), false),
    };

    /// <summary>Widget toggles start unassigned: there are too many widgets to pick sensible keys for.</summary>
    public static (Hotkey? Hotkey, bool Enabled) For(string action) =>
        Global.TryGetValue(action, out var value) ? value : (null, true);
}
