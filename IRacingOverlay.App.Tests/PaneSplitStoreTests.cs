using System.IO;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.Tests;

/// <summary>The remembered split between settings and preview. PaneSplitStore is static, so these
/// run against a temporary file and never the real settings folder.</summary>
public sealed class PaneSplitStoreTests : IDisposable
{
    private readonly string _originalPath = PaneSplitStore.FilePath;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "oo-split-" + Guid.NewGuid().ToString("N"));

    public PaneSplitStoreTests()
    {
        Directory.CreateDirectory(_dir);
        PaneSplitStore.FilePath = Path.Combine(_dir, "pane-split.json");
        PaneSplitStore.Reset();
    }

    public void Dispose()
    {
        PaneSplitStore.FilePath = _originalPath;
        PaneSplitStore.Reset();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Split_IsFortySixty_UntilMoved()
    {
        Assert.Equal(0.4, PaneSplitStore.ConfigShare);
    }

    [Fact]
    public void Split_IsRemembered_AcrossRuns()
    {
        PaneSplitStore.Save(0.55);
        PaneSplitStore.Reset();

        Assert.Equal(0.55, PaneSplitStore.ConfigShare);
    }

    [Fact]
    public void Split_NeverHidesEitherPane()
    {
        PaneSplitStore.Save(1.0);
        PaneSplitStore.Reset();

        Assert.Equal(0.9, PaneSplitStore.ConfigShare, precision: 6);
    }
}
