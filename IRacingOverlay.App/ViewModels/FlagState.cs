namespace IRacingOverlay.App.ViewModels;

/// <summary>Every flag the widget can show. Priority and grouping live in <see cref="FlagCatalog"/>,
/// so the order here carries no meaning.</summary>
public enum FlagKind
{
    None,
    Green,
    Yellow,
    Caution,
    Red,
    Debris,
    White,
    Checkered,
    Crossed,
    TenToGo,
    FiveToGo,
    OneLapToGreen,
    StartLights,
    RandomWaving,
    Blue,
    Black,
    Furled,
    Meatball,
    Disqualified,
}

/// <summary>A state of a flag that changes how it reads but not what it is (a waving yellow is still
/// a yellow for the user's on/off choice).</summary>
public enum FlagVariant
{
    Default,
    Waving,
    ScoreVoided,
    LightsSet,
}

/// <summary>Flags in the same exclusive group never show together — the most important one speaks
/// for the group. Advisories are the exception: they stack.</summary>
public enum FlagGroup
{
    Driver,
    Track,
    Race,
    Advisory,
}

public enum FlagVisualStyle
{
    Solid,
    Checkered,
    Meatball,
    DebrisStripes,
    BlueWithOrangeStripe,
    DiagonalSplit,
    BlackWithCross,
    CrossedFlags,
    LapBoard,
    StartLights,
    Mixed,
    Placeholder,
}

/// <summary>A flag decoded from telemetry (or chosen in the preview), before any display policy.</summary>
public readonly record struct ActiveFlag(FlagKind Kind, FlagVariant Variant = FlagVariant.Default);

/// <summary>One flag ready to draw: everything the icon and the row need, resolved from the catalog.</summary>
public sealed class FlagState
{
    public required FlagKind Kind { get; init; }
    public FlagVariant Variant { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public required FlagVisualStyle Style { get; init; }
    public required string BackgroundColor { get; init; }
    public required string ForegroundColor { get; init; }

    /// <summary>Colour of the row's edge bar and wash. Differs from the flag itself where the flag's
    /// own colour would vanish on a dark panel (black, white-on-black).</summary>
    public string AccentColor { get; init; } = "#8E99A5";

    /// <summary>Numeral drawn on a lap board ("10", "5", "1").</summary>
    public string Glyph { get; init; } = "";

    /// <summary>The most important flag on screen, drawn a size up from the rest.</summary>
    public bool IsPrimary { get; init; }

    public string Key => $"{Kind}:{Variant}:{IsPrimary}";

    public static FlagState None { get; } = new()
    {
        Kind = FlagKind.None,
        Name = "NO FLAG",
        Description = "Nothing out on track",
        Style = FlagVisualStyle.Placeholder,
        BackgroundColor = "#161B22",
        ForegroundColor = "#6B7682",
        AccentColor = "#6B7682",
    };
}
