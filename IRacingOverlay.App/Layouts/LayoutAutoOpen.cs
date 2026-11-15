namespace IRacingOverlay.App.Layouts;

/// <summary>The kinds of session a layout can open itself for. Warm-ups and test drives count as
/// practice: the same widgets are wanted when nobody is racing.</summary>
public enum SessionKind
{
    Practice,
    Qualifying,
    Race,
}

/// <summary>
/// Picks the layout that opens by itself for what is being driven: each layout can name the kinds of
/// session it is for, a car, or both. A layout matches when everything it names fits; the one that
/// names the most wins (car and session over car alone over session alone), the first in the list
/// on a tie. A layout that names nothing never opens by itself.
/// </summary>
public static class LayoutAutoOpen
{
    /// <summary>iRacing's SessionType ("Open Qualify", "Lone Practice", "Race", "Warmup", "Offline
    /// Testing"…) as a kind; null for one it doesn't name.</summary>
    public static SessionKind? KindOf(string? sessionType)
    {
        var type = sessionType ?? "";
        if (type.Contains("Race", StringComparison.OrdinalIgnoreCase))
        {
            return SessionKind.Race;
        }

        if (type.Contains("Qualify", StringComparison.OrdinalIgnoreCase))
        {
            return SessionKind.Qualifying;
        }

        return type.Contains("Practice", StringComparison.OrdinalIgnoreCase) ||
               type.Contains("Warmup", StringComparison.OrdinalIgnoreCase) ||
               type.Contains("Testing", StringComparison.OrdinalIgnoreCase)
            ? SessionKind.Practice
            : null;
    }

    public static bool HasRule(Layout layout) => layout.AutoSessions.Count > 0 || !string.IsNullOrEmpty(layout.AutoCar);

    public static Layout? Pick(IEnumerable<Layout> layouts, SessionKind? kind, string? car)
    {
        Layout? best = null;
        var bestScore = 0;
        foreach (var layout in layouts)
        {
            if (!HasRule(layout))
            {
                continue;
            }

            var score = 0;
            if (layout.AutoSessions.Count > 0)
            {
                if (kind is not { } k || !layout.AutoSessions.Contains(k))
                {
                    continue;
                }

                score += 1;
            }

            if (!string.IsNullOrEmpty(layout.AutoCar))
            {
                if (!string.Equals(layout.AutoCar, car, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                score += 2;
            }

            if (score > bestScore)
            {
                best = layout;
                bestScore = score;
            }
        }

        return best;
    }
}
