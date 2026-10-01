using IRacingOverlay.App.About;
using IRacingOverlay.App.Diagnostics;

namespace IRacingOverlay.App.Tests;

public sealed class BuildInfoTests
{
    [Theory]
    [InlineData("installed", "win", "0.5.0", UpdateChannel.Stable)]
    [InlineData("installed", null, "0.5.0", UpdateChannel.Stable)]
    [InlineData("installed", "beta", "0.5.0", UpdateChannel.Preview)]
    [InlineData("installed", "win", "0.5.0-beta.1", UpdateChannel.Preview)]
    [InlineData("development", null, "0.5.0", UpdateChannel.Portable)]
    [InlineData("development", null, "0.5.0-beta.1", UpdateChannel.Portable)]
    public void Channel_FollowsHowTheCopyWasInstalledAndItsVersion(string installKind, string? velopackChannel, string? version, UpdateChannel expected)
    {
        Assert.Equal(expected, BuildInfo.ChannelFor(installKind, velopackChannel, ReleaseVersion.ParseOrNull(version)));
    }

    [Fact]
    public void Version_LeavesOutTheCommit()
    {
        Assert.DoesNotContain('+', BuildInfo.Version);
        Assert.NotNull(BuildInfo.Release);
    }
}
