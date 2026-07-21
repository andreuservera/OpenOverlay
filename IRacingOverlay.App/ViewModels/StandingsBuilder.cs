using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Turns a raw TelemetrySnapshot + session info into the row lists the Relative/Standings widgets bind to.
/// </summary>
internal static class StandingsBuilder
{
    /// <summary>
    /// Relative gap uses CarIdxEstTime — iRacing's own "estimated time to reach current location on
    /// track" per car — which is precise within a lap and class-agnostic (each car's progress is
    /// measured in its own seconds, so it works the same for a GT3 car as a slower class). Cars a
    /// full lap apart are corrected using that car's own last/best lap time (falling back to 0, i.e.
    /// no correction, if neither is known yet — harmless while everyone's still on the same lap,
    /// which covers the common "who's near me" case even in a solo test session).
    /// Always includes the player, even alone with no one else on track.
    /// </summary>
    public static List<RelativeRow> BuildRelative(TelemetrySnapshot telemetry, IracingSessionInfo? session, int maxEachSide = 4)
    {
        if (session?.DriverInfo is not { } driverInfo)
        {
            return [];
        }

        if (!telemetry.HasVariable(TelemetryVarNames.CarIdxLap) ||
            !telemetry.HasVariable(TelemetryVarNames.CarIdxEstTime))
        {
            return [];
        }

        var carIdxLap = telemetry.GetIntArray(TelemetryVarNames.CarIdxLap);
        var carIdxEstTime = telemetry.GetFloatArray(TelemetryVarNames.CarIdxEstTime);
        var carIdxLapDistPct = TryGetFloatArray(telemetry, TelemetryVarNames.CarIdxLapDistPct);
        var lastLaps = TryGetFloatArray(telemetry, TelemetryVarNames.CarIdxLastLapTime);
        var bestLaps = TryGetFloatArray(telemetry, TelemetryVarNames.CarIdxBestLapTime);
        var onPitRoad = TryGetBoolArray(telemetry, TelemetryVarNames.CarIdxOnPitRoad);

        var playerCarIdx = driverInfo.DriverCarIdx;
        if (playerCarIdx < 0 || playerCarIdx >= carIdxLap.Length)
        {
            return [];
        }

        double TimePosition(int carIdx)
        {
            var refLapTime = 0.0;
            if (lastLaps is not null && carIdx < lastLaps.Length && lastLaps[carIdx] > 0)
            {
                refLapTime = lastLaps[carIdx];
            }
            else if (bestLaps is not null && carIdx < bestLaps.Length && bestLaps[carIdx] > 0)
            {
                refLapTime = bestLaps[carIdx];
            }

            return carIdxLap[carIdx] * refLapTime + carIdxEstTime[carIdx];
        }

        var playerTimePosition = TimePosition(playerCarIdx);

        var rows = new List<RelativeRow>();
        foreach (var driver in driverInfo.Drivers)
        {
            if (driver.IsPaceCar || driver.CarIdx < 0 || driver.CarIdx >= carIdxLap.Length)
            {
                continue;
            }

            var isPlayer = driver.CarIdx == playerCarIdx;
            var hasStarted = isPlayer
                || carIdxLap[driver.CarIdx] > 0
                || carIdxEstTime[driver.CarIdx] > 0
                || (carIdxLapDistPct is not null && driver.CarIdx < carIdxLapDistPct.Length && carIdxLapDistPct[driver.CarIdx] > 0);
            if (!hasStarted)
            {
                continue; // car not yet out on track this session
            }

            rows.Add(new RelativeRow
            {
                CarIdx = driver.CarIdx,
                Name = driver.UserName,
                CarNumber = driver.CarNumber,
                IsPlayer = isPlayer,
                GapSeconds = playerTimePosition - TimePosition(driver.CarIdx),
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

    /// <summary>
    /// Prefers iRacing's own official race position (CarIdxPosition), which is only meaningful once
    /// a session is actually scoring (practice/qualify/race with the field classified). A solo/offline
    /// Test session never populates it, so this falls back to ordering by track position ourselves —
    /// the same CarIdxLap+CarIdxEstTime technique BuildRelative uses — so standings still shows at
    /// least the player (and anyone else out there) instead of going blank.
    /// </summary>
    public static List<StandingsRow> BuildStandings(TelemetrySnapshot telemetry, IracingSessionInfo? session)
    {
        if (session?.DriverInfo is not { } driverInfo)
        {
            return [];
        }

        var positions = TryGetIntArray(telemetry, TelemetryVarNames.CarIdxPosition);
        var classPositions = TryGetIntArray(telemetry, TelemetryVarNames.CarIdxClassPosition);
        var currentLaps = TryGetIntArray(telemetry, TelemetryVarNames.CarIdxLap);
        var carIdxEstTime = TryGetFloatArray(telemetry, TelemetryVarNames.CarIdxEstTime);
        var gapToLeader = TryGetFloatArray(telemetry, TelemetryVarNames.CarIdxF2Time);
        var lastLaps = TryGetFloatArray(telemetry, TelemetryVarNames.CarIdxLastLapTime);
        var bestLaps = TryGetFloatArray(telemetry, TelemetryVarNames.CarIdxBestLapTime);
        var onPitRoad = TryGetBoolArray(telemetry, TelemetryVarNames.CarIdxOnPitRoad);
        var playerCarIdx = driverInfo.DriverCarIdx;

        var distinctClasses = driverInfo.Drivers
            .Where(d => !d.IsPaceCar)
            .Select(d => d.CarClassID)
            .Distinct()
            .Count();
        var isMultiClass = distinctClasses > 1;

        var hasOfficialPositions = positions is not null
            && playerCarIdx >= 0 && playerCarIdx < positions.Length
            && positions[playerCarIdx] > 0;

        double TimePosition(int carIdx)
        {
            if (currentLaps is null || carIdxEstTime is null || carIdx >= currentLaps.Length || carIdx >= carIdxEstTime.Length)
            {
                return 0;
            }

            var refLapTime = 0.0;
            if (lastLaps is not null && carIdx < lastLaps.Length && lastLaps[carIdx] > 0)
            {
                refLapTime = lastLaps[carIdx];
            }
            else if (bestLaps is not null && carIdx < bestLaps.Length && bestLaps[carIdx] > 0)
            {
                refLapTime = bestLaps[carIdx];
            }

            return currentLaps[carIdx] * refLapTime + carIdxEstTime[carIdx];
        }

        StandingsRow BuildRow(DriverEntry driver, int position, int classPosition) => new()
        {
            CarIdx = driver.CarIdx,
            Position = position,
            ClassPosition = classPosition,
            Name = driver.UserName,
            CarNumber = driver.CarNumber,
            IsPlayer = driver.CarIdx == playerCarIdx,
            OnPitRoad = onPitRoad is not null && driver.CarIdx < onPitRoad.Length && onPitRoad[driver.CarIdx],
            CurrentLap = currentLaps is not null && driver.CarIdx < currentLaps.Length ? currentLaps[driver.CarIdx] : 0,
            GapToLeaderSeconds = gapToLeader is not null && driver.CarIdx < gapToLeader.Length ? gapToLeader[driver.CarIdx] : 0,
            LastLapTime = lastLaps is not null && driver.CarIdx < lastLaps.Length ? lastLaps[driver.CarIdx] : 0,
            BestLapTime = bestLaps is not null && driver.CarIdx < bestLaps.Length ? bestLaps[driver.CarIdx] : 0,
            IsMultiClass = isMultiClass,
            ClassColor = FormatClassColor(driver.CarClassColor),
        };

        var eligible = new List<DriverEntry>();
        foreach (var driver in driverInfo.Drivers)
        {
            if (driver.IsPaceCar || driver.CarIdx < 0)
            {
                continue;
            }

            if (hasOfficialPositions)
            {
                if (driver.CarIdx >= positions!.Length || positions[driver.CarIdx] <= 0)
                {
                    continue; // not currently classified (e.g. not yet on track)
                }
            }
            else
            {
                var hasStarted = driver.CarIdx == playerCarIdx
                    || (currentLaps is not null && driver.CarIdx < currentLaps.Length && currentLaps[driver.CarIdx] > 0)
                    || (carIdxEstTime is not null && driver.CarIdx < carIdxEstTime.Length && carIdxEstTime[driver.CarIdx] > 0);
                if (!hasStarted)
                {
                    continue;
                }
            }

            eligible.Add(driver);
        }

        List<StandingsRow> rows;
        if (hasOfficialPositions)
        {
            rows = eligible
                .Select(driver => BuildRow(
                    driver,
                    positions![driver.CarIdx],
                    classPositions is not null && driver.CarIdx < classPositions.Length ? classPositions[driver.CarIdx] : positions[driver.CarIdx]))
                .ToList();
        }
        else
        {
            var ordered = eligible.OrderByDescending(d => TimePosition(d.CarIdx)).ToList();
            var classRank = new Dictionary<int, int>();
            rows = [];
            for (var i = 0; i < ordered.Count; i++)
            {
                var driver = ordered[i];
                classRank.TryGetValue(driver.CarClassID, out var rank);
                rank++;
                classRank[driver.CarClassID] = rank;
                rows.Add(BuildRow(driver, i + 1, rank));
            }
        }

        rows.Sort((a, b) => a.Position.CompareTo(b.Position));
        return rows;
    }

    private static bool[]? TryGetBoolArray(TelemetrySnapshot telemetry, string name) =>
        telemetry.HasVariable(name) ? telemetry.GetBoolArray(name) : null;

    private static float[]? TryGetFloatArray(TelemetrySnapshot telemetry, string name) =>
        telemetry.HasVariable(name) ? telemetry.GetFloatArray(name) : null;

    private static int[]? TryGetIntArray(TelemetrySnapshot telemetry, string name) =>
        telemetry.HasVariable(name) ? telemetry.GetIntArray(name) : null;

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
