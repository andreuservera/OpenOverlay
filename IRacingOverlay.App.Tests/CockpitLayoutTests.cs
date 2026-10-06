using System.Windows;
using IRacingOverlay.App.ViewModels;
using IRacingOverlay.App.Widgets.Cockpit;
using static IRacingOverlay.App.ViewModels.CockpitModule;

namespace IRacingOverlay.App.Tests;

public class CockpitLayoutTests
{
    private const double Gap = CockpitLayout.Gap;
    private const double Pad = CockpitLayout.Padding;

    public static TheoryData<CockpitModule[]> Combinations() => new()
    {
        new[] { Gear },
        new[] { Rpm },
        new[] { Rpm, Abs },
        new[] { Rpm, Abs, Fuel },
        new[] { Gear, Rpm, Speed },
        CockpitOptions.DefaultOrder.ToArray(),
        CockpitOptions.DefaultVisible.ToArray(),
    };

    [Theory]
    [MemberData(nameof(Combinations))]
    public void EveryModuleIsPlacedOnce_InOrder_LeftToRight(CockpitModule[] modules)
    {
        var layout = CockpitLayout.Compose(modules, shiftLights: true, radar: true);

        Assert.Equal(modules, layout.Cells.Select(cell => cell.Module));
        Assert.True(layout.Cells.Zip(layout.Cells.Skip(1)).All(pair =>
            pair.Second.Bounds.X > pair.First.Bounds.X ||
            (pair.Second.Bounds.X == pair.First.Bounds.X && pair.Second.Bounds.Y > pair.First.Bounds.Y)));
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void NoCellsOverlap(CockpitModule[] modules)
    {
        var cells = CockpitLayout.Compose(modules, shiftLights: true, radar: true).Cells;

        foreach (var (a, i) in cells.Select((cell, i) => (cell, i)))
        {
            foreach (var b in cells.Skip(i + 1))
            {
                var overlap = Rect.Intersect(a.Bounds, b.Bounds);
                Assert.True(overlap.IsEmpty || overlap.Width == 0 || overlap.Height == 0, $"{a.Module} overlaps {b.Module}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void TheCellsTileTheBody_WithNoHoles(CockpitModule[] modules)
    {
        var layout = CockpitLayout.Compose(modules, shiftLights: true, radar: true);
        var body = Rect.Union(layout.Cells.Select(cell => cell.Bounds).Aggregate(Rect.Union), layout.Cells[0].Bounds);
        var columns = layout.Cells.GroupBy(cell => cell.Bounds.X).OrderBy(column => column.Key).ToList();

        // Columns side by side, one gap apart, from the body's left edge to its right edge.
        Assert.Equal(body.Left, columns[0].Key);
        for (var i = 1; i < columns.Count; i++)
        {
            Assert.Equal(columns[i - 1].First().Bounds.Right + Gap, columns[i].Key, 6);
        }

        Assert.Equal(body.Right, columns[^1].First().Bounds.Right, 6);

        // Every column is filled top to bottom: one full-height card, or two rows and the gap.
        foreach (var column in columns)
        {
            Assert.Equal(CockpitLayout.BodyHeight, column.Sum(cell => cell.Bounds.Height) + ((column.Count() - 1) * Gap), 6);
            Assert.All(column, cell => Assert.Equal(column.First().Bounds.Width, cell.Bounds.Width, 6));
        }

        // The shift lights span exactly the modules.
        Assert.Equal(body.Left, layout.ShiftLights!.Value.Left, 6);
        Assert.Equal(body.Width, layout.ShiftLights!.Value.Width, 6);
    }

    [Fact]
    public void TwoSmallModulesInARow_ShareAColumn()
    {
        var cells = CockpitLayout.Compose([Rpm, Abs], shiftLights: false, radar: false).Cells;

        Assert.Equal(cells[0].Bounds.X, cells[1].Bounds.X);
        Assert.Equal(CockpitLayout.RowHeight, cells[0].Bounds.Height);
        Assert.False(cells[0].FullHeight);
        Assert.False(cells[1].FullHeight);
    }

    [Theory]
    [InlineData(new[] { Rpm })]
    [InlineData(new[] { Rpm, Abs, Fuel })]
    [InlineData(new[] { Gear, Rpm, Speed })]
    public void ASmallModuleOnItsOwn_FillsItsColumn(CockpitModule[] modules)
    {
        var layout = CockpitLayout.Compose(modules, shiftLights: false, radar: false);
        var alone = layout.Cells.Single(cell => cell.Module == (modules.Length == 3 && modules[0] == Rpm ? Fuel : Rpm));

        Assert.True(alone.FullHeight);
        Assert.Equal(CockpitLayout.BodyHeight, alone.Bounds.Height);
    }

    [Fact]
    public void AllModules_ArePairedTwoByTwoAfterTheLargeOnes()
    {
        var columns = CockpitLayout.Compose(CockpitOptions.DefaultVisible, shiftLights: true, radar: true).Cells
            .GroupBy(cell => cell.Bounds.X)
            .Select(column => column.Select(cell => cell.Module).ToArray())
            .ToList();

        Assert.Equal(
            [[Gear], [Speed], [Rpm, Abs], [Fuel, Inputs], [WaterTemp, OilTemp]],
            columns);
    }

    [Fact]
    public void ANarrowSelection_WidensToTheShiftLightsMinimum()
    {
        var layout = CockpitLayout.Compose([Gear], shiftLights: true, radar: false);

        Assert.Equal(CockpitLayout.MinBodyWidth, layout.Cells[0].Bounds.Width, 6);
        Assert.Equal(CockpitLayout.MinBodyWidth + (2 * Pad), layout.Size.Width, 6);
    }

    [Fact]
    public void Width_FollowsTheVisibleColumns()
    {
        var three = CockpitLayout.Compose([Gear, Speed, Rpm], shiftLights: true, radar: true).Size.Width;
        var all = CockpitLayout.Compose(CockpitOptions.DefaultOrder, shiftLights: true, radar: true).Size.Width;

        Assert.True(all > three);
    }

    [Fact]
    public void Radar_SpansTheFullHeight_OnBothSides()
    {
        var layout = CockpitLayout.Compose([Gear, Speed], shiftLights: true, radar: true);

        var inner = CockpitLayout.ShiftLightsHeight + Gap + CockpitLayout.BodyHeight;
        Assert.Equal(new Rect(Pad, Pad, CockpitLayout.RadarWidth, inner), layout.LeftRadar);
        Assert.Equal(layout.Size.Width - Pad, layout.RightRadar!.Value.Right);
        Assert.Equal(inner, layout.RightRadar!.Value.Height);
        Assert.Equal(inner + (2 * Pad), layout.Size.Height);
    }

    [Fact]
    public void LightsAndRadarOff_LeaveOnlyTheModules()
    {
        var layout = CockpitLayout.Compose([Gear, Speed], shiftLights: false, radar: false);

        Assert.Null(layout.ShiftLights);
        Assert.Null(layout.LeftRadar);
        Assert.Equal(Pad, layout.Cells[0].Bounds.X);
        Assert.Equal(Pad, layout.Cells[0].Bounds.Y);
        Assert.Equal(CockpitLayout.BodyHeight + (2 * Pad), layout.Size.Height);
    }

    [Fact]
    public void NoModules_ShowsOnlyTheFixedElements_OrNothing()
    {
        var lightsOnly = CockpitLayout.Compose([], shiftLights: true, radar: false);
        Assert.Empty(lightsOnly.Cells);
        Assert.Equal(new Size(CockpitLayout.MinBodyWidth + (2 * Pad), CockpitLayout.ShiftLightsHeight + (2 * Pad)), lightsOnly.Size);

        var both = CockpitLayout.Compose([], shiftLights: true, radar: true);
        Assert.Empty(both.Cells);
        Assert.NotNull(both.ShiftLights);
        Assert.NotNull(both.LeftRadar);

        Assert.Equal(new Size(0, 0), CockpitLayout.Compose([], shiftLights: false, radar: false).Size);
    }
}
