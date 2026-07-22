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
                ClassColor = ClassColorFormat.Normalize(driver.CarClassColor),
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

        // Real bug reported live: right at a race's start, many cars can have identical or
        // near-identical TimePosition (everyone still sitting on the grid, Lap 0, EstTime ~0).
        // OrderByDescending is a *stable* sort, so ties fall back to `eligible`'s original order —
        // which is just DriverInfo's roster/YAML order, unrelated to actual grid position. That
        // let a driver who legitimately started last in their class appear ahead of faster-starting
        // classmates purely by roster-order coincidence. Breaking ties by iRacing's own official
        // CarIdxPosition (already assigned at grid formation, well before anyone's first lap timing
        // data exists) fixes this without reintroducing the "frozen until lap end" staleness that's
        // the whole reason official position isn't used as the *primary* sort key.
        int TieBreakPosition(int carIdx) =>
            positions is not null && carIdx < positions.Length && positions[carIdx] > 0 ? positions[carIdx] : int.MaxValue;

        var ordered = eligible
            .OrderByDescending(d => TimePosition(d.CarIdx))
            .ThenBy(d => TieBreakPosition(d.CarIdx))
            .ToList();
        var leaderTimePosition = ordered.Count > 0 ? TimePosition(ordered[0].CarIdx) : 0;

        // The single fastest lap set by anyone in the session, across every car — not just the
        // player's own best. 0 (no valid lap yet) never counts.
        var sessionFastestLap = 0.0;
        foreach (var driver in ordered)
        {
            var bestLap = bestLaps is not null && driver.CarIdx < bestLaps.Length ? bestLaps[driver.CarIdx] : 0;
            if (bestLap > 0 && (sessionFastestLap <= 0 || bestLap < sessionFastestLap))
            {
                sessionFastestLap = bestLap;
            }
        }

        var iRatingDeltaByCarIdx = EstimateIRatingDeltas(ordered);

        var classRank = new Dictionary<int, int>();
        var rows = new List<StandingsRow>();

        for (var i = 0; i < ordered.Count; i++)
        {
            var driver = ordered[i];
            classRank.TryGetValue(driver.CarClassID, out var rank);
            rank++;
            classRank[driver.CarClassID] = rank;

            var bestLapTime = bestLaps is not null && driver.CarIdx < bestLaps.Length ? bestLaps[driver.CarIdx] : 0;

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
                BestLapTime = bestLapTime,
                IsMultiClass = isMultiClass,
                IRating = driver.IRating,
                LicString = driver.LicString,
                IRatingDelta = iRatingDeltaByCarIdx.GetValueOrDefault(driver.CarIdx, 0),
                IsSessionFastestLap = bestLapTime > 0 && bestLapTime <= sessionFastestLap,
                ClassColor = ClassColorFormat.Normalize(driver.CarClassColor),
                CarClassID = driver.CarClassID,
                // iRacing leaves CarClassShortName blank for fixed/spec series (a "class" of one car
                // model, e.g. Porsche Cup) — it's only populated for genuine multi-car classes like
                // GT3. Falling back to the car's own name keeps the header informative either way.
                CarClassName = string.IsNullOrWhiteSpace(driver.CarClassShortName) ? driver.CarScreenNameShort : driver.CarClassShortName,
            });
        }

        return rows;
    }

    /// <summary>
    /// iRacing's published Strength of Field formula: BR1 = 1600/ln(2); each driver contributes
    /// e^(-iRating/BR1) to a sum; SOF = BR1 * ln(driverCount / sum). Self-consistency check: a field
    /// where every driver carries the exact same iRating R comes out to SOF == R.
    /// </summary>
    public static double ComputeStrengthOfField(IReadOnlyList<StandingsRow> rows)
    {
        var iratings = rows.Where(r => r.IRating > 0).Select(r => (double)r.IRating).ToList();
        if (iratings.Count == 0)
        {
            return 0;
        }

        var br1 = 1600.0 / Math.Log(2);
        var sum = iratings.Sum(r => Math.Exp(-r / br1));
        return sum > 0 ? br1 * Math.Log(iratings.Count / sum) : 0;
    }

    /// <summary>
    /// Best-effort approximation of iRacing's undisclosed live iRating-change formula. iRacing has
    /// confirmed the shape of the real calculation (treat the race as a round-robin of 1-on-1
    /// "duels" against every other rated driver — win the duel by finishing ahead, lose it by
    /// finishing behind — score each duel Elo-style, and scale the total by field size so a bigger
    /// field doesn't inflate the swing) but has never published the exact scoring constant. This
    /// uses a commonly-cited community reconstruction (K=200, divided by field size) — it tracks
    /// direction and rough magnitude reliably, but won't necessarily match the official post-race
    /// number. Uses current running order as a live "if it ended right now" position, same as the
    /// rest of Standings.
    /// </summary>
    private static Dictionary<int, double> EstimateIRatingDeltas(List<DriverEntry> ordered)
    {
        var rated = new List<(int CarIdx, int IRating, int Position)>();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].IRating > 0)
            {
                rated.Add((ordered[i].CarIdx, ordered[i].IRating, i));
            }
        }

        var result = new Dictionary<int, double>();
        var n = rated.Count;
        if (n < 2)
        {
            return result;
        }

        var k = 200.0 / n;
        foreach (var driver in rated)
        {
            var delta = 0.0;
            foreach (var opponent in rated)
            {
                if (opponent.CarIdx == driver.CarIdx)
                {
                    continue;
                }

                var expected = 1.0 / (1.0 + Math.Pow(10, (opponent.IRating - driver.IRating) / 1600.0));
                var actual = driver.Position < opponent.Position ? 1.0 : 0.0;
                delta += k * (actual - expected);
            }

            result[driver.CarIdx] = delta;
        }

        return result;
    }

    /// <summary>
    /// Restructures BuildStandings' flat, overall-order rows for multiclass display: the player's
    /// own class is shown in full (that's the race that matters most to them), every other class is
    /// capped to its top <paramref name="otherClassLimit"/> (leaders only, for context), and a
    /// header item naming each class is inserted before its block. The player's class block comes
    /// first; other classes follow ordered by their leading car's overall position. Single-class
    /// sessions pass through unchanged — nothing to group or cap when there's only one class.
    /// </summary>
    public static List<object> GroupForDisplay(IReadOnlyList<StandingsRow> rows, int otherClassLimit = 5)
    {
        if (rows.Count == 0 || !rows[0].IsMultiClass)
        {
            return rows.Cast<object>().ToList();
        }

        var playerClassId = rows.FirstOrDefault(r => r.IsPlayer)?.CarClassID ?? rows[0].CarClassID;

        // Rows already arrive in overall race order, so grouping by class while preserving
        // first-seen order keeps each class's own rows in class-position order too, and the first
        // row recorded for a class is that class's current leader.
        var classOrder = new List<int>();
        var byClass = new Dictionary<int, List<StandingsRow>>();
        foreach (var row in rows)
        {
            if (!byClass.TryGetValue(row.CarClassID, out var classRows))
            {
                classRows = [];
                byClass[row.CarClassID] = classRows;
                classOrder.Add(row.CarClassID);
            }

            classRows.Add(row);
        }

        var orderedClassIds = classOrder
            .OrderBy(id => id == playerClassId ? 0 : 1)
            .ThenBy(id => byClass[id][0].Position);

        var display = new List<object>();
        foreach (var classId in orderedClassIds)
        {
            var classRows = byClass[classId];
            var className = classRows[0].CarClassName;
            display.Add(new StandingsHeaderRow
            {
                ClassName = string.IsNullOrWhiteSpace(className) ? $"CLASS {classId}" : className.ToUpperInvariant(),
                ClassColor = classRows[0].ClassColor,
            });

            display.AddRange(classId == playerClassId ? classRows : classRows.Take(otherClassLimit));
        }

        return display;
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
}
