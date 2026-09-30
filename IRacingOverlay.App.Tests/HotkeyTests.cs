using System.Windows.Input;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift, Key.F9, "Ctrl + Shift + F9")]
    [InlineData(ModifierKeys.Alt | ModifierKeys.Windows, Key.D3, "Alt + Win + 3")]
    [InlineData(ModifierKeys.Control, Key.NumPad5, "Ctrl + Num 5")]
    public void Display_ReadsLikeAShortcut(ModifierKeys modifiers, Key key, string expected)
    {
        Assert.Equal(expected, new Hotkey(modifiers, key).Display);
    }

    [Theory]
    [InlineData(ModifierKeys.Control, Key.H)]
    [InlineData(ModifierKeys.Windows, Key.O)]
    [InlineData(ModifierKeys.None, Key.F8)] // bare function keys are allowed
    [InlineData(ModifierKeys.Shift, Key.F10)]
    public void Problem_AcceptsUsableCombinations(ModifierKeys modifiers, Key key)
    {
        Assert.Null(new Hotkey(modifiers, key).Problem);
    }

    [Theory]
    [InlineData(ModifierKeys.None, Key.H)]            // would block typing "h" everywhere
    [InlineData(ModifierKeys.Shift, Key.A)]           // would block typing "A"
    [InlineData(ModifierKeys.Control, Key.LeftShift)] // modifiers only
    [InlineData(ModifierKeys.Alt, Key.Tab)]           // reserved by Windows
    [InlineData(ModifierKeys.Alt, Key.F4)]
    public void Problem_RejectsUnusableCombinations(ModifierKeys modifiers, Key key)
    {
        Assert.NotNull(new Hotkey(modifiers, key).Problem);
    }

    [Fact]
    public void Defaults_GiveGlobalActionsValidDistinctShortcuts()
    {
        var defaults = HotkeyActions.Global.Select(HotkeyDefaults.For).ToList();

        Assert.All(defaults, d => Assert.Null(d.Hotkey!.Problem));
        Assert.Equal(defaults.Count, defaults.Select(d => d.Hotkey).Distinct().Count());
        Assert.False(HotkeyDefaults.For(HotkeyActions.ResetLayout).Enabled);
    }

    [Fact]
    public void WidgetToggles_StartUnassigned_AndRoundTripTheirWidgetKey()
    {
        var action = HotkeyActions.ToggleWidget("Standings");

        Assert.Null(HotkeyDefaults.For(action).Hotkey);
        Assert.True(HotkeyActions.TryGetWidget(action, out var key));
        Assert.Equal("Standings", key);
        Assert.False(HotkeyActions.TryGetWidget(HotkeyActions.ToggleEditMode, out _));
    }

    [Fact]
    public void Equality_IsByModifiersAndKey()
    {
        Assert.Equal(new Hotkey(ModifierKeys.Control, Key.F1), new Hotkey(ModifierKeys.Control, Key.F1));
        Assert.NotEqual(new Hotkey(ModifierKeys.Control, Key.F1), new Hotkey(ModifierKeys.Alt, Key.F1));
    }
}
