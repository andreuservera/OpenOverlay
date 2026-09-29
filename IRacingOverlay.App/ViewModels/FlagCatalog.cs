namespace IRacingOverlay.App.ViewModels;

/// <summary>A labelled state of a flag, as offered in the preview and drawn in the widget.</summary>
public sealed record FlagVariantInfo(FlagVariant Variant, string Label, string Name, string Description);

/// <summary>
/// Everything the application knows about one kind of flag: how it looks, what it means, where it
/// ranks. Adding a flag is one entry in <see cref="FlagCatalog.All"/> plus its decode line in
/// <see cref="FlagBuilder"/> — settings, preview scenarios and rendering all follow from here.
/// </summary>
public sealed record FlagDefinition(
    FlagKind Kind,
    FlagGroup Group,
    string Label,
    string Name,
    string Description,
    FlagVisualStyle Style,
    string Background,
    string Foreground,
    string Accent)
{
    public string Glyph { get; init; } = "";

    /// <summary>Informational flags that hide after the configured duration; safety flags stay up
    /// for as long as they are out.</summary>
    public bool IsTransient { get; init; }

    public bool EnabledByDefault { get; init; } = true;

    public IReadOnlyList<FlagVariantInfo> Variants { get; init; } = [];

    public FlagState Create(FlagVariant variant, bool isPrimary)
    {
        var info = Variants.FirstOrDefault(v => v.Variant == variant);
        return new FlagState
        {
            Kind = Kind,
            Variant = variant,
            Name = info?.Name ?? Name,
            Description = info?.Description ?? Description,
            Style = Style,
            BackgroundColor = Background,
            ForegroundColor = Foreground,
            AccentColor = Accent,
            Glyph = Glyph,
            IsPrimary = isPrimary,
        };
    }
}

public static class FlagCatalog
{
    private const string FlagGreen = "#1FAA59";
    private const string FlagYellow = "#F7C600";
    private const string FlagRed = "#E03131";
    private const string FlagBlue = "#1C64D9";
    private const string FlagOrange = "#FF7A1A";
    private const string FlagWhite = "#F2F5F8";
    private const string FlagBlack = "#0B0D10";
    private const string Board = "#1B2129";

    private const string AccentGreen = "#34D399";
    private const string AccentRed = "#F04438";
    private const string AccentBlue = "#3D8BFF";
    private const string AccentNeutral = "#C4CCD4";

    /// <summary>In priority order: when flags compete for a group or for the widget's space, the
    /// earlier one wins, and the widget lists them in this order.</summary>
    public static IReadOnlyList<FlagDefinition> All { get; } =
    [
        new(FlagKind.Disqualified, FlagGroup.Driver, "Disqualified", "DISQUALIFIED", "Removed from the results",
            FlagVisualStyle.BlackWithCross, FlagBlack, FlagWhite, AccentRed)
        {
            Variants = [new(FlagVariant.ScoreVoided, "score voided", "DISQUALIFIED", "Score voided · leave the track")],
        },
        new(FlagKind.Black, FlagGroup.Driver, "Black", "BLACK FLAG", "Penalty · serve it in the pits",
            FlagVisualStyle.Solid, FlagBlack, FlagWhite, FlagWhite),
        new(FlagKind.Red, FlagGroup.Track, "Red", "RED FLAG", "Session stopped",
            FlagVisualStyle.Solid, FlagRed, FlagWhite, AccentRed),
        new(FlagKind.Meatball, FlagGroup.Driver, "Meatball", "MEATBALL", "Damage · pit for repairs",
            FlagVisualStyle.Meatball, FlagBlack, FlagOrange, FlagOrange),
        new(FlagKind.Furled, FlagGroup.Driver, "Furled black", "WARNING", "Next offence is a penalty",
            FlagVisualStyle.DiagonalSplit, FlagBlack, FlagWhite, FlagWhite),
        new(FlagKind.Checkered, FlagGroup.Race, "Checkered", "CHECKERED", "Session complete",
            FlagVisualStyle.Checkered, FlagWhite, FlagBlack, FlagWhite),
        new(FlagKind.Caution, FlagGroup.Track, "Caution", "CAUTION", "Full-course yellow · hold position",
            FlagVisualStyle.Solid, FlagYellow, FlagBlack, FlagYellow)
        {
            Variants = [new(FlagVariant.Waving, "waving", "CAUTION", "Caution called · slow, no passing")],
        },
        new(FlagKind.Yellow, FlagGroup.Track, "Yellow", "YELLOW", "Hazard ahead · no passing",
            FlagVisualStyle.Solid, FlagYellow, FlagBlack, FlagYellow)
        {
            Variants = [new(FlagVariant.Waving, "waving", "WAVING YELLOW", "Hazard close by · slow down")],
        },
        new(FlagKind.Debris, FlagGroup.Advisory, "Debris", "DEBRIS", "Slippery surface ahead",
            FlagVisualStyle.DebrisStripes, FlagYellow, FlagRed, FlagYellow),
        new(FlagKind.Blue, FlagGroup.Advisory, "Blue", "BLUE FLAG", "Faster car behind · let it by",
            FlagVisualStyle.BlueWithOrangeStripe, FlagBlue, FlagOrange, AccentBlue),
        new(FlagKind.White, FlagGroup.Race, "White", "WHITE FLAG", "Final lap",
            FlagVisualStyle.Solid, FlagWhite, FlagBlack, FlagWhite),
        new(FlagKind.OneLapToGreen, FlagGroup.Race, "One to green", "ONE TO GREEN", "Green flag next time by",
            FlagVisualStyle.LapBoard, Board, AccentGreen, AccentGreen) { Glyph = "1" },
        new(FlagKind.StartLights, FlagGroup.Track, "Start lights", "READY", "Start lights on",
            FlagVisualStyle.StartLights, Board, FlagRed, AccentRed)
        {
            Variants = [new(FlagVariant.LightsSet, "set", "SET", "Lights red · green is next")],
        },
        new(FlagKind.Green, FlagGroup.Track, "Green", "GREEN", "Track clear · race on",
            FlagVisualStyle.Solid, FlagGreen, FlagWhite, AccentGreen) { IsTransient = true },
        new(FlagKind.FiveToGo, FlagGroup.Race, "5 to go", "5 TO GO", "Five laps remaining",
            FlagVisualStyle.LapBoard, Board, FlagWhite, AccentNeutral) { Glyph = "5", IsTransient = true },
        new(FlagKind.TenToGo, FlagGroup.Race, "10 to go", "10 TO GO", "Ten laps remaining",
            FlagVisualStyle.LapBoard, Board, FlagWhite, AccentNeutral) { Glyph = "10", IsTransient = true },
        new(FlagKind.Crossed, FlagGroup.Race, "Halfway", "HALFWAY", "Crossed flags · half distance",
            FlagVisualStyle.CrossedFlags, Board, FlagWhite, AccentNeutral) { IsTransient = true },
        // iRacing sets this bit without documenting it; off unless someone asks for it.
        new(FlagKind.RandomWaving, FlagGroup.Advisory, "Waving flags", "FLAGS WAVING", "Flag stand is waving",
            FlagVisualStyle.Mixed, Board, FlagWhite, "#F5A524") { IsTransient = true, EnabledByDefault = false },
    ];

    private static readonly Dictionary<FlagKind, (FlagDefinition Definition, int Priority)> ByKind =
        All.Select((definition, index) => (definition, index)).ToDictionary(x => x.definition.Kind, x => (x.definition, x.index));

    public static FlagDefinition Get(FlagKind kind) => ByKind[kind].Definition;

    public static int PriorityOf(FlagKind kind) => ByKind[kind].Priority;
}
