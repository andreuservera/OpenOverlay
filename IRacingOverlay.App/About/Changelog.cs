using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IRacingOverlay.App.About;

/// <summary>One bullet, with the bullets indented under it.</summary>
public sealed record ChangelogItem(string Text, IReadOnlyList<string> Children);

/// <summary>A heading inside a version ("Added", "Fixed", "Reliability"…) and its bullets, as written.</summary>
public sealed record ChangelogSection(string Title, IReadOnlyList<ChangelogItem> Items)
{
    /// <summary>A section a driver must not miss, such as "Breaking Changes".</summary>
    public bool IsWarning => Title.Contains("breaking", StringComparison.OrdinalIgnoreCase);
}

public sealed record ChangelogEntry(ReleaseVersion Version, DateOnly? Date, string? Summary, IReadOnlyList<ChangelogSection> Sections)
{
    public int ItemCount => Sections.Sum(section => section.Items.Count);
}

/// <summary>
/// CHANGELOG.md as written: one entry per "[X.Y.Z]" heading, in file order (newest first), with the
/// headings and bullets under it. Any other heading — the title, "[Unreleased]" — and what follows
/// it are ignored. Parsing never throws.
/// </summary>
public sealed partial class Changelog(IReadOnlyList<ChangelogEntry> entries)
{
    public static Changelog Empty { get; } = new([]);

    public IReadOnlyList<ChangelogEntry> Entries { get; } = entries;

    /// <summary>The newest entry, which is this build's version: the build reads its version from it.</summary>
    public ChangelogEntry? Current => Entries.Count > 0 ? Entries[0] : null;

    public static Changelog Parse(string markdown)
    {
        var entries = new List<ChangelogEntry>();
        Entry? entry = null;
        foreach (var line in Comment().Replace(markdown.TrimStart('\uFEFF').Replace("\r\n", "\n"), "").Split('\n'))
        {
            if (Heading().Match(line) is not { Success: true } heading)
            {
                if (entry is not null && !Ignored().IsMatch(line))
                {
                    entry.Add(line);
                }

                continue;
            }

            var level = heading.Groups["hashes"].Length;
            var title = heading.Groups["title"].Value;
            if (VersionTitle().Match(title) is { Success: true } version &&
                ReleaseVersion.TryParse(version.Groups["version"].Value, out var parsed))
            {
                entry?.AddTo(entries);
                entry = new Entry(parsed, ParseDate(version.Groups["rest"].Value), level);
            }
            else if (entry is not null && level > entry.Level)
            {
                entry.StartSection(ToPlainText(title));
            }
            else
            {
                entry?.AddTo(entries);
                entry = null;
            }
        }

        entry?.AddTo(entries);
        return new Changelog(entries);
    }

    private static DateOnly? ParseDate(string text) =>
        IsoDate().Match(text) is { Success: true } date &&
        DateOnly.TryParseExact(date.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    /// <summary>Inline markdown reduced to what a reader sees: links keep their label, emphasis and
    /// code markers go, escapes are resolved.</summary>
    private static string ToPlainText(string markdown)
    {
        var text = InlineLink().Replace(markdown, "${label}");
        text = ReferenceLink().Replace(text, "${label}");
        text = AutoLink().Replace(text, "${url}");
        text = StrongStar().Replace(text, "${text}");
        text = StrongUnderscore().Replace(text, "${text}");
        text = EmphasisStar().Replace(text, "${text}");
        text = EmphasisUnderscore().Replace(text, "${text}");
        text = Code().Replace(text, "${text}");
        text = Escape().Replace(text, "${char}");
        return Whitespace().Replace(text, " ").Trim();
    }

    private sealed class Entry(ReleaseVersion version, DateOnly? date, int level)
    {
        private readonly List<string> _summary = [];
        private readonly List<(string Title, List<(StringBuilder Text, List<string> Children)> Items)> _sections = [];
        private bool _afterBlank = true;

        public int Level => level;

        public void StartSection(string title)
        {
            _sections.Add((title, []));
            _afterBlank = true;
        }

        public void Add(string line)
        {
            var text = line.Trim();
            if (text.Length == 0)
            {
                _afterBlank = true;
                return;
            }

            if (Bullet().Match(line) is { Success: true } bullet)
            {
                if (_sections.Count == 0)
                {
                    StartSection("");
                }

                var items = _sections[^1].Items;
                var bulletText = bullet.Groups["text"].Value.Trim();
                if (bullet.Groups["indent"].Length >= 2 && items.Count > 0)
                {
                    items[^1].Children.Add(bulletText);
                }
                else
                {
                    items.Add((new StringBuilder(bulletText), []));
                }
            }
            else if (_sections.Count > 0 && _sections[^1].Items is { Count: > 0 } items && !_afterBlank)
            {
                // A wrapped line continues the bullet above it.
                var last = items[^1];
                if (last.Children.Count > 0)
                {
                    last.Children[^1] += " " + text;
                }
                else
                {
                    last.Text.Append(' ').Append(text);
                }
            }
            else if (_sections.Count == 0)
            {
                _summary.Add(text);
            }
            else
            {
                _sections[^1].Items.Add((new StringBuilder(text), []));
            }

            _afterBlank = false;
        }

        public void AddTo(List<ChangelogEntry> entries) => entries.Add(new ChangelogEntry(
            version,
            date,
            _summary.Count == 0 ? null : ToPlainText(string.Join(' ', _summary)),
            _sections
                .Where(section => section.Items.Count > 0)
                .Select(section => new ChangelogSection(section.Title, section.Items
                    .Select(item => new ChangelogItem(ToPlainText(item.Text.ToString()), item.Children.Select(ToPlainText).ToList()))
                    .ToList()))
                .ToList()));
    }

    [GeneratedRegex(@"^(?<hashes>#{1,6})\s+(?<title>.*?)[\s#]*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\[?v?(?<version>\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?)\]?(?<rest>.*)$")]
    private static partial Regex VersionTitle();

    [GeneratedRegex(@"^(?<indent>\s*)[-*+]\s+(?<text>.*)$")]
    private static partial Regex Bullet();

    // Horizontal rules ("---") and link reference definitions.
    [GeneratedRegex(@"^\s*((-{3,}|\*{3,}|_{3,})|\[[^\]]+\]:\s*\S.*)\s*$")]
    private static partial Regex Ignored();

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}")]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"!?\[(?<label>[^\]]*)\]\([^)]*\)")]
    private static partial Regex InlineLink();

    [GeneratedRegex(@"\[(?<label>[^\]]+)\]\[[^\]]*\]")]
    private static partial Regex ReferenceLink();

    [GeneratedRegex(@"<(?<url>https?://[^>\s]+)>")]
    private static partial Regex AutoLink();

    [GeneratedRegex(@"\*\*(?=\S)(?<text>.+?)(?<=\S)\*\*")]
    private static partial Regex StrongStar();

    [GeneratedRegex(@"(?<!\w)__(?=\S)(?<text>.+?)(?<=\S)__(?!\w)")]
    private static partial Regex StrongUnderscore();

    [GeneratedRegex(@"(?<![\w*])\*(?=\S)(?<text>.+?)(?<=\S)\*(?![\w*])")]
    private static partial Regex EmphasisStar();

    [GeneratedRegex(@"(?<![\w_])_(?=\S)(?<text>.+?)(?<=\S)_(?![\w_])")]
    private static partial Regex EmphasisUnderscore();

    [GeneratedRegex(@"`(?<text>[^`]*)`")]
    private static partial Regex Code();

    [GeneratedRegex(@"\\(?<char>[\\`*_{}\[\]()#+\-.!|<>])")]
    private static partial Regex Escape();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
