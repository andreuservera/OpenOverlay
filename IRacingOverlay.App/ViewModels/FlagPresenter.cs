namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Display policy for flags: turns everything that is out into what the widget draws — filtered by
/// the user's choices, one flag per exclusive group, informational flags timed out, capped, and in
/// priority order. Stateful only for the timing, so each consumer holds its own instance.
/// </summary>
public sealed class FlagPresenter
{
    private readonly Dictionary<FlagKind, TimeSpan> _firstSeen = [];

    public IReadOnlyList<FlagState> Present(IReadOnlyList<ActiveFlag> active, FlagOptions options, TimeSpan now)
    {
        // A flag that goes away and comes back gets a fresh timer (a second restart shows green again).
        foreach (var kind in _firstSeen.Keys.Where(kind => !active.Any(a => a.Kind == kind)).ToList())
        {
            _firstSeen.Remove(kind);
        }

        foreach (var flag in active)
        {
            _firstSeen.TryAdd(flag.Kind, now);
        }

        var hold = options.InfoFlagHold;
        return Compose(
            active.Where(flag => hold <= TimeSpan.Zero
                || !FlagCatalog.Get(flag.Kind).IsTransient
                || now - _firstSeen[flag.Kind] < hold),
            options);
    }

    /// <summary>The selection itself, with no timing: also what the preview shows, since a preview
    /// is always "the moment the flag comes out".</summary>
    public static IReadOnlyList<FlagState> Compose(IEnumerable<ActiveFlag> active, FlagOptions options)
    {
        var chosen = new List<ActiveFlag>();
        var takenGroups = new HashSet<FlagGroup>();

        foreach (var flag in active.Where(f => options.IsEnabled(f.Kind)).OrderBy(f => FlagCatalog.PriorityOf(f.Kind)))
        {
            var group = FlagCatalog.Get(flag.Kind).Group;
            if (group != FlagGroup.Advisory && !takenGroups.Add(group))
            {
                continue;
            }

            chosen.Add(flag);
            if (chosen.Count >= options.MaxFlags)
            {
                break;
            }
        }

        return chosen.Select((flag, index) => FlagCatalog.Get(flag.Kind).Create(flag.Variant, isPrimary: index == 0)).ToList();
    }
}
