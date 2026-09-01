namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// Everything the control panel needs to know about a widget that isn't behaviour: what to call it,
/// what to draw next to it, and how big its preview should be laid out. Registering a new widget is
/// one entry in <see cref="WidgetCatalog.All"/> plus one line in the view model's factory table —
/// the rail, the configuration pane and the preview are all driven from this list, so none of them
/// has to be touched.
/// </summary>
/// <param name="Key">Persistence key. Must match the name the widget passes to
/// <c>OverlayWindowBase</c>, since that is what WidgetVisibilityStore, HideOutsideCarStore and
/// WidgetLayoutStore are all keyed by.</param>
/// <param name="Name">Display name in the rail and as the configuration pane's title.</param>
/// <param name="Blurb">One line under the title saying what the widget is for. This is the only
/// documentation most users will ever read, so it says what it shows, not what it is called.</param>
/// <param name="IconData">Path geometry, drawn stroked in a 24x24 box.</param>
/// <param name="PreviewWidth">Design width at level M for panels that stretch to fill and so have no
/// meaningful natural width. Mirrors the DesignWidth the widget's own XAML declares; 0 means "let it
/// size to its content", which is right for every table and readout panel.</param>
/// <param name="PreviewHeight">As <paramref name="PreviewWidth"/>, for height.</param>
public sealed record WidgetDescriptor(
    string Key,
    string Name,
    string Blurb,
    string IconData,
    double PreviewWidth = 0,
    double PreviewHeight = 0)
{
    /// <summary>Key this widget's size level is persisted under. The prefix matches the
    /// PersistenceKey each widget's ScalablePanel declares, so the control panel and the widget's
    /// own +/- control read and write the same entry.</summary>
    public string ScaleKey => "Widget." + Key;
}

/// <summary>The registry every part of the control panel is generated from.</summary>
public static class WidgetCatalog
{
    public const string Relative = "Relative";
    public const string Standings = "Standings";
    public const string Cockpit = "Cockpit";
    public const string Flag = "Flag";
    public const string TireInfo = "TireInfo";
    public const string Delta = "Delta";
    public const string Fuel = "Fuel";
    public const string PedalTrace = "PedalTrace";
    public const string Incident = "Incident";
    public const string TrackInfo = "TrackInfo";
    public const string TrackMap = "TrackMap";
    public const string FuelCalculator = "FuelCalculator";

    /// <summary>Ordered as a driver would reach for them: the two timing tables first (the reason
    /// most people install an overlay at all), then the car, then the session, then strategy.</summary>
    public static IReadOnlyList<WidgetDescriptor> All { get; } =
    [
        new(Relative, "Relative", "Cars around you on track, closest first.",
            "M6,9 L12,4 L18,9 M6,15 L12,20 L18,15 M3,12 H21", PreviewWidth: 300),

        new(Standings, "Standings", "Running order with the podium pinned and your own battle in view.",
            "M3,20 H21 M6,20 V13 H10 V20 M10,20 V8 H14 V20 M14,20 V15 H18 V20"),

        new(Cockpit, "Cockpit", "Shift lights, gear, speed and the cars alongside you.",
            "M3,18 A9,9 0 0 1 21,18 M12,18 L16.5,11 M12,18 H12.01",
            PreviewWidth: 310, PreviewHeight: 150),

        new(Flag, "Flags", "Every flag currently being shown to you.",
            "M5,3 V21 M5,4 H18 L15.5,8.5 L18,13 H5"),

        new(TireInfo, "Tires", "Pressures, carcass temperatures and remaining tread, corner by corner.",
            "M12,3 A9,9 0 1 0 12.01,3 Z M12,8 A4,4 0 1 0 12.01,8 Z"),

        new(Delta, "Delta", "Live gap to your reference lap.",
            "M12,21 A8,8 0 1 0 12,5 A8,8 0 0 0 12,21 Z M12,9.5 V13 L14.5,15 M9.5,2.5 H14.5"),

        new(Fuel, "Fuel", "Level, burn rate and how many laps are left in the tank.",
            "M4,21 V5 A2,2 0 0 1 6,3 H11 A2,2 0 0 1 13,5 V21 M3,21 H14 M4,10 H13 M16,8 L19,11 V17"),

        new(PedalTrace, "Pedal trace", "Throttle, brake and clutch, with ABS activity marked.",
            "M3,17 L8,9 L12,14 L16,6 L21,12"),

        new(Incident, "Incidents", "Your incident count, and your team's in a team race.",
            "M12,4 L22,20 H2 Z M12,10 V15 M12,17.6 V17.8"),

        new(TrackInfo, "Track & session", "Weather, track state and what's left of the session.",
            "M3,12 A9,9 0 1 0 21,12 A9,9 0 1 0 3,12 Z M12,7.5 V7.7 M12,11 V16.5"),

        new(TrackMap, "Track map", "Where every car is around the lap, on one bar.",
            "M5,7 H13 A5,5 0 0 1 13,17 H9 A4,4 0 0 1 9,9 H19", PreviewWidth: 700),

        new(FuelCalculator, "Fuel calculator", "What you'll burn, what you need, and what to put in.",
            "M5,3 H19 V21 H5 Z M8,7 H16 M8,11.5 H10 M13.5,11.5 H16 M8,16 H10 M13.5,16 H16"),
    ];
}
