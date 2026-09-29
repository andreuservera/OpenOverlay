using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.Tests;

[Collection("Diagnostics statics")]
public class DiagnosticsReportTests
{
    [Fact]
    public void Build_CoversEverySectionABugReportNeeds()
    {
        DiagnosticsReport.LatestContext = new DiagnosticsContext(
            "Spa · Race · subsession 12345",
            new ConnectionHealth
            {
                State = ConnectionState.Connected,
                LastTickUtc = DateTime.UtcNow,
                TicksReceived = 1000,
                Reconnects = 2,
                LastError = "read: IOException: gone",
                LastErrorUtc = DateTime.UtcNow,
            },
            ["Fuel: on, visible, size M, opacity 100 %"],
            "120 MB · GC 1/0/0");
        AppLog.Info("Test", "diagnostics report test entry");

        var report = DiagnosticsReport.Build("Test", new InvalidOperationException("boom"));

        Assert.Contains("OpenOverlay diagnostics report", report);
        Assert.Contains("== Application ==", report);
        Assert.Contains(AppInfo.Version, report);
        Assert.Contains("Spa · Race · subsession 12345", report);
        Assert.Contains("Reconnects    2", report);
        Assert.Contains("read: IOException: gone", report);
        Assert.Contains("== Health:", report);
        Assert.Contains("Fuel: on, visible", report);
        Assert.Contains("System.InvalidOperationException: boom", report);
        Assert.Contains("diagnostics report test entry", report);
    }

    [Fact]
    public void Redact_MasksTheUserProfileFolder()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var text = $@"Could not save {profile}\AppData\Local\IRacingOverlay\layout.json";

        var redacted = DiagnosticsReport.Redact(text);

        Assert.DoesNotContain(profile, redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"%USERPROFILE%\AppData\Local\IRacingOverlay\layout.json", redacted);
    }
}
