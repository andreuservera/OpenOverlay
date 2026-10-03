using System.Collections.ObjectModel;
using System.Reflection;
using IRacingOverlay.App.ViewModels;
using IRacingOverlay.App.Widgets;

namespace IRacingOverlay.App.Tests;

public class RowSlotTests
{
    private static StandingsRow Row(double gap = 1.5, int position = 2, TireCompound? tire = null, PitStop? pit = null) => new()
    {
        CarIdx = 3,
        Position = position,
        ClassPosition = position,
        Name = "Driver",
        CarNumber = "7",
        IsPlayer = false,
        OnPitRoad = false,
        CurrentLap = 4,
        GapToLeaderSeconds = gap,
        LastLapTime = 90.1,
        BestLapTime = 89.9,
        IsMultiClass = false,
        IRating = 2000,
        LicString = "A 3.0",
        IRatingDelta = 12,
        IsSessionFastestLap = false,
        TireCompound = tire,
        LastPitStop = pit,
    };

    [Fact]
    public void FirstDifference_IsNullForAFreshRowWithTheSameValues()
    {
        Assert.Null(RowSlot.FirstDifference(
            Row(tire: new TireCompound("M", "#FFF", "Medium"), pit: new PitStop(3, 25.1)),
            Row(tire: new TireCompound("M", "#FFF", "Medium"), pit: new PitStop(3, 25.1))));
        Assert.Null(RowSlot.FirstDifference(Row(gap: double.NaN), Row(gap: double.NaN)));
        Assert.Null(RowSlot.FirstDifference(new RelativePlaceholderRow(), new RelativePlaceholderRow()));
    }

    [Fact]
    public void FirstDifference_NamesTheChangedProperty()
    {
        Assert.Equal(nameof(StandingsRow.GapToLeaderSeconds), RowSlot.FirstDifference(Row(gap: 1.5), Row(gap: 1.6)));
        Assert.Equal(nameof(DriverRow.Position), RowSlot.FirstDifference(Row(position: 2), Row(position: 3)));
        Assert.Equal(nameof(DriverRow.LastPitStop), RowSlot.FirstDifference(Row(), Row(pit: new PitStop(3, 25.1))));
    }

    [Fact]
    public void FirstDifference_TreatsADifferentKindOfRowAsChanged()
    {
        Assert.Equal("type", RowSlot.FirstDifference(Row(), new RelativePlaceholderRow()));
        Assert.Equal("type", RowSlot.FirstDifference(null, Row()));
    }

    [Fact]
    public void Sync_LeavesASlotAloneWhenItsRowIsUnchanged()
    {
        var shown = Row();
        var slots = new ObservableCollection<RowSlot>();
        RowSlot.Sync(slots, [shown]);
        var notified = 0;
        slots[0].PropertyChanged += (_, _) => notified++;

        RowSlot.Sync(slots, [Row()]);

        Assert.Same(shown, slots[0].Value);
        Assert.Equal(0, notified);
    }

    [Fact]
    public void Sync_SwapsAChangedRowAndFollowsTheRowCount()
    {
        var slots = new ObservableCollection<RowSlot>();
        RowSlot.Sync(slots, [Row(), Row()]);
        var changed = Row(gap: 2.0);

        RowSlot.Sync(slots, [changed, Row(), Row(), new RelativePlaceholderRow()]);
        Assert.Same(changed, slots[0].Value);
        Assert.Equal(4, slots.Count);
        Assert.IsType<RelativePlaceholderRow>(slots[3].Value);

        RowSlot.Sync(slots, [changed]);
        Assert.Single(slots);
    }

    // FirstDifference only compares the properties the builders set. A row that kept anything else
    // (a plain field, a getter over constructor state) could change on screen while comparing equal.
    [Theory]
    [InlineData(typeof(StandingsRow))]
    [InlineData(typeof(RelativeRow))]
    [InlineData(typeof(RelativePlaceholderRow))]
    [InlineData(typeof(StandingsSeparatorRow))]
    public void RowTypes_HoldNoStateOutsideSettableProperties(Type rowType)
    {
        for (var type = rowType; type is not null && type != typeof(object); type = type.BaseType)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var property = field.Name.StartsWith('<') && field.Name.EndsWith(">k__BackingField")
                    ? type.GetProperty(field.Name[1..field.Name.IndexOf('>')], BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    : null;
                Assert.True(property?.SetMethod is not null, $"{type.Name}.{field.Name} is not a settable auto-property");
            }
        }
    }
}
