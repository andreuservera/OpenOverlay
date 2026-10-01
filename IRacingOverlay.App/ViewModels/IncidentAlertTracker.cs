namespace IRacingOverlay.App.ViewModels;

/// <summary>What just happened, e.g. 2 points of "LOSS OF CONTROL".</summary>
public sealed record IncidentAlert(int Points, string Label)
{
    public string Display => $"{Points}x {Label}";
}

/// <summary>
/// Turns the running incident count into "what did I just get". iRacing counts only the worst
/// incident of a chain — an off-track that becomes a spin is upgraded from 1x to 2x, arriving as +1
/// then +1 — so increments close together add up to one incident, as long as the total is a value
/// an upgrade can reach (1x→2x, 1x→4x, 2x→4x).
/// </summary>
internal sealed class IncidentAlertTracker
{
    private static readonly TimeSpan ChainWindow = TimeSpan.FromSeconds(5);

    private int? _lastCount;
    private int _chainPoints;
    private TimeSpan _chainAt;

    /// <summary>Forget the baseline: the next count is taken as-is, not as an incident.</summary>
    public void Reset()
    {
        _lastCount = null;
        _chainPoints = 0;
    }

    public IncidentAlert? Observe(int count, TimeSpan now)
    {
        // First reading, or a new session reset the count: nothing happened, it's the new baseline.
        if (_lastCount is not { } last || count < last)
        {
            _lastCount = count;
            _chainPoints = 0;
            return null;
        }

        _lastCount = count;
        var added = count - last;
        if (added == 0)
        {
            return null;
        }

        var upgrade = _chainPoints > 0 && now - _chainAt <= ChainWindow && (_chainPoints + added) is 2 or 4;
        _chainPoints = upgrade ? _chainPoints + added : added;
        _chainAt = now;
        return new IncidentAlert(_chainPoints, LabelOf(_chainPoints));
    }

    public static string LabelOf(int points) => points switch
    {
        1 => "OFF TRACK",
        2 => "LOSS OF CONTROL",
        4 => "CONTACT",
        _ => "INCIDENT",
    };
}
