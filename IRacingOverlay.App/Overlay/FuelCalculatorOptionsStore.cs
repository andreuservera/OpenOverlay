using System.IO;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists the Fuel Calculator widget's section toggles and calculation settings. Unlike
/// the other stores this writes one JSON object rather than a string-keyed dictionary, since the
/// settings are a mix of bools, an enum, and numeric margins.</summary>
internal static class FuelCalculatorOptionsStore
{
    private sealed record Snapshot(
        bool ShowFuelBar,
        bool ShowFuelRemaining,
        bool ShowLastLap,
        bool ShowAverage,
        bool ShowMinimum,
        bool ShowMaximum,
        bool ShowLapsRemaining,
        bool ShowFuelToFinish,
        bool ShowRefuel,
        FuelAverageSource AverageSource,
        double MarginLiters,
        double MarginLaps,
        bool Vertical = false,
        // Added later; files written before them keep the default order and grouping.
        string[]? UsageOrder = null,
        string[]? StrategyOrder = null,
        FuelGroupOrder FirstGroup = FuelGroupOrder.Auto);

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "fuel-calculator.json");

    public static void ApplyTo(FuelCalculatorOptions options)
    {
        var saved = Load();
        if (saved is null)
        {
            return; // never configured — leave the constructor defaults in place
        }

        options.ShowFuelBar = saved.ShowFuelBar;
        options.ShowFuelRemaining = saved.ShowFuelRemaining;
        options.ShowLastLap = saved.ShowLastLap;
        options.ShowAverage = saved.ShowAverage;
        options.ShowMinimum = saved.ShowMinimum;
        options.ShowMaximum = saved.ShowMaximum;
        options.ShowLapsRemaining = saved.ShowLapsRemaining;
        options.ShowFuelToFinish = saved.ShowFuelToFinish;
        options.ShowRefuel = saved.ShowRefuel;
        options.AverageSource = saved.AverageSource;
        options.MarginLiters = saved.MarginLiters;
        options.MarginLaps = saved.MarginLaps;
        options.Vertical = saved.Vertical;
        options.FirstGroup = saved.FirstGroup;
        if (saved.UsageOrder is { } usage)
        {
            options.UsageOrder = Cells(usage);
        }

        if (saved.StrategyOrder is { } strategy)
        {
            options.StrategyOrder = Cells(strategy);
        }
    }

    public static void Save(FuelCalculatorOptions options)
    {
        var snapshot = new Snapshot(
            options.ShowFuelBar,
            options.ShowFuelRemaining,
            options.ShowLastLap,
            options.ShowAverage,
            options.ShowMinimum,
            options.ShowMaximum,
            options.ShowLapsRemaining,
            options.ShowFuelToFinish,
            options.ShowRefuel,
            options.AverageSource,
            options.MarginLiters,
            options.MarginLaps,
            options.Vertical,
            options.UsageOrder.Select(cell => cell.ToString()).ToArray(),
            options.StrategyOrder.Select(cell => cell.ToString()).ToArray(),
            options.FirstGroup);

        SettingsFile.WriteJson(FilePath, snapshot);
    }

    private static Snapshot? Load() => SettingsFile.ReadJson<Snapshot>(FilePath);

    private static List<FuelCell> Cells(IEnumerable<string> names) => names
        .Select(name => Enum.TryParse<FuelCell>(name, out var cell) ? cell : (FuelCell?)null)
        .OfType<FuelCell>()
        .ToList();
}
