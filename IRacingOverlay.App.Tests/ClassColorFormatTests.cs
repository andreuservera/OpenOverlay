using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Tests;

public class ClassColorFormatTests
{
    [Fact]
    public void Normalize_0xPrefixedHex_ParsesCorrectly()
    {
        // Confirmed live: this is the actual format iRacing sends, not a bare hex string or a
        // "#"-prefixed one — the real bug this regression test guards against silently fell back to
        // white for every class because NumberStyles.HexNumber rejects a "0x" prefix.
        Assert.Equal("#33CEFF", ClassColorFormat.Normalize("0x33ceff"));
        Assert.Equal("#FFDA59", ClassColorFormat.Normalize("0xffda59"));
    }

    [Fact]
    public void Normalize_HashPrefixedHex_ParsesCorrectly()
    {
        Assert.Equal("#33CEFF", ClassColorFormat.Normalize("#33ceff"));
    }

    [Fact]
    public void Normalize_BareHex_ParsesCorrectly()
    {
        Assert.Equal("#33CEFF", ClassColorFormat.Normalize("33ceff"));
    }

    [Fact]
    public void Normalize_EmptyOrWhitespace_FallsBackToWhite()
    {
        Assert.Equal("#FFFFFF", ClassColorFormat.Normalize(""));
        Assert.Equal("#FFFFFF", ClassColorFormat.Normalize("   "));
    }

    [Fact]
    public void Normalize_Unparseable_FallsBackToWhite()
    {
        Assert.Equal("#FFFFFF", ClassColorFormat.Normalize("not-a-color"));
    }
}
