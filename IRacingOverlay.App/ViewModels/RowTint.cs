namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Row backgrounds for the Standings and Relative tables, in one place so both read identically.
///
/// Tints are light-over-dark rather than dark-over-dark: lifting a row slightly above the panel
/// behind it is what makes it read as a band, the way broadcast timing screens do. Going darker
/// instead just muddies it into the panel.
/// </summary>
internal static class RowTint
{
    // The one row you must be able to find without reading anything.
    private const string Player = "#7A2A93FF";

    // A car being serviced isn't racing you this instant, so it sits closer to the panel than a
    // normal row: it recedes in brightness as well as shifting to a warm hue. Hue alone is a weak
    // signal — measured, an equally bright warm tint came out within 1.04:1 of a normal row.
    private const string PitRoad = "#1AD08A38";

    // Single-class sessions: every car shares one class colour, so tinting by it says nothing — and
    // iRacing hands out a washed-out grey for a lot of single-make series, which is where the dull
    // look came from. A cool near-white at low alpha gives a clean, neutral band instead.
    private const string Neutral = "#30C6D8EC";

    public static string For(bool isPlayer, bool onPitRoad, bool isMultiClass, string classColor)
    {
        if (isPlayer)
        {
            return Player;
        }

        if (onPitRoad)
        {
            return PitRoad;
        }

        // Multiclass keeps the class colour: there it carries real information, and iRacing's class
        // colours are genuinely distinct hues. The alpha is a compromise measured against the text
        // ramp — the hue has to read, but a bright class colour like LMP yellow at full strength
        // pushed the lap-time and delta colours below 4.5:1 on top of it.
        return isMultiClass ? $"#44{classColor.TrimStart('#')}" : Neutral;
    }
}
