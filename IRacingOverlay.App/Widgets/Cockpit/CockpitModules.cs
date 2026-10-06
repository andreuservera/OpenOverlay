using System.Globalization;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets.Cockpit;

/// <summary>What a value means for the driver; the dashboard turns it into a colour. Only
/// <see cref="Warning"/> and <see cref="Critical"/> are states that also tint the module's card.</summary>
internal enum CockpitTone
{
    Normal,
    Positive,
    Negative,
    Warning,
    Critical,
}

/// <summary>A bar inside a module (the pedal inputs), 0 to 1.</summary>
internal readonly record struct CockpitBar(double Value, CockpitTone Tone);

/// <summary>What one module shows right now: a value, or a set of bars in its place, and an
/// optional level gauge drawn under the value when the module has a full column to itself.</summary>
internal sealed record CockpitReading(
    string Value,
    CockpitTone Tone = CockpitTone.Normal,
    double? Gauge = null,
    IReadOnlyList<CockpitBar>? Bars = null);

/// <summary>
/// One entry of the module catalog: how the module is named in the settings, labelled on the
/// dashboard, how much room it takes, and how it reads <see cref="CockpitState"/>. A module the car
/// can't feed (<see cref="CockpitState.Unsupported"/>) is left out of the dashboard.
/// </summary>
/// <param name="IsLarge">Takes a full column (two rows); small modules pair up in one.</param>
/// <param name="Width">Preferred column width at scale M. A shared column takes the wider of its two.</param>
/// <param name="FullSize">Value size when the module has a full column to itself.</param>
/// <param name="Centered">Value centred in the card rather than right-aligned.</param>
internal sealed record CockpitModuleSpec(
    CockpitModule Module,
    string Name,
    Func<UnitSystem, string> Label,
    bool IsLarge,
    double Width,
    double FullSize,
    Func<CockpitState, CockpitReading> Read,
    bool Centered = false);

/// <summary>
/// The catalog. Adding a module is adding a <see cref="CockpitModule"/> value and an entry here:
/// the composition and the drawing work from these entries alone.
/// </summary>
internal static class CockpitModules
{
    // Warning thresholds stay in the telemetry's own units (°C, fraction of the tank); only the
    // text is converted.
    private const double LowFuelFraction = 0.1;
    private const double WaterWarnCelsius = 105;
    private const double OilWarnCelsius = 125;

    private const double SmallFullSize = 34;

    private static readonly Dictionary<CockpitModule, CockpitModuleSpec> Specs = new CockpitModuleSpec[]
    {
        new(CockpitModule.Gear, "Gear", _ => "GEAR", IsLarge: true, Width: 58, FullSize: 54,
            state => new CockpitReading(state.Gear, state.ShiftBlink ? CockpitTone.Critical : CockpitTone.Normal), Centered: true),
        new(CockpitModule.Speed, "Speed", units => Units.SpeedUnit(units).ToUpperInvariant(), IsLarge: true, Width: 86, FullSize: 38,
            state => new CockpitReading(state.SpeedKph > 0 ? Whole(Units.Speed(state.SpeedKph, state.UnitSystem)) : "—")),
        new(CockpitModule.Rpm, "RPM", _ => "RPM", IsLarge: false, Width: 74, FullSize: SmallFullSize,
            state => new CockpitReading(state.Rpm > 0 ? Whole(state.Rpm) : "—")),
        // The configured level; intervention is shown by the tone, never by changing the text.
        new(CockpitModule.Abs, "ABS", _ => "ABS", IsLarge: false, Width: 70, FullSize: SmallFullSize,
            state => new CockpitReading(
                state.AbsLevel?.ToString(CultureInfo.InvariantCulture) ?? "—",
                state.AbsActive ? CockpitTone.Warning : CockpitTone.Normal)),
        new(CockpitModule.Fuel, "Fuel", units => $"FUEL {Units.VolumeUnit(units).ToUpperInvariant()}", IsLarge: false, Width: 74, FullSize: SmallFullSize,
            state => new CockpitReading(
                state.FuelLiters is { } liters ? Units.Volume(liters, state.UnitSystem).ToString("0.0", CultureInfo.InvariantCulture) : "—",
                state.FuelPct is < LowFuelFraction ? CockpitTone.Warning : CockpitTone.Normal,
                Gauge: state.FuelPct)),
        new(CockpitModule.Inputs, "Inputs", _ => "INPUTS", IsLarge: false, Width: 66, FullSize: SmallFullSize,
            state => new CockpitReading("", Bars: [new(state.Throttle, CockpitTone.Positive), new(state.Brake, CockpitTone.Negative)])),
        new(CockpitModule.WaterTemp, "Water temp", units => $"WATER {Units.TemperatureUnit(units)}", IsLarge: false, Width: 70, FullSize: SmallFullSize,
            state => Temperature(state, state.WaterTempC, WaterWarnCelsius)),
        new(CockpitModule.OilTemp, "Oil temp", units => $"OIL {Units.TemperatureUnit(units)}", IsLarge: false, Width: 70, FullSize: SmallFullSize,
            state => Temperature(state, state.OilTempC, OilWarnCelsius)),
        new(CockpitModule.BrakeBias, "Brake bias", _ => "BB %", IsLarge: false, Width: 70, FullSize: SmallFullSize,
            state => new CockpitReading(state.BrakeBias is { } bias ? bias.ToString("0.0", CultureInfo.InvariantCulture) : "—")),
        new(CockpitModule.TractionControl, "Traction control", _ => "TC", IsLarge: false, Width: 70, FullSize: SmallFullSize,
            state => new CockpitReading(state.TractionControl?.ToString(CultureInfo.InvariantCulture) ?? "—")),
        // Against the limit when the session has one, coloured as the Incidents widget colours it.
        new(CockpitModule.Incidents, "Incidents", _ => "INC", IsLarge: false, Width: 70, FullSize: SmallFullSize,
            state => state.Incidents is not { } incidents
                ? new CockpitReading("—")
                : new CockpitReading(
                    incidents.Limit is { } limit
                        ? string.Create(CultureInfo.InvariantCulture, $"{incidents.CountedTotal} / {limit}")
                        : incidents.CountedTotal.ToString(CultureInfo.InvariantCulture),
                    incidents.Severity switch
                    {
                        IncidentSeverity.Critical => CockpitTone.Critical,
                        IncidentSeverity.Warning => CockpitTone.Warning,
                        _ => CockpitTone.Normal,
                    })),
        // Green while ahead of the session's best lap, red while behind it.
        new(CockpitModule.Delta, "Delta (session best)", _ => "DELTA", IsLarge: false, Width: 80, FullSize: SmallFullSize,
            state => state.Delta is not { IsValid: true } delta
                ? new CockpitReading("—")
                : new CockpitReading(
                    delta.Display,
                    delta.DeltaSeconds < 0 ? CockpitTone.Positive : delta.DeltaSeconds > 0 ? CockpitTone.Negative : CockpitTone.Normal)),
    }.ToDictionary(spec => spec.Module);

    public static CockpitModuleSpec Of(CockpitModule module) => Specs[module];

    private static string Whole(double value) => value.ToString("0", CultureInfo.InvariantCulture);

    private static CockpitReading Temperature(CockpitState state, double? celsius, double warnAtCelsius) => new(
        celsius is { } c ? Whole(Units.Temperature(c, state.UnitSystem)) : "—",
        celsius >= warnAtCelsius ? CockpitTone.Warning : CockpitTone.Normal);
}
