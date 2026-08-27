using System.IO;
using System.Text.Json;
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
        double MarginLaps);

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
            options.MarginLaps);

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(snapshot));
    }

    private static Snapshot? Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(FilePath)) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
