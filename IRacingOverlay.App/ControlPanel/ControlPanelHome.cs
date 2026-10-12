using System.Globalization;
using System.Windows;
using System.Windows.Input;
using IRacingOverlay.App.About;
using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.App.Layouts;
using IRacingOverlay.App.Overlay;
using Screen = System.Windows.Forms.Screen;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// Home, the first page: what is going on right now (iRacing, the layout, what is on screen), what
/// needs attention (a widget off every monitor, overlays hidden by the shortcut, one that couldn't
/// open), the things done most often, and what changed in this version. And the setup assistant,
/// which Home can run again and which is offered once on a fresh install.
/// </summary>
public sealed partial class ControlPanelViewModel
{
    private const string HomePageKey = "app.home";

    // A notice about the new version stays on Home this long after an update.
    private static readonly TimeSpan NewVersionNoticeFor = TimeSpan.FromDays(14);

    private string _homeSignature = "";
    private SetupAssistant? _setup;
    private RelayCommand? _runSetup;

    private void AddHomePage() => NavItems.Add(NavItem.ForPage(
        HomePageKey, "Home", "What's on screen, what needs attention, and the things you do most.",
        "M4,11 L12,4 L20,11 V20 H14 V14 H10 V20 H4 Z", "#home"));

    private Layout? _homeScreen;

    /// <summary>What is on screen now, drawn like a layout on the monitor most of it is on, for Home's
    /// side panel; null on any other page.</summary>
    public Layout? HomeScreen => IsHomePage ? _homeScreen : null;

    /// <summary>Redraws Home's picture of the screen from the widgets on it now: where each is, how
    /// big, how see-through, set up as it is.</summary>
    private void RefreshHomeScreen()
    {
        var monitors = Monitors.Monitors();
        if (monitors.Count == 0)
        {
            return;
        }

        var scale = PixelsPerDip();
        var placed = _slots.Values
            .Where(slot => slot.State == WidgetState.OnScreen && slot.SavedPosition is not null)
            .Select(slot => (Slot: slot, X: slot.SavedPosition!.Value.Left * scale, Y: slot.SavedPosition!.Value.Top * scale))
            .ToList();
        bool On(DisplayMonitor monitor, double x, double y) =>
            x >= monitor.Left && x < monitor.Left + monitor.Width && y >= monitor.Top && y < monitor.Top + monitor.Height;
        var monitor = monitors
            .OrderByDescending(candidate => placed.Count(widget => On(candidate, widget.X, widget.Y)))
            .ThenByDescending(candidate => candidate.IsPrimary)
            .First();

        var screen = new Layout("On screen now", monitor.ToRef(), monitor.Width, monitor.Height);
        var codecs = CreateConfigCodecs();
        foreach (var (slot, x, y) in placed.Where(widget => On(monitor, widget.X, widget.Y)))
        {
            screen.Add(new LayoutWidget
            {
                Type = slot.Key,
                X = x - monitor.Left,
                Y = y - monitor.Top,
                Scale = slot.Scale,
                Opacity = slot.Opacity,
                Config = codecs[slot.Key].Read(),
            });
        }

        _homeScreen = screen;
        OnPropertyChanged(nameof(HomeScreen));
        OnPropertyChanged(nameof(HomeScreenCaption));
    }

    /// <summary>Under Home's picture: which monitor it is, and how many widgets are on it.</summary>
    public string HomeScreenCaption => _homeScreen is { } screen
        ? string.Create(CultureInfo.InvariantCulture, $"{screen.Monitor.FriendlyName} · {screen.Width}×{screen.Height} · {screen.Widgets.Count} on screen")
        : "";

    private IEnumerable<SettingsGroup> HomePage()
    {
        if (!_buildingIndex)
        {
            RefreshHomeScreen();
        }

        var onScreen = _slots.Values.Where(slot => slot.State == WidgetState.OnScreen).ToList();
        var switchedOn = _slots.Values.Count(slot => slot.IsEnabled);
        List<SettingsGroup> groups =
        [
            new SettingsGroup("RIGHT NOW")
                .With(
                    new InfoSetting("iRacing", ConnectionHint.Length > 0 ? ConnectionHint : null, ConnectionLabel),
                    LayoutRow(),
                    new InfoSetting(
                        "On screen",
                        onScreen.Count > 0 ? string.Join(", ", onScreen.Select(slot => slot.Descriptor.Name)) : null,
                        $"{onScreen.Count} of {_slots.Count}"),
                    new InfoSetting("Switched on", null, $"{switchedOn} of {_slots.Count}")),
        ];

        var attention = Attention().ToList();
        if (attention.Count > 0)
        {
            groups.Add(new SettingsGroup("NEEDS ATTENTION") { IsWarning = true }.With([.. attention]));
        }

        groups.Add(new SettingsGroup("QUICK ACTIONS")
            .With(
                new ActionSetting(
                    "Move widgets",
                    _isEditMode ? "Widgets can be dragged now. Press Done before racing." : "Drag widgets into place on screen.",
                    _isEditMode ? "Done" : "Move widgets",
                    () => IsEditMode = !IsEditMode),
                new ActionSetting(
                    _overlaysHidden ? "Overlays are hidden" : "Hide all overlays",
                    "Every widget at once, until you show them again or restart.",
                    _overlaysHidden ? "Show them" : "Hide",
                    () => OverlaysHidden = !OverlaysHidden),
                new ActionSetting(
                    "Layouts",
                    HasLayouts ? $"{LayoutChoices.Count} saved. Open one from the toolbar." : "Save an arrangement of widgets to switch to in one click.",
                    "Open Layouts",
                    () => Navigate(LayoutsPageKey)),
                new ActionSetting(
                    "Set up from a preset",
                    "Pick a monitor and a set of widgets, and they're placed for you.",
                    "Start…",
                    OpenSetupAssistant)));

        if (NewVersionNotice() is { } notice)
        {
            groups.Add(notice);
        }

        return groups;
    }

    /// <summary>The layout the toolbar shows, and whether it is on screen: the toolbar names a layout
    /// even while none is open, so "none open" alone left people wondering which one was meant.</summary>
    private InfoSetting LayoutRow() => OpenLayoutName is { } open
        ? new InfoSetting("Layout", "Its widgets follow it until you close it.", $"{open} · on screen")
        : ToolbarLayout is { } chosen
            ? new InfoSetting("Layout", "Chosen in the toolbar, not open: widgets use their own settings.", $"{chosen.Name} · not open")
            : new InfoSetting("Layout", "Widgets use their own settings.", "No layouts yet");

    /// <summary>What would make the overlay look broken while racing, each with its fix.</summary>
    private IEnumerable<SettingItem> Attention()
    {
        if (_overlaysHidden)
        {
            yield return new ActionSetting("All overlays are hidden", "Hidden with the shortcut.", "Show them", () => OverlaysHidden = false);
        }

        if (_isEditMode)
        {
            yield return new ActionSetting("Widgets can be moved", "Remember to press Done before racing.", "Done", () => IsEditMode = false);
        }

        foreach (var slot in _slots.Values)
        {
            if (slot.State == WidgetState.Failed)
            {
                yield return new ActionSetting($"{slot.Descriptor.Name} couldn't open", "Settings › Diagnostics has the details.", "Try again", slot.Apply);
            }
            else if (IsOffScreen(slot))
            {
                yield return new ActionSetting(
                    $"{slot.Descriptor.Name} is off screen",
                    "Its window is outside every monitor, perhaps one that is now disconnected.",
                    "Bring it back",
                    slot.ResetPosition);
            }
        }

        if (_health == HealthStatus.Failed)
        {
            yield return new ActionSetting("Something isn't working", HealthLine, "Diagnostics", () => Navigate(DiagnosticsPageKey));
        }
    }

    /// <summary>A widget on screen whose window no monitor shows any useful part of.</summary>
    private static bool IsOffScreen(WidgetSlot slot)
    {
        if (slot.State != WidgetState.OnScreen || slot.Window is not { IsVisible: true } window)
        {
            return false;
        }

        var scale = PixelsPerDip();
        var bounds = new System.Drawing.Rectangle(
            (int)(window.Left * scale), (int)(window.Top * scale),
            (int)Math.Max(1, window.ActualWidth * scale), (int)Math.Max(1, window.ActualHeight * scale));
        // Forty pixels in view is enough to grab it; less, and it is as good as lost.
        return !Screen.AllScreens.Any(screen =>
            System.Drawing.Rectangle.Intersect(screen.Bounds, bounds) is { Width: >= 40, Height: >= 20 });
    }

    private SettingsGroup? NewVersionNotice()
    {
        if (ReleaseCatalog.Current is not { } entry ||
            (InstallHistory.InstalledUtc is { } installed && DateTime.UtcNow - installed > NewVersionNoticeFor))
        {
            return null;
        }

        return new SettingsGroup($"NEW IN {entry.Version}")
            .With(new ActionSetting(
                "What's new",
                entry.Summary ?? entry.Sections.SelectMany(section => section.Items).FirstOrDefault()?.Text,
                "Read",
                ShowWhatsNew));
    }

    /// <summary>Rebuilds Home if what it says has changed. Called whenever anything it shows may
    /// have (the link, a widget's state, the layout, a tick of the diagnostics), and cheap when
    /// nothing has: the page is only rebuilt for a real change, never on a timer.</summary>
    private void RefreshHome()
    {
        if (_selected?.Key != HomePageKey || _buildingIndex)
        {
            return;
        }

        var signature = string.Join("|",
            ConnectionLabel,
            OpenLayoutName,
            ToolbarLayout?.Name,
            _isEditMode,
            _overlaysHidden,
            _health,
            HasLayouts,
            string.Join(",", _slots.Values.Select(slot => $"{(int)slot.State}{(IsOffScreen(slot) ? "x" : "")}")));
        if (signature == _homeSignature)
        {
            return;
        }

        _homeSignature = signature;
        BuildSettings(_selected);
    }

    // ===== Setup assistant =====

    /// <summary>The assistant on screen, or null.</summary>
    public SetupAssistant? Setup
    {
        get => _setup;
        private set
        {
            _setup = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSetupOpen));
        }
    }

    public bool IsSetupOpen => _setup is not null;

    public ICommand RunSetupCommand => _runSetup ??= new RelayCommand(OpenSetupAssistant);

    /// <summary>Offers the assistant on a fresh install: nothing switched on, no layouts, and never
    /// offered before. Offered once; after that it lives on Home.</summary>
    public void OfferSetupOnFirstRun()
    {
        if (FirstRunStore.SetupOffered)
        {
            return;
        }

        FirstRunStore.MarkSetupOffered();
        if (_slots.Values.Any(slot => slot.IsEnabled) || HasLayouts)
        {
            return;
        }

        OpenSetupAssistant(isFirstRun: true);
    }

    /// <summary>Leaves the assistant without finishing it (Escape, a click beside it).</summary>
    public void CancelSetup() => _setup?.CancelCommand.Execute(null);

    private void OpenSetupAssistant() => OpenSetupAssistant(isFirstRun: false);

    private void OpenSetupAssistant(bool isFirstRun)
    {
        var monitors = Monitors.Monitors().OrderByDescending(monitor => monitor.IsPrimary).ToList();
        var choices = monitors
            .Select((monitor, index) => new SetupChoice(
                monitor.FriendlyName,
                monitor.IsPrimary ? "Your main monitor." : "Another monitor.",
                string.Create(CultureInfo.InvariantCulture, $"{monitor.Width}×{monitor.Height}"),
                index))
            .ToList();

        AppLog.Activity("Control Panel", "Setup assistant opened");
        Setup = new SetupAssistant(
            choices,
            (monitor, preset) => ApplyPreset(monitors[monitor.Index], WidgetPresets.All[preset.Index]),
            moveNow =>
            {
                Setup = null;
                if (moveNow)
                {
                    IsEditMode = true;
                }
            },
            isFirstRun);
    }

    /// <summary>Switches the preset's widgets on, places them on the monitor, and the rest off.
    /// Undo puts every widget back as it was.</summary>
    private void ApplyPreset(DisplayMonitor monitor, WidgetPreset preset)
    {
        var before = _slots.Values.Select(slot => (Slot: slot, slot.IsEnabled, Position: slot.SavedPosition)).ToList();
        var scale = PixelsPerDip();
        var area = new Rect(monitor.Left / scale, monitor.Top / scale, monitor.Width / scale, monitor.Height / scale);
        foreach (var slot in _slots.Values)
        {
            var wanted = preset.Widgets.Contains(slot.Key);
            if (wanted)
            {
                var at = WidgetPresets.PositionOn(slot.Key, area);
                slot.MoveTo(at.X, at.Y);
            }

            slot.IsEnabled = wanted;
        }

        AppLog.Activity("Control Panel", $"Preset \"{preset.Name}\" applied on {monitor.FriendlyName}");
        ShowToast($"{preset.Name} preset is on.", () =>
        {
            foreach (var (slot, enabled, position) in before)
            {
                if (position is { } at)
                {
                    slot.MoveTo(at.Left, at.Top);
                }
                else
                {
                    slot.ForgetPosition();
                }

                slot.IsEnabled = enabled;
            }
        });
    }
}
