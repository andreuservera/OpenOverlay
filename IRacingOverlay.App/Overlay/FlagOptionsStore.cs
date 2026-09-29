using System.IO;
using System.Text.Json;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists the flag widget's options as one JSON object. Flag types are stored by name and
/// only when the user changed them, so reordering or adding flags never scrambles saved choices.</summary>
internal static class FlagOptionsStore
{
    private sealed record Snapshot(
        Dictionary<string, bool>? EnabledKinds,
        FlagDisplayMode DisplayMode,
        bool ShowName,
        bool ShowDescription,
        FlagLayout Layout,
        FlagIconPlacement IconPlacement,
        int MaxFlags,
        int InfoFlagSeconds);

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "flags.json");

    public static void ApplyTo(FlagOptions options)
    {
        var saved = Load();
        if (saved is null)
        {
            return;
        }

        foreach (var (name, enabled) in saved.EnabledKinds ?? [])
        {
            if (Enum.TryParse<FlagKind>(name, out var kind) && kind != FlagKind.None)
            {
                options.SetEnabled(kind, enabled);
            }
        }

        options.DisplayMode = saved.DisplayMode;
        options.ShowName = saved.ShowName;
        options.ShowDescription = saved.ShowDescription;
        options.Layout = saved.Layout;
        options.IconPlacement = saved.IconPlacement;
        options.MaxFlags = saved.MaxFlags;
        options.InfoFlagSeconds = saved.InfoFlagSeconds;
    }

    public static void Save(FlagOptions options)
    {
        var snapshot = new Snapshot(
            options.EnabledOverrides.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
            options.DisplayMode,
            options.ShowName,
            options.ShowDescription,
            options.Layout,
            options.IconPlacement,
            options.MaxFlags,
            options.InfoFlagSeconds);

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(snapshot));
    }

    private static Snapshot? Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(FilePath)) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
