using System.Text.Json;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.Tests;

public sealed class WidgetScaleTests
{
    [Fact]
    public void Default_IsTheStandardSize()
    {
        Assert.Equal(100, default(WidgetScale).Percent);
        Assert.Equal(1.15, WidgetScale.Default.Factor, 6);
    }

    [Theory]
    [InlineData(10, 65)]
    [InlineData(65, 65)]
    [InlineData(143, 143)]
    [InlineData(200, 200)]
    [InlineData(500, 200)]
    public void Percent_StaysWithinTheRange(int asked, int expected)
    {
        Assert.Equal(expected, new WidgetScale(asked).Percent);
    }

    [Theory]
    [InlineData("117", 117)]
    [InlineData(" 117 % ", 117)]
    [InlineData("XXS", 65)]
    [InlineData("M", 100)]
    [InlineData("XL", 139)]
    [InlineData("XXXL", 196)]
    [InlineData("3XL", 196)]
    public void Parse_TakesPercentagesAndTheOldSizeNames(string text, int expected)
    {
        Assert.Equal(expected, WidgetScale.Parse(text).Percent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("HUGE")]
    public void Parse_FallsBackToTheStandardSize(string? text)
    {
        Assert.Null(WidgetScale.TryParse(text));
        Assert.Equal(WidgetScale.Default, WidgetScale.Parse(text));
    }

    [Fact]
    public void Steps_StopAtTheEnds()
    {
        Assert.Equal(105, WidgetScale.Default.Larger().Percent);
        Assert.Equal(99, WidgetScale.Default.Smaller(1).Percent);
        Assert.True(new WidgetScale(66).Smaller().IsSmallest);
        Assert.True(new WidgetScale(199).Larger().IsLargest);
    }

    [Fact]
    public void Json_WritesTheNumber_AndReadsNumbersOrOldNames()
    {
        Assert.Equal("123", JsonSerializer.Serialize(new WidgetScale(123)));
        Assert.Equal(123, JsonSerializer.Deserialize<WidgetScale>("123").Percent);
        Assert.Equal(139, JsonSerializer.Deserialize<WidgetScale>("\"XL\"").Percent);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WidgetScale>("\"HUGE\""));
    }
}
