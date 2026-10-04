namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Where each driver-table column sits and how wide it is, for one column order and set of visible
/// columns. Header and rows share one 14-slot grid; a cell asks for its slot here instead of naming a
/// fixed one, so reordering is just a new layout. iRating and its delta are one unit in the order —
/// they share a badge — so the delta always takes the slot right after the rating. Immutable: the
/// options build a new one whenever the order or a column's visibility changes.
/// </summary>
public sealed class DriverTableColumnLayout
{
    public const int SlotCount = 14;

    /// <summary>The order the columns have always had, and the order anything not in a saved order
    /// falls back to.</summary>
    public static readonly IReadOnlyList<DriverTableColumn> DefaultOrder =
    [
        DriverTableColumn.Position,
        DriverTableColumn.CarNumber,
        DriverTableColumn.CarBrand,
        DriverTableColumn.CountryFlag,
        DriverTableColumn.Driver,
        DriverTableColumn.LastPitStop,
        DriverTableColumn.TireCompound,
        DriverTableColumn.IRating,
        DriverTableColumn.License,
        DriverTableColumn.Lap,
        DriverTableColumn.BestLap,
        DriverTableColumn.LastLap,
        DriverTableColumn.Gap,
    ];

    private readonly double[] _widths = new double[SlotCount];
    private readonly int[] _slots = new int[Enum.GetValues<DriverTableColumn>().Length];

    public DriverTableColumnLayout(IReadOnlyList<DriverTableColumn> order, Func<DriverTableColumn, bool> isVisible)
    {
        var slot = 0;
        foreach (var column in Normalize(order))
        {
            Place(column, slot++, isVisible);
            if (column == DriverTableColumn.IRating)
            {
                Place(DriverTableColumn.IRatingDelta, slot++, isVisible);
            }
        }
    }

    /// <summary>A usable order from whatever was saved or imported: the delta (which travels with
    /// the rating), repeats and unknown values are dropped, and any column missing is appended where
    /// the default order has it.</summary>
    public static IReadOnlyList<DriverTableColumn> Normalize(IEnumerable<DriverTableColumn> order)
    {
        var result = order
            .Where(column => column != DriverTableColumn.IRatingDelta && Enum.IsDefined(column))
            .Distinct()
            .ToList();
        result.AddRange(DefaultOrder.Where(column => !result.Contains(column)));
        return result;
    }

    /// <summary>A column's width when shown, in device-independent pixels at size M.</summary>
    public static double WidthOf(DriverTableColumn column) => column switch
    {
        DriverTableColumn.Position => 36,
        DriverTableColumn.CarNumber => 40,
        DriverTableColumn.Driver => 128,
        DriverTableColumn.LastPitStop => 76,
        DriverTableColumn.TireCompound => 36,
        DriverTableColumn.IRating => 48,
        DriverTableColumn.IRatingDelta => 44,
        DriverTableColumn.License => 44,
        DriverTableColumn.Lap => 34,
        DriverTableColumn.BestLap => 64,
        DriverTableColumn.LastLap => 64,
        DriverTableColumn.CarBrand => 38,
        DriverTableColumn.CountryFlag => 34,
        _ => 58,
    };

    public int SlotOf(DriverTableColumn column) => _slots[(int)column];

    // One property per slot and per column, so XAML can bind each straight off the options.
    public double Width0 => _widths[0];
    public double Width1 => _widths[1];
    public double Width2 => _widths[2];
    public double Width3 => _widths[3];
    public double Width4 => _widths[4];
    public double Width5 => _widths[5];
    public double Width6 => _widths[6];
    public double Width7 => _widths[7];
    public double Width8 => _widths[8];
    public double Width9 => _widths[9];
    public double Width10 => _widths[10];
    public double Width11 => _widths[11];
    public double Width12 => _widths[12];
    public double Width13 => _widths[13];

    public int Position => SlotOf(DriverTableColumn.Position);
    public int CarNumber => SlotOf(DriverTableColumn.CarNumber);
    public int Driver => SlotOf(DriverTableColumn.Driver);
    public int LastPitStop => SlotOf(DriverTableColumn.LastPitStop);
    public int TireCompound => SlotOf(DriverTableColumn.TireCompound);
    public int IRating => SlotOf(DriverTableColumn.IRating);
    public int License => SlotOf(DriverTableColumn.License);
    public int Lap => SlotOf(DriverTableColumn.Lap);
    public int BestLap => SlotOf(DriverTableColumn.BestLap);
    public int LastLap => SlotOf(DriverTableColumn.LastLap);
    public int Gap => SlotOf(DriverTableColumn.Gap);
    public int CarBrand => SlotOf(DriverTableColumn.CarBrand);
    public int CountryFlag => SlotOf(DriverTableColumn.CountryFlag);

    private void Place(DriverTableColumn column, int slot, Func<DriverTableColumn, bool> isVisible)
    {
        _slots[(int)column] = slot;
        _widths[slot] = isVisible(column) ? WidthOf(column) : 0;
    }
}
