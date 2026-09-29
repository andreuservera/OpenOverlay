using System.IO;
using System.Text.Json;

namespace IRacingOverlay.App.Overlay;

/// <summary>What the Control Panel's X button does. Index order matches the Control Panel's choices.</summary>
public enum CloseBehavior
{
    MinimizeToTray,
    Exit,
}

/// <summary>Persists the close behaviour and whether the one-time "still running" notice was shown.</summary>
internal static class TrayPreferencesStore
{
    private sealed record Snapshot(CloseBehavior CloseBehavior, bool TrayNoticeShown);

    public static readonly string ConfigFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IRacingOverlay");

    private static readonly string FilePath = Path.Combine(ConfigFolder, "tray.json");

    private static Snapshot? _cache;

    public static CloseBehavior CloseBehavior => Load().CloseBehavior;

    public static bool TrayNoticeShown => Load().TrayNoticeShown;

    public static void SaveCloseBehavior(CloseBehavior behavior) => Save(Load() with { CloseBehavior = behavior });

    public static void MarkTrayNoticeShown() => Save(Load() with { TrayNoticeShown = true });

    private static Snapshot Load()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        try
        {
            _cache = File.Exists(FilePath) ? JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(FilePath)) : null;
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            _cache = null;
        }

        return _cache ??= new Snapshot(CloseBehavior.MinimizeToTray, false);
    }

    private static void Save(Snapshot snapshot)
    {
        _cache = snapshot;
        Directory.CreateDirectory(ConfigFolder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(snapshot));
    }
}
