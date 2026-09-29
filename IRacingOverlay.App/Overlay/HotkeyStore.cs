using System.IO;
using System.Windows.Input;

namespace IRacingOverlay.App.Overlay;

/// <summary>One action's shortcut. A null <see cref="Hotkey"/> means the user cleared it.</summary>
public sealed record HotkeyBinding(string Action, Hotkey? Hotkey, bool Enabled);

/// <summary>
/// Persists hotkeys as one JSON object keyed by action name. Actions missing from the file — every
/// action for an existing user upgrading, or one added in a later version — get their default, so
/// there is nothing to migrate; unknown names from a newer version are ignored.
/// </summary>
internal static class HotkeyStore
{
    private sealed record Entry(ModifierKeys Modifiers, Key Key, bool Enabled);

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "hotkeys.json");

    public static IReadOnlyList<HotkeyBinding> Load(IEnumerable<string> actions)
    {
        var saved = Read();
        return actions
            .Select(action =>
            {
                if (saved.TryGetValue(action, out var entry))
                {
                    var hotkey = entry.Key == Key.None ? null : new Hotkey(entry.Modifiers, entry.Key);
                    return new HotkeyBinding(action, hotkey, entry.Enabled);
                }

                var (defaultHotkey, enabled) = HotkeyDefaults.For(action);
                return new HotkeyBinding(action, defaultHotkey, enabled);
            })
            .ToList();
    }

    public static void Save(IEnumerable<HotkeyBinding> bindings)
    {
        var entries = bindings.ToDictionary(
            b => b.Action,
            b => new Entry(b.Hotkey?.Modifiers ?? ModifierKeys.None, b.Hotkey?.Key ?? Key.None, b.Enabled));
        SettingsFile.WriteJson(FilePath, entries);
    }

    private static Dictionary<string, Entry> Read() => SettingsFile.ReadJson<Dictionary<string, Entry>>(FilePath) ?? [];
}
