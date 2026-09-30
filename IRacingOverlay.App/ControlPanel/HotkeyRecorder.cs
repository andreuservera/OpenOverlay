using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// The click-to-record field of a <see cref="HotkeySetting"/>. Click, then press the combination:
/// held modifiers show live, the first non-modifier key completes it, Esc cancels, and clicking or
/// tabbing away cancels too. Every key is swallowed while recording so nothing else reacts to it.
/// </summary>
public sealed class HotkeyRecorder : Button
{
    private HotkeySetting? Setting => DataContext as HotkeySetting;

    protected override void OnClick()
    {
        base.OnClick();
        if (Setting is { IsRecording: false } setting)
        {
            Focus();
            setting.BeginRecording();
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Setting is not { IsRecording: true } setting)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        // Alt turns every key into Key.System; the real key is in SystemKey.
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        var modifiers = Keyboard.Modifiers;

        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            setting.CancelRecording();
        }
        else if (Hotkey.IsModifier(key))
        {
            setting.PreviewModifiers(modifiers);
        }
        else
        {
            setting.Record(modifiers, key);
        }
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        if (Setting is { IsRecording: true } setting)
        {
            e.Handled = true;
            setting.PreviewModifiers(Keyboard.Modifiers);
            return;
        }

        base.OnPreviewKeyUp(e);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        Setting?.CancelRecording();
    }
}
