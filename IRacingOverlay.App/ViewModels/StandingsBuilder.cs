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
    /// measured in its own seconds, so it works the same for a GT3 car as a slower class). The gap
    /// is the raw CarIdxEstTime difference folded into the nearest ±half-lap-time window (a shared
    /// reference lap time, not each car's individually — using each car's own lap time here was an
    /// earlier bug), rather than corrected by each car's own *total completed laps*: CarIdxLap only
    /// tracks laps-since-session-start, which is meaningless for "how far apart on track are we
    /// right now" in Practice/Qualifying — cars don't start together there, so a car that joined
    /// earlier can be dozens of laps ahead in count while still running right next to the player.
    /// Multiplying that raw lap-count difference by a lap time (the earlier approach) produced gaps
    /// of thousands of seconds for cars that were genuinely side by side (reported live). Folding to
    /// the nearest half-lap instead answers the question a Relative widget actually needs to: what's
    /// the smallest gap consistent with this car's current track position, regardless of how many
    /// total laps either car has done. Always includes the player, even alone with no one else on
    /// track.
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

        // Fall back to ANY car's recorded lap time, not just the player's own — otherwise the whole
        // field disappears from Relative for the player's entire first lap of every session (reported
        // live), even though by then other cars in a live session have almost always already set one.
        var refLapTime = GetReferenceLapTime(playerCarIdx, lastLaps, bestLaps);
        if (refLapTime <= 0)
        {
            refLapTime = GetAnyRecordedLapTime(lastLaps, bestLaps);
        }

        double GapTo(int carIdx)
        {
            var gap = (double)carIdxEstTime[playerCarIdx] - carIdxEstTime[carIdx];
            if (refLapTime > 0)
            {
                gap %= refLapTime;
                if (gap > refLapTime / 2)
                {
                    gap -= refLapTime;
                }
                else if (gap < -refLapTime / 2)
                {
                    gap += refLapTime;
                }
            }

            return gap;
        }

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

            // With no reference lap time available anywhere in the whole session (nobody, including
            // the player, has ever completed a lap this session — a genuinely rare "just loaded in"
            // moment), the wrap in GapTo can't be applied at all, and an un-wrapped raw CarIdxEstTime
            // difference against a car on a different lap is a small, plausible-looking gap that's
            // actually meaningless (observed live: sitting in the garage made several genuinely-
            // lap(s)-apart cars all show nearly the same ~53s "gap" purely by coincidence of
            // within-lap position). Only compare cars we can actually place relative to the player.
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
                GapSeconds = GapTo(driver.CarIdx),
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
    public static List<StandingsRow> BuildStandings(TelemetrySnapshot telemetry, IracingSessionInfo? session, SessionBestLapTracker? bestLapTracker = null)
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

        // Practice and Qualifying both need every driver in the session ranked by their own best
        // lap time, not the on-track running order: a driver who's set a fast lap and driven back to
        // their pit stall still needs to show up (and know their grid slot / where their pace ranks),
        // even though they're now stationary in the pits and iRacing can drop their live
        // CarIdxBestLapTime/CarIdxLastLapTime/CarIdxLap back toward the "not on track" values that
        // BuildStandings' normal eligibility check below would otherwise exclude them for.
        if (IsPracticeOrQualifyingSession(session))
        {
            return BuildFastestLapStandings(
                session, driverInfo, currentLaps, lastLaps, bestLaps, onPitRoad, playerCarIdx, isMultiClass,
                bestLapTracker ?? new SessionBestLapTracker());
        }

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
    ///
    /// Computed directly from the session's driver roster (iRating is a per-driver, session-lifetime
    /// value from the YAML, not per-tick telemetry) rather than from already-built StandingsRows —
    /// SOF describes the whole lobby, so it must include every driver regardless of whether they
    /// currently happen to be on track or parked in the pits, which the eligibility filtering in
    /// BuildStandings' rows deliberately does not guarantee.
    /// </summary>
    public static double ComputeStrengthOfField(IracingSessionInfo? session)
    {
        if (session?.DriverInfo is not { } driverInfo)
        {
            return 0;
        }

        var iratings = driverInfo.Drivers
            .Where(d => !d.IsPaceCar && d.IRating > 0)
            .Select(d => (double)d.IRating)
            .ToList();
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

    private static bool IsPracticeOrQualifyingSession(IracingSessionInfo? session)
    {
        if (session?.SessionInfo is not { } sessionInfo)
        {
            return false;
        }

        var current = sessionInfo.Sessions.FirstOrDefault(s => s.SessionNum == sessionInfo.CurrentSessionNum);
        var type = current?.SessionType ?? "";
        return type.Contains("Qualify", StringComparison.OrdinalIgnoreCase) ||
               type.Contains("Practice", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ranks every non-pace-car driver by their own best lap time, fastest first — regardless of
    /// whether they're currently out on track or parked back in their pit stall — so the player can
    /// see their actual grid slot (Qualifying) or where their pace ranks (Practice) at any point in
    /// the session, not just while cars happen to still be circulating. Sourced from
    /// <paramref name="bestLapTracker"/>'s running cache rather than this tick's raw telemetry, since
    /// iRacing can drop a parked car's live CarIdxBestLapTime/CarIdxLastLapTime back toward 0 — the
    /// tracker is what remembers the real number for the rest of the session.
    /// </summary>
    private static List<StandingsRow> BuildFastestLapStandings(
        IracingSessionInfo session,
        DriverInfoSection driverInfo,
        int[] currentLaps,
        float[]? lastLaps,
        float[]? bestLaps,
        bool[]? onPitRoad,
        int playerCarIdx,
        bool isMultiClass,
        SessionBestLapTracker bestLapTracker)
    {
        var drivers = driverInfo.Drivers.Where(d => !d.IsPaceCar && d.CarIdx >= 0).ToList();
        var sessionNum = session.SessionInfo?.CurrentSessionNum ?? 0;
        var cachedBest = bestLapTracker.Update(sessionNum, drivers.Select(d => d.CarIdx), bestLaps, lastLaps);

        double QualTime(DriverEntry d) => cachedBest.TryGetValue(d.CarIdx, out var t) && t > 0 ? t : double.MaxValue;

        // Drivers with no time yet sort to the bottom (double.MaxValue), stable-tied by CarIdx.
        var ordered = drivers.OrderBy(QualTime).ThenBy(d => d.CarIdx).ToList();

        var sessionFastestLap = 0.0;
        foreach (var t in cachedBest.Values)
        {
            if (t > 0 && (sessionFastestLap <= 0 || t < sessionFastestLap))
            {
                sessionFastestLap = t;
            }
        }

        var poleTime = ordered.Count > 0 ? QualTime(ordered[0]) : double.MaxValue;

        var classRank = new Dictionary<int, int>();
        var rows = new List<StandingsRow>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var driver = ordered[i];
            classRank.TryGetValue(driver.CarClassID, out var rank);
            rank++;
            classRank[driver.CarClassID] = rank;

            var bestLapTime = cachedBest.GetValueOrDefault(driver.CarIdx, 0);
            var thisTime = QualTime(driver);

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
                GapToLeaderSeconds = thisTime < double.MaxValue && poleTime < double.MaxValue ? thisTime - poleTime : 0,
                LastLapTime = lastLaps is not null && driver.CarIdx < lastLaps.Length ? lastLaps[driver.CarIdx] : 0,
                BestLapTime = bestLapTime,
                IsMultiClass = isMultiClass,
                IRating = driver.IRating,
                LicString = driver.LicString,
                // A single pairwise-duel iRating estimate makes no sense against a fastest-lap order.
                IRatingDelta = 0,
                IsSessionFastestLap = bestLapTime > 0 && bestLapTime <= sessionFastestLap,
                ClassColor = ClassColorFormat.Normalize(driver.CarClassColor),
                CarClassID = driver.CarClassID,
                CarClassName = string.IsNullOrWhiteSpace(driver.CarClassShortName) ? driver.CarScreenNameShort : driver.CarClassShortName,
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
}
