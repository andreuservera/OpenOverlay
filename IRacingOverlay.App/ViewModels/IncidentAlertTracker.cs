namespace IRacingOverlay.App.ViewModels;

/// <summary>What just happened, e.g. 2 points of "LOSS OF CONTROL".</summary>
/// <param name="IsCorrection">The sim's own report arrived a moment after the count changed: same
/// incident, better name. Relabel what's on screen rather than announce it again.</param>
public sealed record IncidentAlert(int Points, string Label, bool IsCorrection = false)
{
    public string Display => $"{Points}x {Label}";
}

/// <summary>
/// Turns the running incident count into "what did I just get". iRacing counts only the worst
/// incident of a chain — an off-track that becomes a spin is upgraded from 1x to 2x, arriving as +1
/// then +1 — so increments close together add up to one incident, as long as the total is a value
/// an upgrade can reach (1x→2x, 1x→4x, 2x→4x).
///
/// The count says how much; iRacing's PlayerIncidents log (<see cref="IncidentReport"/>) says what.
/// Without it — older sim builds — the name is inferred from the points, which is only certain for
/// 1x: 2x can be a spin or a hit wall, 4x is a car-to-car collision on a road course.
/// </summary>
internal sealed class IncidentAlertTracker
{
    private static readonly TimeSpan ChainWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CorrectionWindow = TimeSpan.FromSeconds(2);

    private int? _lastCount;
    private int _chainPoints;
    private TimeSpan _chainAt;
    private long _consumedReport;

    /// <summary>Forget the baseline: the next count is taken as-is, not as an incident.</summary>
    public void Reset()
    {
        _lastCount = null;
        _chainPoints = 0;
    }

    public IncidentAlert? Observe(int count, TimeSpan now, IncidentReport? report = null)
    {
        var fresh = report is not null && report.Sequence != _consumedReport ? report : null;
        if (fresh is not null)
        {
            _consumedReport = fresh.Sequence;
        }

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
            if (fresh is null)
            {
                return null;
            }

            // Light contact: logged, but it costs nothing, so the count never moves.
            if (fresh.Points == 0)
            {
                return new IncidentAlert(0, fresh.Label);
            }

            return _chainPoints > 0 && fresh.Points == _chainPoints && now - _chainAt <= CorrectionWindow
                ? new IncidentAlert(_chainPoints, fresh.Label, IsCorrection: true)
                : null;
        }

        var upgrade = _chainPoints > 0 && now - _chainAt <= ChainWindow && (_chainPoints + added) is 2 or 4;
        _chainPoints = upgrade ? _chainPoints + added : added;
        _chainAt = now;
        return new IncidentAlert(_chainPoints, fresh is { Points: > 0 } ? fresh.Label : LabelOf(_chainPoints));
    }

    /// <summary>The name inferred from the points alone, for when the sim doesn't say.</summary>
    public static string LabelOf(int points) => points switch
    {
        1 => "OFF TRACK",
        2 => "LOSS OF CONTROL",
        4 => "CONTACT",
        _ => "INCIDENT",
    };
}
