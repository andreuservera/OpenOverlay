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
    /// full lap apart are corrected using a *single shared* reference lap time (the player's own),
    /// not each car's individually — using each car's own lap time here was the earlier bug: two
    /// cars on the very same lap but with different (or missing) recorded lap times would get a
    /// spurious offset of `lapNumber * lapTimeDifference` seconds, growing larger every lap and
    /// producing gaps that made no sense. With one shared reference, same-lap comparisons reduce to
    /// a plain CarIdxEstTime difference regardless of any car's lap-time data quality.
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

        var refLapTime = GetReferenceLapTime(playerCarIdx, lastLaps, bestLaps);

        double TimePosition(int carIdx) => carIdxLap[carIdx] * refLapTime + carIdxEstTime[carIdx];

        var playerTimePosition = TimePosition(playerCarIdx);

        var rows = new List<RelativeRow>();
        foreach (var driver in driverInfo.Drivers)
        {
            if (driver.IsPaceCar || driver.CarIdx < 0 || driver.CarIdx >= carIdxLap.Length)
            {
                continue;
            }

            var isPlayer = driver.CarIdx == playerCarIdx;

            // CurrentLap == -1 is iRacing's own "never left the garage this session" sentinel —
            // never include such a car regardless of any other signal (see BuildStandings for the
            // live-confirmed failure mode this guards against: a session's placeholder AI roster).
            if (!isPlayer && carIdxLap[driver.CarIdx] < 0)
            {
                continue;
            }

            var hasStarted = isPlayer
                || carIdxLap[driver.CarIdx] > 0
                || carIdxEstTime[driver.CarIdx] > 0
                || (carIdxLapDistPct is not null && driver.CarIdx < carIdxLapDistPct.Length && carIdxLapDistPct[driver.CarIdx] > 0);
            if (!hasStarted)
            {
                continue; // car not yet out on track this session
            }

            // Without a reference lap time, the lap-count term is unavailable — a car on a
            // different lap than the player would then compare as if same-lap, showing a small,
            // plausible-looking gap that's actually meaningless (observed live: sitting in the
            // garage with no lap time set yet made several genuinely-lap(s)-apart cars all show
            // nearly the same ~53s "gap" purely by coincidence of within-lap position). Only compare
            // cars we can actually place relative to the player.
            if (!isPlayer && refLapTime <= 0 && carIdxLap[driver.CarIdx] != carIdxLap[playerCarIdx])
            {
                continue;
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
    /// Always orders and computes gaps continuously from CarIdxLap+CarIdxEstTime (the same technique
    /// BuildRelative uses), rather than iRacing's own CarIdxPosition/CarIdxF2Time. Those official
    /// values are only recomputed at scoring-line crossings (effectively once per lap), which is
    /// exactly the "standings only updates when finishing a lap" behavior reported live — using them
    /// made the whole table look frozen mid-lap. CarIdxPosition is still used as one signal for "has
    /// this car actually started," just not for the displayed position/gap numbers themselves.
    /// </summary>
    public static List<StandingsRow> BuildStandings(TelemetrySnapshot telemetry, IracingSessionInfo? session)
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

        var currentLaps = telemetry.GetIntArray(TelemetryVarNames.CarIdxLap);
        var carIdxEstTime = telemetry.GetFloatArray(TelemetryVarNames.CarIdxEstTime);
        var positions = TryGetIntArray(telemetry, TelemetryVarNames.CarIdxPosition);
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

        // Standings needs to order the *whole* field, including cars on laps the player hasn't
        // reached yet, so — unlike BuildRelative, which just excludes cars it can't place — this
        // falls back to any car's recorded pace in the field when the player hasn't set a lap time.
        var refLapTime = GetReferenceLapTime(playerCarIdx, lastLaps, bestLaps);
        if (refLapTime <= 0)
        {
            refLapTime = GetAnyRecordedLapTime(lastLaps, bestLaps);
        }

        double TimePosition(int carIdx) =>
            carIdx < currentLaps.Length && carIdx < carIdxEstTime.Length
                ? currentLaps[carIdx] * refLapTime + carIdxEstTime[carIdx]
                : 0;

        var eligible = new List<DriverEntry>();
        foreach (var driver in driverInfo.Drivers)
        {
            if (driver.IsPaceCar || driver.CarIdx < 0)
            {
                continue;
            }

            var isPlayer = driver.CarIdx == playerCarIdx;

            // CurrentLap == -1 is iRacing's own "never left the garage this session" sentinel.
            // Confirmed live: a solo Test session's placeholder AI roster all sat at Lap -1 but
            // still carried an assigned CarIdxPosition, which let them slip through as "eligible" and
            // show up as a full grid of cars all tied on an identical, meaningless gap. A real
            // position assignment does not override a car that plainly never went on track.
            var lap = driver.CarIdx < currentLaps.Length ? currentLaps[driver.CarIdx] : -1;
            if (!isPlayer && lap < 0)
            {
                continue;
            }

            var hasOfficialPosition = positions is not null && driver.CarIdx < positions.Length && positions[driver.CarIdx] > 0;
            var hasStarted = isPlayer
                || hasOfficialPosition
                || lap > 0
                || (driver.CarIdx < carIdxEstTime.Length && carIdxEstTime[driver.CarIdx] > 0);
            if (!hasStarted)
            {
                continue; // car not yet out on track this session
            }

            eligible.Add(driver);
        }

        var ordered = eligible.OrderByDescending(d => TimePosition(d.CarIdx)).ToList();
        var leaderTimePosition = ordered.Count > 0 ? TimePosition(ordered[0].CarIdx) : 0;
        var classRank = new Dictionary<int, int>();
        var rows = new List<StandingsRow>();

        for (var i = 0; i < ordered.Count; i++)
        {
            var driver = ordered[i];
            classRank.TryGetValue(driver.CarClassID, out var rank);
            rank++;
            classRank[driver.CarClassID] = rank;

            rows.Add(new StandingsRow
            {
                CarIdx = driver.CarIdx,
                Position = i + 1,
                ClassPosition = rank,
                Name = driver.UserName,
                CarNumber = driver.CarNumber,
                IsPlayer = driver.CarIdx == playerCarIdx,
                OnPitRoad = onPitRoad is not null && driver.CarIdx < onPitRoad.Length && onPitRoad[driver.CarIdx],
                CurrentLap = driver.CarIdx < currentLaps.Length ? currentLaps[driver.CarIdx] : 0,
                GapToLeaderSeconds = leaderTimePosition - TimePosition(driver.CarIdx),
                LastLapTime = lastLaps is not null && driver.CarIdx < lastLaps.Length ? lastLaps[driver.CarIdx] : 0,
                BestLapTime = bestLaps is not null && driver.CarIdx < bestLaps.Length ? bestLaps[driver.CarIdx] : 0,
                IsMultiClass = isMultiClass,
                IRating = driver.IRating,
                LicString = driver.LicString,
                ClassColor = FormatClassColor(driver.CarClassColor),
            });
        }

        return rows;
    }

    private static double GetAnyRecordedLapTime(float[]? lastLaps, float[]? bestLaps)
    {
        if (lastLaps is not null)
        {
            foreach (var t in lastLaps)
            {
                if (t > 0)
                {
                    return t;
                }
            }
        }

        if (bestLaps is not null)
        {
            foreach (var t in bestLaps)
            {
                if (t > 0)
                {
                    return t;
                }
            }
        }

        return 0;
    }

    private static double GetReferenceLapTime(int carIdx, float[]? lastLaps, float[]? bestLaps)
    {
        if (lastLaps is not null && carIdx < lastLaps.Length && lastLaps[carIdx] > 0)
        {
            return lastLaps[carIdx];
        }

        if (bestLaps is not null && carIdx < bestLaps.Length && bestLaps[carIdx] > 0)
        {
            return bestLaps[carIdx];
        }

        return 0;
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
