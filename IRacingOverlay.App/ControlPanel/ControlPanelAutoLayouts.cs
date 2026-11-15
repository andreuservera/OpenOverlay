using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.App.Layouts;
using IRacingOverlay.App.Localization;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// Layouts that open by themselves for a kind of session or a car. When what is being driven
/// changes, the best-matching layout opens; when nothing matches any more, a layout that opened by
/// itself closes again. A layout opened or closed by hand stays as it is until the next change.
/// </summary>
public sealed partial class ControlPanelViewModel
{
    // Session-kind choices on the Layouts page, in order, and what each one stores.
    private static readonly SessionKind[][] SessionChoices =
    [
        [],
        [SessionKind.Practice],
        [SessionKind.Qualifying],
        [SessionKind.Race],
        [SessionKind.Practice, SessionKind.Qualifying],
        [SessionKind.Practice, SessionKind.Qualifying, SessionKind.Race],
    ];

    private (SessionKind? Kind, string? Car) _drivingContext;
    private Guid? _autoOpenedId;

    /// <summary>The car being driven (CarScreenName), once a session is known.</summary>
    public string? CurrentCar => _drivingContext.Car;

    /// <summary>From the main window, on the UI thread, every tick: what is being driven right now.
    /// Only a change does anything. Nulls (iRacing gone) leave the open layout as it is.</summary>
    public void SetDrivingContext(SessionKind? kind, string? car)
    {
        if ((kind, car) == _drivingContext)
        {
            return;
        }

        _drivingContext = (kind, car);
        if (kind is null && car is null)
        {
            return;
        }

        ApplyAutoLayouts();
    }

    private void ApplyAutoLayouts()
    {
        var (kind, car) = _drivingContext;
        var pick = LayoutAutoOpen.Pick(LayoutStore.List(), kind, car);
        var open = LayoutStore.Open?.LayoutId;
        if (pick is not null)
        {
            if (pick.Id != open && OpenLayoutQuietly(pick))
            {
                _autoOpenedId = pick.Id;
                AppLog.Activity("Layouts", $"Opened \"{pick.Name}\" automatically", new Dictionary<string, string>
                {
                    ["session"] = kind?.ToString() ?? "-",
                    ["car"] = car ?? "-",
                });
            }
        }
        else if (open is { } id && id == _autoOpenedId)
        {
            AppLog.Activity("Layouts", "Closed the automatically opened layout");
            _autoOpenedId = null;
            CloseLayout();
        }
    }

    /// <summary>Opens a layout without asking anything, for opening it mid-session: a monitor that
    /// isn't connected leaves it closed, and a different resolution scales the positions.</summary>
    private bool OpenLayoutQuietly(Layout layout)
    {
        if (Monitors.Resolve(layout.Monitor) is not { IsFallback: false } found)
        {
            AppLog.Info("Layouts", $"\"{layout.Name}\" not opened automatically: its monitor isn't connected");
            return false;
        }

        var monitor = found.Monitor;
        var scale = monitor.Width != layout.Width || monitor.Height != layout.Height;
        LayoutSession.Open(layout, new LayoutPlacement(monitor, scale, PixelsPerDip()));
        RequestStacking();
        OnLayoutOpenChanged();
        return true;
    }

    private static string SessionChoiceLabel(SessionKind[] kinds) => kinds switch
    {
        [] => Loc.T("layouts.auto.anySession"),
        [SessionKind.Practice] => Loc.T("layouts.auto.practice"),
        [SessionKind.Qualifying] => Loc.T("layouts.auto.qualifying"),
        [SessionKind.Race] => Loc.T("layouts.auto.race"),
        [SessionKind.Practice, SessionKind.Qualifying] => Loc.T("layouts.auto.practiceAndQualifying"),
        _ => Loc.T("layouts.auto.everySession"),
    };

    /// <summary>Layouts page: per layout, when it opens by itself.</summary>
    private SettingsGroup AutoOpenGroup(IReadOnlyList<Layout> layouts)
    {
        var group = new SettingsGroup(Loc.T("layouts.auto.title"), Loc.T("layouts.auto.hint"));
        foreach (var layout in layouts)
        {
            var id = layout.Id;
            var sessions = layout.AutoSessions.Order().ToArray();
            var sessionIndex = Array.FindIndex(SessionChoices, choice => choice.SequenceEqual(sessions));
            if (sessionIndex < 0)
            {
                sessionIndex = SessionChoices.Length - 1;
            }

            // Any car, the car saved for it, and the car being driven now when that's another one.
            var cars = new List<string?> { null };
            if (layout.AutoCar is { } saved)
            {
                cars.Add(saved);
            }

            if (CurrentCar is { } current && !string.Equals(current, layout.AutoCar, StringComparison.OrdinalIgnoreCase))
            {
                cars.Add(current);
            }

            var carLabels = cars.Select(car => car ?? Loc.T("layouts.auto.anyCar")).ToList();
            var noRuleYet = !LayoutAutoOpen.HasRule(layout);
            group.Items.Add(new ChoiceSetting(
                layout.Name,
                noRuleYet ? Loc.T("layouts.auto.never") : null,
                SessionChoices.Select(SessionChoiceLabel).ToList(),
                sessionIndex,
                index => SetAutoOpen(id, SessionChoices[index], LayoutStore.Get(id)?.AutoCar)));
            group.Items.Add(new ChoiceSetting(
                Loc.T("layouts.auto.car"),
                CurrentCar is null && layout.AutoCar is null ? Loc.T("layouts.auto.car.hint") : null,
                carLabels,
                Math.Max(0, cars.FindIndex(car => string.Equals(car, layout.AutoCar, StringComparison.OrdinalIgnoreCase))),
                index => SetAutoOpen(id, LayoutStore.Get(id)?.AutoSessions ?? [], cars[index])));
        }

        return group;
    }

    private void SetAutoOpen(Guid id, IReadOnlyList<SessionKind> sessions, string? car)
    {
        if (!LayoutStore.SetAutoOpen(id, sessions, car))
        {
            return;
        }

        AppLog.Activity("Layouts", "Automatic opening changed", new Dictionary<string, string>
        {
            ["sessions"] = sessions.Count == 0 ? "-" : string.Join(",", sessions),
            ["car"] = car ?? "-",
        });
        if (_drivingContext != default)
        {
            ApplyAutoLayouts();
        }
    }
}
