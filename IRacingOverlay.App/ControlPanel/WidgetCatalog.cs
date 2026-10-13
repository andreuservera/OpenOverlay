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
/// <param name="IconData">Path geometry, drawn stroked in a 24x24 box.</param>
/// <param name="PreviewWidth">Design width at level M for panels that stretch to fill and so have no
/// meaningful natural width. Mirrors the DesignWidth the widget's own XAML declares; 0 means "let it
/// size to its content", which is right for every table and readout panel.</param>
/// <param name="PreviewHeight">As <paramref name="PreviewWidth"/>, for height.</param>
/// <param name="Category">What the widget is for, which is the rail heading it is listed under.</param>
/// <param name="HasHeader">Whether its frame can show the header strip (name and live dot), and so
/// whether its page offers the switch. Every widget but the self-drawn Cockpit.</param>
/// <param name="HeaderByDefault">Whether the header shows before anyone touches the switch. Off for
/// the bars and the one-number readouts, which carry their own caption.</param>
public sealed record WidgetDescriptor(
    string Key,
    string IconData,
    double PreviewWidth = 0,
    double PreviewHeight = 0,
    WidgetCategory Category = WidgetCategory.Race,
    bool HasHeader = true,
    bool HeaderByDefault = true)
{
    /// <summary>Key this widget's size level is persisted under. The prefix matches the
    /// PersistenceKey each widget's ScalablePanel declares, so the control panel and the widget's
    /// own +/- control read and write the same entry.</summary>
    public string ScaleKey => "Widget." + Key;

    /// <summary>Text key of the display name in the rail and as the configuration pane's title.</summary>
    public string NameKey => $"widget.{Key}.name";

    /// <summary>Text key of the line under the title saying what the widget is for. This is the only
    /// documentation most users will ever read, so it says what it shows, not what it is called.</summary>
    public string BlurbKey => $"widget.{Key}.blurb";

    public string Name => Loc.T(NameKey);

    public string Blurb => Loc.T(BlurbKey);

    /// <summary>The name in English, for logs.</summary>
    public string LogName => Loc.English(NameKey);
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
    public const string PedalTrace = "PedalTrace";
    public const string Incident = "Incident";
    public const string TrackInfo = "TrackInfo";
    public const string Weather = "Weather";
    public const string TrackMap = "TrackMap";
    public const string FuelCalculator = "FuelCalculator";

    /// <summary>Grouped by what they are for — the race around you, your car and driving, the track
    /// and session — and within each group ordered as a driver would reach for them, the two timing
    /// tables first (the reason most people install an overlay at all). The rail lists them in this
    /// order under one heading per <see cref="WidgetCategory"/>.</summary>
    public static IReadOnlyList<WidgetDescriptor> All { get; } =
    [
        new(Relative, "M6,9 L12,4 L18,9 M6,15 L12,20 L18,15 M3,12 H21", PreviewWidth: 300),

        new(Standings, "M3,20 H21 M6,20 V13 H10 V20 M10,20 V8 H14 V20 M14,20 V15 H18 V20"),

        new(TrackMap, "M5,7 H13 A5,5 0 0 1 13,17 H9 A4,4 0 0 1 9,9 H19", PreviewWidth: 700, HeaderByDefault: false),

        new(Flag, "M5,3 V21 M5,4 H18 L15.5,8.5 L18,13 H5"),

        new(Incident, "M12,4 L22,20 H2 Z M12,10 V15 M12,17.6 V17.8", HeaderByDefault: false),

        new(Cockpit, "M3,18 A9,9 0 0 1 21,18 M12,18 L16.5,11 M12,18 H12.01", Category: WidgetCategory.Car, HasHeader: false),

        new(TireInfo, "M12,3 A9,9 0 1 0 12.01,3 Z M12,8 A4,4 0 1 0 12.01,8 Z", Category: WidgetCategory.Car),

        new(FuelCalculator, "M5,3 H19 V21 H5 Z M8,7 H16 M8,11.5 H10 M13.5,11.5 H16 M8,16 H10 M13.5,16 H16", Category: WidgetCategory.Car),

        new(Delta, "M12,21 A8,8 0 1 0 12,5 A8,8 0 0 0 12,21 Z M12,9.5 V13 L14.5,15 M9.5,2.5 H14.5", Category: WidgetCategory.Car, HeaderByDefault: false),

        new(PedalTrace, "M3,17 L8,9 L12,14 L16,6 L21,12", PreviewHeight: 110, Category: WidgetCategory.Car),

        // A map pin: the "i" in a circle is Help's.
        new(TrackInfo, "M12,21.5 C12,21.5 5,14.6 5,9.5 A7,7 0 0 1 19,9.5 C19,14.6 12,21.5 12,21.5 Z M12,7 A2.5,2.5 0 1 0 12.01,7 Z",
            Category: WidgetCategory.Track, HeaderByDefault: false),

        new(Weather, "M7,19 H17 A4,4 0 0 0 17,11 A5.5,5.5 0 0 0 6.6,12.2 A3.4,3.4 0 0 0 7,19 Z M14,3 V4.5 M18.6,5.4 L17.5,6.5 M20.5,10 H19",
            Category: WidgetCategory.Track),
    ];

    /// <summary>The rail heading for a category.</summary>
    public static string HeadingOf(WidgetCategory category) => category switch
    {
        WidgetCategory.Car => "CAR & DRIVING",
        WidgetCategory.Track => "TRACK & SESSION",
        _ => "RACE",
    };

    /// <summary>Whether this version of the app has a widget of that type. Saved data can name one
    /// that was since removed, or that only a newer version has.</summary>
    public static bool Contains(string type) => All.Any(descriptor => descriptor.Key == type);

    /// <summary>Whether a widget of that type shows its header before anyone has set it.</summary>
    public static bool HeaderByDefault(string type) =>
        All.FirstOrDefault(descriptor => descriptor.Key == type) is not { } descriptor || descriptor.HeaderByDefault;
}

/// <summary>What a widget is for: the rail lists widgets under one heading per category.</summary>
public enum WidgetCategory
{
    /// <summary>The race around you: timing, positions, flags, incidents.</summary>
    Race,

    /// <summary>Your car and how you drive it.</summary>
    Car,

    /// <summary>The track and the session: conditions and time.</summary>
    Track,
}
