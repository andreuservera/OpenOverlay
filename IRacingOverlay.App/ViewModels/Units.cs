using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

public enum UnitSystem
{
    Metric,
    Imperial,
}

/// <summary>Where the unit system comes from. Index order matches the Control Panel's choices.</summary>
public enum UnitPreference
{
    FollowIRacing,
    Metric,
    Imperial,
}

/// <summary>
/// The one place units are decided and converted. By default every overlay follows iRacing's own
/// <c>DisplayUnits</c> setting; the user can override that with a fixed choice in the app, which
/// always wins. Telemetry always arrives metric (m/s, °C, kPa, litres); everything is stored and
/// compared in metric and only converted at display time.
/// </summary>
public static class Units
{
    private const double KphPerMph = 1.609344;
    private const double KpaPerPsi = 6.894757;
    private const double LitersPerGallon = 3.785411784;

    /// <summary>The units in effect: the user's override, or what iRacing last reported. Used where
    /// there's no telemetry at hand — the control-panel previews and settings.</summary>
    public static UnitSystem Current { get; private set; } = UnitSystem.Metric;

    public static UnitPreference Preference { get; private set; } = UnitPreference.FollowIRacing;

    /// <summary>What iRacing's own setting was when last seen; null until the sim has reported it.</summary>
    public static UnitSystem? IRacingSetting { get; private set; }

    /// <summary>Raised on the UI thread whenever <see cref="Current"/> changes.</summary>
    public static event Action? CurrentChanged;

    public static UnitSystem Read(TelemetrySnapshot telemetry) => Resolve(Preference, FromTelemetry(telemetry) ?? IRacingSetting);

    public static void Observe(TelemetrySnapshot telemetry)
    {
        IRacingSetting = FromTelemetry(telemetry) ?? IRacingSetting;
        Update();
    }

    public static void SetPreference(UnitPreference preference)
    {
        Preference = preference;
        Update();
    }

    /// <summary>A fixed choice wins; following iRacing falls back to metric until the sim reports.</summary>
    internal static UnitSystem Resolve(UnitPreference preference, UnitSystem? iracing) => preference switch
    {
        UnitPreference.Metric => UnitSystem.Metric,
        UnitPreference.Imperial => UnitSystem.Imperial,
        _ => iracing ?? UnitSystem.Metric,
    };

    /// <summary>iRacing's DisplayUnits: 0 = English (imperial), 1 = metric.</summary>
    private static UnitSystem? FromTelemetry(TelemetrySnapshot telemetry) =>
        telemetry.HasVariable(TelemetryVarNames.DisplayUnits)
            ? telemetry.GetInt(TelemetryVarNames.DisplayUnits) == 0 ? UnitSystem.Imperial : UnitSystem.Metric
            : null;

    private static void Update()
    {
        var units = Resolve(Preference, IRacingSetting);
        if (units != Current)
        {
            Current = units;
            CurrentChanged?.Invoke();
        }
    }

    public static double Speed(double kph, UnitSystem units) => units == UnitSystem.Imperial ? kph / KphPerMph : kph;

    public static double SpeedFromMs(double metersPerSecond, UnitSystem units) => Speed(metersPerSecond * 3.6, units);

    public static string SpeedUnit(UnitSystem units) => units == UnitSystem.Imperial ? "mph" : "km/h";

    public static double Temperature(double celsius, UnitSystem units) =>
        units == UnitSystem.Imperial ? (celsius * 9 / 5) + 32 : celsius;

    public static string TemperatureUnit(UnitSystem units) => units == UnitSystem.Imperial ? "°F" : "°C";

    public static double Pressure(double kpa, UnitSystem units) => units == UnitSystem.Imperial ? kpa / KpaPerPsi : kpa;

    public static string PressureUnit(UnitSystem units) => units == UnitSystem.Imperial ? "psi" : "kPa";

    /// <summary>kPa read as whole numbers; psi needs a decimal to be useful.</summary>
    public static string PressureFormat(UnitSystem units) => units == UnitSystem.Imperial ? "0.0" : "0";

    public static double Volume(double liters, UnitSystem units) => units == UnitSystem.Imperial ? liters / LitersPerGallon : liters;

    public static double VolumeToLiters(double value, UnitSystem units) => units == UnitSystem.Imperial ? value * LitersPerGallon : value;

    public static string VolumeUnit(UnitSystem units) => units == UnitSystem.Imperial ? "gal" : "L";
}
