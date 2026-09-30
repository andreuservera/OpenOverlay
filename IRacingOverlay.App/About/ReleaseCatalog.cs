using System.IO;
using System.Reflection;
using IRacingOverlay.App.Diagnostics;

namespace IRacingOverlay.App.About;

/// <summary>CHANGELOG.md as embedded in this build, parsed on first use.</summary>
public static class ReleaseCatalog
{
    public const string ResourceName = "OpenOverlay.CHANGELOG.md";

    private static readonly Lazy<Changelog> Embedded = new(() => Load(typeof(ReleaseCatalog).Assembly));

    public static Changelog Changelog => Embedded.Value;

    /// <summary>What's New: the newest entry, which is the version this build is.</summary>
    public static ChangelogEntry? Current => Changelog.Current;

    /// <summary>Never throws: without its notes the app still runs, and the pages say so.</summary>
    internal static Changelog Load(Assembly assembly)
    {
        try
        {
            using var stream = assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                AppLog.Warn("Release notes", "This build has no embedded changelog");
                return Changelog.Empty;
            }

            using var reader = new StreamReader(stream);
            return Changelog.Parse(reader.ReadToEnd());
        }
        catch (Exception e) when (!ExceptionPolicy.IsFatal(e))
        {
            AppLog.Warn("Release notes", "Embedded changelog unreadable", e);
            return Changelog.Empty;
        }
    }
}
