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
        HomePageKey, "nav.home.title", "nav.home.blurb",
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

        var screen = new Layout(Loc.T("home.onScreenNow"), monitor.ToRef(), monitor.Width, monitor.Height);
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
        ? Loc.F("home.screenSummary", screen.Monitor.FriendlyName, screen.Width, screen.Height, screen.Widgets.Count)
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
            new SettingsGroup(Loc.T("home.rightNow"))
                .With(
                    new InfoSetting("iRacing", ConnectionHint.Length > 0 ? ConnectionHint : null, ConnectionLabel),
                    LayoutRow(),
                    new InfoSetting(
                        Loc.T("home.onScreen"),
                        onScreen.Count > 0 ? string.Join(", ", onScreen.Select(slot => slot.Descriptor.Name)) : null,
                        Loc.F("home.countOf", onScreen.Count, _slots.Count)),
                    new InfoSetting(Loc.T("home.switchedOn"), null, Loc.F("home.countOf", switchedOn, _slots.Count))),
        ];

        var attention = Attention().ToList();
        if (attention.Count > 0)
        {
            groups.Add(new SettingsGroup(Loc.T("home.needsAttention")) { IsWarning = true }.With([.. attention]));
        }

        groups.Add(new SettingsGroup(Loc.T("home.quickActions"))
            .With(
                new ActionSetting(
                    Loc.T("toolbar.editMode.move"),
                    Loc.T(_isEditMode ? "home.move.editing" : "home.move.idle"),
                    EditModeLabel,
                    () => IsEditMode = !IsEditMode),
                new ActionSetting(
                    Loc.T(_overlaysHidden ? "home.hidden" : "home.hideAll"),
                    Loc.T("home.hide.hint"),
                    Loc.T(_overlaysHidden ? "home.showThem" : "home.hide"),
                    () => OverlaysHidden = !OverlaysHidden),
                new ActionSetting(
                    Loc.T("nav.layouts.title"),
                    HasLayouts ? Loc.F("home.layouts.saved", LayoutChoices.Count) : Loc.T("home.layouts.none"),
                    Loc.T("home.layouts.open"),
                    () => Navigate(LayoutsPageKey)),
                new ActionSetting(
                    Loc.T("home.preset"),
                    Loc.T("home.preset.hint"),
                    Loc.T("home.preset.start"),
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
        ? new InfoSetting(Loc.T("home.layout"), Loc.T("home.layout.openHint"), Loc.F("home.layout.openValue", open))
        : ToolbarLayout is { } chosen
            ? new InfoSetting(Loc.T("home.layout"), Loc.T("home.layout.chosenHint"), Loc.F("home.layout.chosenValue", chosen.Name))
            : new InfoSetting(Loc.T("home.layout"), Loc.T("home.layout.noneHint"), Loc.T("home.layout.none"));

    /// <summary>What would make the overlay look broken while racing, each with its fix.</summary>
    private IEnumerable<SettingItem> Attention()
    {
        if (_overlaysHidden)
        {
            yield return new ActionSetting(Loc.T("home.attention.hidden"), Loc.T("home.attention.hidden.hint"), Loc.T("home.showThem"), () => OverlaysHidden = false);
        }

        if (_isEditMode)
        {
            yield return new ActionSetting(Loc.T("home.attention.editing"), Loc.T("home.attention.editing.hint"), Loc.T("toolbar.editMode.done"), () => IsEditMode = false);
        }

        foreach (var slot in _slots.Values)
        {
            if (slot.State == WidgetState.Failed)
            {
                yield return new ActionSetting(Loc.F("home.attention.failed", slot.Descriptor.Name), Loc.T("home.attention.failed.hint"), Loc.T("home.attention.failed.retry"), slot.Apply);
            }
            else if (IsOffScreen(slot))
            {
                yield return new ActionSetting(
                    Loc.F("home.attention.offScreen", slot.Descriptor.Name),
                    Loc.T("home.attention.offScreen.hint"),
                    Loc.T("home.attention.offScreen.button"),
                    slot.ResetPosition);
            }
        }

        if (_health == HealthStatus.Failed)
        {
            yield return new ActionSetting(Loc.T("home.attention.health"), HealthLine, Loc.T("nav.diagnostics.title"), () => Navigate(DiagnosticsPageKey));
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

        return new SettingsGroup(Loc.F("home.newIn", entry.Version))
            .With(new ActionSetting(
                Loc.T("nav.help.tab.whatsNew"),
                entry.Summary ?? entry.Sections.SelectMany(section => section.Items).FirstOrDefault()?.Text,
                Loc.T("home.whatsNew.read"),
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
                Loc.T(monitor.IsPrimary ? "setup.mainMonitor" : "setup.otherMonitor"),
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
        ShowToast(Loc.F("home.presetToast", preset.Name), () =>
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
