using System.Globalization;

namespace IRacingOverlay.App.About;

/// <summary>
/// What the one-time notice after an update says: the new version and the first few lines of its
/// changelog entry, with the rest a click away. A breaking-changes section is never the teaser but
/// is always mentioned, since it's the part someone might need to act on.
/// </summary>
public sealed record UpdateNotice(
    string Title,
    string Subtitle,
    string? Heading,
    IReadOnlyList<string> Items,
    string? More,
    bool HasBreakingChanges)
{
    public const int MaxItems = 4;

    public static UpdateNotice For(string version, ChangelogEntry? entry)
    {
        var title = $"OpenOverlay has been updated to v{version}";
        var released = entry?.Date is { } date ? $"Released {date.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}. " : "";
        var sections = entry?.Sections ?? [];
        var teaser = sections.FirstOrDefault(section => !section.IsWarning) ?? sections.FirstOrDefault();
        if (teaser is null)
        {
            return new UpdateNotice(title, released + "The details are in What's New.", null, [], null, false);
        }

        var items = teaser.Items.Take(MaxItems).Select(item => item.Text).ToList();
        var remaining = entry!.ItemCount - items.Count;
        return new UpdateNotice(
            title,
            released + "Here's what's new.",
            teaser.Title.Length > 0 ? teaser.Title.ToUpperInvariant() : "CHANGES",
            items,
            remaining > 0 ? $"…and {remaining} more {(remaining == 1 ? "change" : "changes")}." : null,
            sections.Any(section => section.IsWarning));
    }
}
