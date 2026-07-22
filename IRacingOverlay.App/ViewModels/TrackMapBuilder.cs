using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Positions every car along a flat, linear representation of the lap (0% = start/finish, 100% =
/// back to start/finish) using CarIdxLapDistPct — the same "how far around the lap" signal RaceLab's
/// own Flat Map and irDashies' linear track map use, rather than deriving position from gap/time
/// math. Deliberately a single line — cars close together on track are left to overlap (the panel
/// renders them with transparency so overlaps stay visible) rather than spread onto extra lanes,
/// keeping the bar thin.
/// </summary>
internal static class TrackMapBuilder
{
    public static List<TrackMapMarker> Build(TelemetrySnapshot telemetry, IracingSessionInfo? session)
    {
        if (session?.DriverInfo is not { } driverInfo)
        {
            return [];
        }

        if (!telemetry.HasVariable(TelemetryVarNames.CarIdxLapDistPct))
        {
            return [];
        }

        var lapDistPct = telemetry.GetFloatArray(TelemetryVarNames.CarIdxLapDistPct);
        var onPitRoad = telemetry.HasVariable(TelemetryVarNames.CarIdxOnPitRoad)
            ? telemetry.GetBoolArray(TelemetryVarNames.CarIdxOnPitRoad)
            : null;

        var markers = new List<TrackMapMarker>();
        foreach (var driver in driverInfo.Drivers)
        {
            if (driver.IsPaceCar || driver.CarIdx < 0 || driver.CarIdx >= lapDistPct.Length)
            {
                continue;
            }

            var pct = lapDistPct[driver.CarIdx];
            // -1 is iRacing's own "no valid position" sentinel (car not out on track this session).
            if (pct < 0)
            {
                continue;
            }

            markers.Add(new TrackMapMarker
            {
                CarIdx = driver.CarIdx,
                CarNumber = driver.CarNumber,
                LapDistPct = pct,
                IsPlayer = driver.CarIdx == driverInfo.DriverCarIdx,
                OnPitRoad = onPitRoad is not null && driver.CarIdx < onPitRoad.Length && onPitRoad[driver.CarIdx],
                ClassColor = ClassColorFormat.Normalize(driver.CarClassColor),
            });
        }

        markers.Sort((a, b) => a.LapDistPct.CompareTo(b.LapDistPct));
        return markers;
    }
}
