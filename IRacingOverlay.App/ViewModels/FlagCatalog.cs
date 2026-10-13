namespace IRacingOverlay.App.ViewModels;

/// <summary>A labelled state of a flag, as offered in the preview and drawn in the widget. Its texts
/// come from the language files, under flag.{Kind}.{Variant}.</summary>
public sealed record FlagVariantInfo(FlagKind Kind, FlagVariant Variant)
{
    /// <summary>What the preview's picker calls this state ("waving").</summary>
    public string Label => Loc.T($"flag.{Kind}.{Variant}.label");

    public string Name => Loc.T($"flag.{Kind}.{Variant}.name");

    public string Description => Loc.T($"flag.{Kind}.{Variant}.description");
}

/// <summary>
/// Everything the application knows about one kind of flag: how it looks, what it means, where it
/// ranks. Adding a flag is one entry in <see cref="FlagCatalog.All"/> plus its decode line in
/// <see cref="FlagBuilder"/> — settings, preview scenarios and rendering all follow from here.
/// </summary>
public sealed record FlagDefinition(
    FlagKind Kind,
    FlagGroup Group,
    FlagVisualStyle Style,
    string Background,
    string Foreground,
    string Accent)
{
    /// <summary>The flag in the settings and the preview's picker ("Yellow"). Its texts come from the
    /// language files, under flag.{Kind}.</summary>
    public string Label => Loc.T($"flag.{Kind}.label");

    /// <summary>What the widget writes across the flag ("YELLOW").</summary>
    public string Name => Loc.T($"flag.{Kind}.name");

    /// <summary>What the flag asks of the driver, under its name.</summary>
    public string Description => Loc.T($"flag.{Kind}.description");

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
    private const string FlagGreen = "#3DD68C";
    private const string FlagYellow = "#F5A524";
    private const string FlagRed = "#E03131";
    private const string FlagBlue = "#1C64D9";
    private const string FlagOrange = "#FF7A1A";
    private const string FlagWhite = "#F2F4F7";
    private const string FlagBlack = "#0B0D10";
    private const string Board = "#1B2129";

    private const string AccentGreen = "#3DD68C";
    private const string AccentRed = "#F04438";
    private const string AccentBlue = "#3D8BFF";
    private const string AccentNeutral = "#B4BCC8";

    /// <summary>In priority order: when flags compete for a group or for the widget's space, the
    /// earlier one wins, and the widget lists them in this order.</summary>
    public static IReadOnlyList<FlagDefinition> All { get; } =
    [
        new(FlagKind.Disqualified, FlagGroup.Driver,
            FlagVisualStyle.BlackWithCross, FlagBlack, FlagWhite, AccentRed)
        {
            Variants = [new(FlagKind.Disqualified, FlagVariant.ScoreVoided)],
        },
        new(FlagKind.Black, FlagGroup.Driver,
            FlagVisualStyle.Solid, FlagBlack, FlagWhite, FlagWhite),
        new(FlagKind.Red, FlagGroup.Track,
            FlagVisualStyle.Solid, FlagRed, FlagWhite, AccentRed),
        new(FlagKind.Meatball, FlagGroup.Driver,
            FlagVisualStyle.Meatball, FlagBlack, FlagOrange, FlagOrange),
        new(FlagKind.Furled, FlagGroup.Driver,
            FlagVisualStyle.CornerCross, FlagBlack, FlagWhite, FlagWhite),
        new(FlagKind.Checkered, FlagGroup.Race,
            FlagVisualStyle.Checkered, FlagWhite, FlagBlack, FlagWhite),
        new(FlagKind.Caution, FlagGroup.Track,
            FlagVisualStyle.Solid, FlagYellow, FlagBlack, FlagYellow)
        {
            Variants = [new(FlagKind.Caution, FlagVariant.Waving)],
        },
        new(FlagKind.Yellow, FlagGroup.Track,
            FlagVisualStyle.Solid, FlagYellow, FlagBlack, FlagYellow)
        {
            Variants = [new(FlagKind.Yellow, FlagVariant.Waving)],
        },
        new(FlagKind.Debris, FlagGroup.Advisory,
            FlagVisualStyle.DebrisStripes, FlagYellow, FlagRed, FlagYellow),
        new(FlagKind.Blue, FlagGroup.Advisory,
            FlagVisualStyle.BlueWithOrangeStripe, FlagBlue, FlagOrange, AccentBlue),
        new(FlagKind.White, FlagGroup.Race,
            FlagVisualStyle.Solid, FlagWhite, FlagBlack, FlagWhite),
        new(FlagKind.OneLapToGreen, FlagGroup.Race,
            FlagVisualStyle.LapBoard, Board, AccentGreen, AccentGreen) { Glyph = "1" },
        new(FlagKind.StartLights, FlagGroup.Track,
            FlagVisualStyle.StartLights, Board, FlagRed, AccentRed)
        {
            Variants = [new(FlagKind.StartLights, FlagVariant.LightsSet)],
        },
        new(FlagKind.Green, FlagGroup.Track,
            FlagVisualStyle.Solid, FlagGreen, FlagWhite, AccentGreen) { IsTransient = true },
        new(FlagKind.FiveToGo, FlagGroup.Race,
            FlagVisualStyle.LapBoard, Board, FlagWhite, AccentNeutral) { Glyph = "5", IsTransient = true },
        new(FlagKind.TenToGo, FlagGroup.Race,
            FlagVisualStyle.LapBoard, Board, FlagWhite, AccentNeutral) { Glyph = "10", IsTransient = true },
        new(FlagKind.Crossed, FlagGroup.Race,
            FlagVisualStyle.CrossedFlags, Board, FlagWhite, AccentNeutral) { IsTransient = true },
        // iRacing sets this bit without documenting it; off unless someone asks for it.
        new(FlagKind.RandomWaving, FlagGroup.Advisory,
            FlagVisualStyle.Mixed, Board, FlagWhite, "#F5A524") { IsTransient = true, EnabledByDefault = false },
    ];

    private static readonly Dictionary<FlagKind, (FlagDefinition Definition, int Priority)> ByKind =
        All.Select((definition, index) => (definition, index)).ToDictionary(x => x.definition.Kind, x => (x.definition, x.index));

    public static FlagDefinition Get(FlagKind kind) => ByKind[kind].Definition;

    public static int PriorityOf(FlagKind kind) => ByKind[kind].Priority;
}
