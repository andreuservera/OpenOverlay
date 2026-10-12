using IRacingOverlay.App.Layouts;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.Tests;

public sealed class LayoutGeometryTests
{
    private const double Canvas = 1000;

    [Fact]
    public void Snap_EdgeNearAnotherWidgetsEdge_AlignsAndDrawsAGuide()
    {
        var other = new Box(100, 100, 200, 100);
        var moving = new Box(304, 500, 50, 50); // left edge 4 px from the other's right edge

        var result = LayoutGeometry.Snap(moving, [other], Canvas, Canvas, threshold: 6, grid: null);

        Assert.Equal(300, result.X);
        Assert.Contains(new Guide(IsVertical: true, 300), result.Guides);
    }

    [Fact]
    public void Snap_CentreNearTheCanvasCentre_Centres()
    {
        var moving = new Box(473, 700, 50, 50); // centre at 498

        var result = LayoutGeometry.Snap(moving, [], Canvas, Canvas, threshold: 6, grid: null);

        Assert.Equal(475, result.X);
        Assert.Contains(new Guide(IsVertical: true, 500), result.Guides);
    }

    [Fact]
    public void Snap_NothingNearby_FallsBackToTheGrid()
    {
        var moving = new Box(233, 417, 50, 50);

        var result = LayoutGeometry.Snap(moving, [], Canvas, Canvas, threshold: 6, grid: 10);

        Assert.Equal((230.0, 420.0), (result.X, result.Y));
        Assert.Empty(result.Guides);
    }

    [Fact]
    public void Snap_NoGridAndNothingNearby_LeavesThePositionAlone()
    {
        var moving = new Box(233, 417, 50, 50);

        var result = LayoutGeometry.Snap(moving, [], Canvas, Canvas, threshold: 6, grid: null);

        Assert.Equal((233.0, 417.0), (result.X, result.Y));
    }

    [Fact]
    public void Snap_AlignmentWinsOverTheGrid()
    {
        var other = new Box(0, 0, 303, 100);
        var moving = new Box(305, 500, 50, 50);

        var result = LayoutGeometry.Snap(moving, [other], Canvas, Canvas, threshold: 6, grid: 10);

        Assert.Equal(303, result.X);
    }

    [Fact]
    public void Snap_TopEdge_AlignsWithTheCanvasTop()
    {
        var moving = new Box(400, 3, 50, 50);

        var result = LayoutGeometry.Snap(moving, [], Canvas, Canvas, threshold: 6, grid: null);

        Assert.Equal(0, result.Y);
        Assert.Contains(new Guide(IsVertical: false, 0), result.Guides);
    }

    [Theory]
    [InlineData(0.1, 65)]
    [InlineData(1.0, 87)]
    [InlineData(1.15, 100)]
    [InlineData(1.4, 122)]
    [InlineData(9.0, 200)]
    public void NearestLevel_PicksTheClosestPercent_WithinTheRange(double factor, int expectedPercent)
    {
        Assert.Equal(expectedPercent, LayoutGeometry.NearestLevel(factor).Percent);
    }

    [Theory]
    [InlineData(0, 0, 100, 100, false)]
    [InlineData(900, 900, 100, 100, false)]
    [InlineData(-1, 0, 100, 100, true)]
    [InlineData(901, 0, 100, 100, true)]
    [InlineData(0, 950, 100, 100, true)]
    public void IsOffCanvas_FlagsAnyPartOutside(double x, double y, double width, double height, bool expected)
    {
        Assert.Equal(expected, LayoutGeometry.IsOffCanvas(new Box(x, y, width, height), Canvas, Canvas));
    }
}
