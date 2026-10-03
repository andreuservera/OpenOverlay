using IRacingOverlay.App.Layouts;

namespace IRacingOverlay.App.Tests;

public sealed class MonitorCatalogTests
{
    private const string UltrawidePath = @"\\?\DISPLAY#PHLC347#5&378f1b5b&0&UID24834#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string SidePath = @"\\?\DISPLAY#ACI24A4#5&378f1b5b&0&UID24837#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    private static readonly DisplayMonitor Ultrawide = new(UltrawidePath, "PHLC347", "34M2C3500L", @"\\.\DISPLAY1", 0, 0, 3440, 1440, IsPrimary: true);
    private static readonly DisplayMonitor Side = new(SidePath, "ACI24A4", "VG248", @"\\.\DISPLAY2", 3440, 357, 1920, 1080, IsPrimary: false);

    [Fact]
    public void Resolve_ExactDevicePath_FindsThatMonitor()
    {
        var result = Catalog(Ultrawide, Side).Resolve(Side.ToRef())!;

        Assert.Same(Side, result.Monitor);
        Assert.Equal(MonitorMatch.DevicePath, result.Match);
        Assert.False(result.IsFallback);
    }

    [Fact]
    public void Resolve_SameModelOnAnotherPort_FindsItByEdid()
    {
        var moved = Side with { DevicePath = SidePath.Replace("UID24837", "UID99999") };

        var result = Catalog(Ultrawide, moved).Resolve(Side.ToRef())!;

        Assert.Same(moved, result.Monitor);
        Assert.Equal(MonitorMatch.Edid, result.Match);
    }

    [Fact]
    public void Resolve_TwoMonitorsOfTheSameModel_FallsBackToThePrimaryRatherThanGuessing()
    {
        var twinA = Side with { DevicePath = SidePath.Replace("UID24837", "UID1"), GdiDeviceName = @"\\.\DISPLAY2" };
        var twinB = Side with { DevicePath = SidePath.Replace("UID24837", "UID2"), GdiDeviceName = @"\\.\DISPLAY3" };

        var result = Catalog(twinA, Ultrawide, twinB).Resolve(Side.ToRef())!;

        Assert.Same(Ultrawide, result.Monitor);
        Assert.True(result.IsFallback);
    }

    [Fact]
    public void Resolve_MonitorNotConnected_FallsBackToThePrimary()
    {
        var result = Catalog(Side with { IsPrimary = true }).Resolve(Ultrawide.ToRef())!;

        Assert.Equal("VG248", result.Monitor.FriendlyName);
        Assert.Equal(MonitorMatch.PrimaryFallback, result.Match);
    }

    [Fact]
    public void Resolve_NoMonitorsAtAll_IsNull()
    {
        Assert.Null(Catalog().Resolve(Ultrawide.ToRef()));
    }

    [Theory]
    [InlineData(UltrawidePath, "PHLC347")]
    [InlineData(@"\\?\display#acI24a4#5&1&0&UID1#{guid}", "ACI24A4")]
    [InlineData(@"\\.\DISPLAY1", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void EdidIdFromDevicePath_ReadsTheModelSegment(string? path, string? expected)
    {
        Assert.Equal(expected, MonitorCatalog.EdidIdFromDevicePath(path));
    }

    private static MonitorCatalog Catalog(params DisplayMonitor[] monitors) => new(new FixedSource(monitors));

    private sealed class FixedSource(IReadOnlyList<DisplayMonitor> monitors) : IMonitorSource
    {
        public IReadOnlyList<DisplayMonitor> GetMonitors() => monitors;
    }
}
