using IRacingOverlay.App.About;

namespace IRacingOverlay.App.Tests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("0.4.0", 0, 4, 0, "")]
    [InlineData("v1.2.3", 1, 2, 3, "")]
    [InlineData("0.4.0-beta.2", 0, 4, 0, "beta.2")]
    [InlineData("0.4.0-beta.2+8ad1c11", 0, 4, 0, "beta.2")]
    [InlineData(" 2.0.0-beta.1 ", 2, 0, 0, "beta.1")]
    [InlineData("10.20.30-rc-1", 10, 20, 30, "rc-1")]
    public void TryParse_ReadsTheCoreAndLabel_AndDropsBuildMetadata(string text, int major, int minor, int patch, string prerelease)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var version));
        Assert.Equal(new ReleaseVersion(major, minor, patch, prerelease), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Unreleased")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.x.3")]
    [InlineData("-1.2.3")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-beta..1")]
    [InlineData("1.2.3-beta_1")]
    public void TryParse_RejectsAnythingElse(string? text)
    {
        Assert.False(ReleaseVersion.TryParse(text, out _));
        Assert.Null(ReleaseVersion.ParseOrNull(text));
    }

    [Fact]
    public void Ordering_FollowsSemVerPrecedence()
    {
        var expected = new[]
        {
            "0.9.9", "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2",
            "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "2.0.0",
        }.Select(text => ReleaseVersion.ParseOrNull(text)!).ToList();

        var sorted = expected.AsEnumerable().Reverse().ToList();
        sorted.Sort();

        Assert.Equal(expected, sorted);
    }

    [Fact]
    public void IsPrerelease_TellsPreviewsFromReleases()
    {
        Assert.False(ReleaseVersion.ParseOrNull("0.4.0")!.IsPrerelease);
        Assert.True(ReleaseVersion.ParseOrNull("0.5.0-beta.1")!.IsPrerelease);
    }

    [Theory]
    [InlineData("v0.4.0", "0.4.0")]
    [InlineData("0.5.0-beta.1+8ad1c11", "0.5.0-beta.1")]
    public void ToString_IsTheNormalizedVersion(string text, string expected)
    {
        Assert.Equal(expected, ReleaseVersion.ParseOrNull(text)!.ToString());
    }
}
