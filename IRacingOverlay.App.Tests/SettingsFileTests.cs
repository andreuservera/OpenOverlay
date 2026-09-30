using System.IO;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.Tests;

public sealed class SettingsFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "oo-settings-" + Guid.NewGuid().ToString("N"));

    public SettingsFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string PathOf(string name) => Path.Combine(_directory, name);

    [Fact]
    public void WriteJson_ThenReadJson_RoundTrips()
    {
        var path = PathOf("layout.json");

        Assert.True(SettingsFile.WriteJson(path, new Dictionary<string, double> { ["Fuel"] = 0.5 }));

        Assert.Equal(0.5, SettingsFile.ReadJson<Dictionary<string, double>>(path)!["Fuel"]);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Write_KeepsThePreviousVersionAsBackup()
    {
        var path = PathOf("units.txt");

        SettingsFile.Write(path, "Metric");
        SettingsFile.Write(path, "Imperial");

        Assert.Equal("Imperial", File.ReadAllText(path));
        Assert.Equal("Metric", File.ReadAllText(path + ".bak"));
    }

    [Fact]
    public void Read_CorruptFile_FallsBackToTheBackupAndSetsTheCorruptCopyAside()
    {
        var path = PathOf("layout.json");
        SettingsFile.WriteJson(path, new Dictionary<string, int> { ["Relative"] = 1 });
        SettingsFile.WriteJson(path, new Dictionary<string, int> { ["Relative"] = 2 });
        File.WriteAllText(path, "{ \"Relative\": 2, \"trunc");

        var restored = SettingsFile.ReadJson<Dictionary<string, int>>(path);

        Assert.Equal(1, restored!["Relative"]);
        Assert.True(File.Exists(path + ".corrupt"));

        // The next save must not rotate the corrupt copy over the good backup.
        SettingsFile.WriteJson(path, restored);
        Assert.Equal(1, SettingsFile.ReadJson<Dictionary<string, int>>(path)!["Relative"]);
    }

    [Fact]
    public void Read_EmptyOrNullJson_FallsBackToTheBackup()
    {
        var path = PathOf("flags.json");
        SettingsFile.WriteJson(path, new Dictionary<string, bool> { ["Blue"] = false });
        SettingsFile.WriteJson(path, new Dictionary<string, bool> { ["Blue"] = true });
        File.WriteAllText(path, "null");

        Assert.False(SettingsFile.ReadJson<Dictionary<string, bool>>(path)!["Blue"]);
    }

    [Fact]
    public void Read_NothingOnDisk_IsNull()
    {
        Assert.Null(SettingsFile.ReadJson<Dictionary<string, int>>(PathOf("missing.json")));
        Assert.Null(SettingsFile.ReadText(PathOf("missing.txt")));
    }

    [Fact]
    public void Write_Unwritable_ReportsFailureWithoutThrowing()
    {
        // A file where the folder should be: every attempt fails with an IOException.
        var blocker = PathOf("blocker");
        File.WriteAllText(blocker, "");
        var failuresBefore = SettingsFile.WriteFailures;

        var saved = SettingsFile.Write(Path.Combine(blocker, "hotkeys.json"), "{}");

        Assert.False(saved);
        Assert.True(SettingsFile.WriteFailures > failuresBefore);
        Assert.Contains("hotkeys.json", SettingsFile.LastWriteError);
    }

    [Fact]
    public void WriteJson_UnserializableValue_ReportsFailureWithoutThrowing()
    {
        var path = PathOf("opacity.json");

        Assert.False(SettingsFile.WriteJson(path, new Dictionary<string, double> { ["Fuel"] = double.NaN }));
        Assert.False(File.Exists(path));
    }
}
