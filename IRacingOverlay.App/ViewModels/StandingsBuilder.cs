using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Turns a raw TelemetrySnapshot + session info into the row lists the Relative/Standings widgets bind to.
/// </summary>
internal static class StandingsBuilder
{
    /// <summary>
    /// Relative gap is approximated as (player's fractional lap position - car's fractional lap
    /// position) * player's own last/best lap time. This is the same approximation most community
    /// relative overlays use; it's good enough for "who's near me and closing/gapping" at a glance.
    /// A more precise version using CarIdxEstTime for sub-lap accuracy is a good "dive deep" follow-up.
    /// </summary>
    public static List<RelativeRow> BuildRelative(TelemetrySnapshot telemetry, IracingSessionInfo? session, int maxEachSide = 4)
    {
        if (session?.DriverInfo is not { } driverInfo)
        {
            return [];
        }

        if (!telemetry.HasVariable(TelemetryVarNames.CarIdxLap) ||
            !telemetry.HasVariable(TelemetryVarNames.CarIdxLapDistPct))
        {
            return [];
        }

        var carIdxLap = telemetry.GetIntArray(TelemetryVarNames.CarIdxLap);
        var carIdxLapDistPct = telemetry.GetFloatArray(TelemetryVarNames.CarIdxLapDistPct);
        var onPitRoad = TryGetBoolArray(telemetry, TelemetryVarNames.CarIdxOnPitRoad);

        var playerCarIdx = driverInfo.DriverCarIdx;
        if (playerCarIdx < 0 || playerCarIdx >= carIdxLap.Length)
        {
            return [];
        }

        var playerLapTime = GetPlayerReferenceLapTime(telemetry);
        if (playerLapTime <= 0)
        {
            return [];
        }

        var playerTrackPosition = carIdxLap[playerCarIdx] + carIdxLapDistPct[playerCarIdx];

        var rows = new List<RelativeRow>();
        foreach (var driver in driverInfo.Drivers)
        {
            if (driver.IsPaceCar || driver.CarIdx < 0 || driver.CarIdx >= carIdxLap.Length)
            {
                continue;
            }

            // A car with lap==0 and distPct==0 that hasn't ever moved isn't meaningfully "on track" yet.
            if (carIdxLap[driver.CarIdx] == 0 && carIdxLapDistPct[driver.CarIdx] == 0 && driver.CarIdx != playerCarIdx)
            {
                continue;
            }

            var trackPosition = carIdxLap[driver.CarIdx] + carIdxLapDistPct[driver.CarIdx];
            var gapSeconds = (playerTrackPosition - trackPosition) * playerLapTime;

            rows.Add(new RelativeRow
            {
                CarIdx = driver.CarIdx,
                Name = driver.UserName,
                CarNumber = driver.CarNumber,
                IsPlayer = driver.CarIdx == playerCarIdx,
                GapSeconds = gapSeconds,
                OnPitRoad = onPitRoad is not null && driver.CarIdx < onPitRoad.Length && onPitRoad[driver.CarIdx],
                ClassColor = FormatClassColor(driver.CarClassColor),
            });
        }

        rows.Sort((a, b) => a.GapSeconds.CompareTo(b.GapSeconds));

        var playerIndex = rows.FindIndex(r => r.IsPlayer);
        if (playerIndex < 0)
        {
            return rows;
        }

        var start = Math.Max(0, playerIndex - maxEachSide);
        var end = Math.Min(rows.Count, playerIndex + maxEachSide + 1);
        return rows.GetRange(start, end - start);
    }

    public static List<StandingsRow> BuildStandings(TelemetrySnapshot telemetry, IracingSessionInfo? session)
    {
        if (session?.DriverInfo is not { } driverInfo)
        {
            return [];
        }

        if (!telemetry.HasVariable(TelemetryVarNames.CarIdxPosition))
        {
            return [];
        }

        var positions = telemetry.GetIntArray(TelemetryVarNames.CarIdxPosition);
        var gapToLeader = TryGetFloatArray(telemetry, TelemetryVarNames.CarIdxF2Time);
        var bestLaps = TryGetFloatArray(telemetry, TelemetryVarNames.CarIdxBestLapTime);
        var onPitRoad = TryGetBoolArray(telemetry, TelemetryVarNames.CarIdxOnPitRoad);
        var playerCarIdx = driverInfo.DriverCarIdx;

        var rows = new List<StandingsRow>();
        foreach (var driver in driverInfo.Drivers)
        {
            if (driver.IsPaceCar || driver.CarIdx < 0 || driver.CarIdx >= positions.Length)
            {
                continue;
            }

            var position = positions[driver.CarIdx];
            if (position <= 0)
            {
                continue; // not currently classified (e.g. not yet on track)
            }

            rows.Add(new StandingsRow
            {
                CarIdx = driver.CarIdx,
                Position = position,
                Name = driver.UserName,
                CarNumber = driver.CarNumber,
                IsPlayer = driver.CarIdx == playerCarIdx,
                OnPitRoad = onPitRoad is not null && driver.CarIdx < onPitRoad.Length && onPitRoad[driver.CarIdx],
                GapToLeaderSeconds = gapToLeader is not null && driver.CarIdx < gapToLeader.Length ? gapToLeader[driver.CarIdx] : 0,
                BestLapTime = bestLaps is not null && driver.CarIdx < bestLaps.Length ? bestLaps[driver.CarIdx] : 0,
                ClassColor = FormatClassColor(driver.CarClassColor),
            });
        }

        rows.Sort((a, b) => a.Position.CompareTo(b.Position));
        return rows;
    }

    private static double GetPlayerReferenceLapTime(TelemetrySnapshot telemetry)
    {
        if (telemetry.HasVariable(TelemetryVarNames.PlayerLastLapTime))
        {
            var last = telemetry.GetFloat(TelemetryVarNames.PlayerLastLapTime);
            if (last > 0)
            {
                return last;
            }
        }

        if (telemetry.HasVariable(TelemetryVarNames.PlayerBestLapTime))
        {
            var best = telemetry.GetFloat(TelemetryVarNames.PlayerBestLapTime);
            if (best > 0)
            {
                return best;
            }
        }

        return 0;
    }

    private static bool[]? TryGetBoolArray(TelemetrySnapshot telemetry, string name) =>
        telemetry.HasVariable(name) ? telemetry.GetBoolArray(name) : null;

    private static float[]? TryGetFloatArray(TelemetrySnapshot telemetry, string name) =>
        telemetry.HasVariable(name) ? telemetry.GetFloatArray(name) : null;

    private static string FormatClassColor(string carClassColor)
    {
        // iRacing supplies class colors as a decimal or hex-without-# integer string; normalize to "#RRGGBB".
        if (string.IsNullOrWhiteSpace(carClassColor))
        {
            return "#FFFFFF";
        }

        var trimmed = carClassColor.TrimStart('#');
        return int.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out var value)
            ? $"#{value & 0xFFFFFF:X6}"
            : "#FFFFFF";
    }
}
