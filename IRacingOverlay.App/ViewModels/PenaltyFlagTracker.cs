using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>Reports each car's black/meatball flags whenever they change, for the activity log.</summary>
internal sealed class PenaltyFlagTracker
{
    private readonly Dictionary<int, CarPenalties> _last = new();

    public List<(DriverEntry Driver, CarPenalties Penalties)> Update(TelemetrySnapshot telemetry, IracingSessionInfo? session)
    {
        var changes = new List<(DriverEntry, CarPenalties)>();
        if (session?.DriverInfo is not { } driverInfo)
        {
            return changes;
        }

        var penaltiesOf = FlagBuilder.ReadCarPenalties(telemetry, driverInfo.DriverCarIdx);
        foreach (var driver in driverInfo.Drivers)
        {
            if (driver.IsPaceCar || driver.CarIdx < 0)
            {
                continue;
            }

            var now = penaltiesOf(driver.CarIdx);
            if (now != _last.GetValueOrDefault(driver.CarIdx))
            {
                _last[driver.CarIdx] = now;
                changes.Add((driver, now));
            }
        }

        return changes;
    }
}
