using System.Text.RegularExpressions;
using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Turns a raw TelemetrySnapshot + session info into the row lists the Relative/Standings widgets bind to.
/// </summary>
internal static class StandingsBuilder
{
    /// <summary>How close up the road a lapped car has to be for Relative to flag it as about to be
    /// lapped. Close enough to act on, far enough out to see it coming.</summary>
    private const double BeingLappedWithinSeconds = 5;

    /// <summary>
    /// Which cars show, and in what order, is their place on the road around the player. Each gap is
    /// time on the player's own CarIdxEstTime clock: how long the player's car would take to get from
    /// one spot to the other. CarIdxEstTime follows the car's speed round the lap, so a car a steady
    /// second behind reads a steady second through a hairpin and down a straight alike. Pricing the
    /// distance at an average lap speed instead was tried and failed live: on a clean lap at Road
    /// Atlanta a true one-second gap read anywhere from 0.5 to 1.4 s depending on where the pair
    /// was, between cars of one class too, and with no lap set yet there was nothing to show at all.
    /// CarIdxEstTime is there from the first tick, formation lap included.
    ///
    /// Each car's CarIdxEstTime runs on its own CarClassEstLapTime clock, so only a car of the
    /// player's own model can be read as it is. Anyone else's spot is looked up on the player's own
    /// curve (<see cref="EstTimeProfile"/>), which keeps one separation one gap whatever the other
    /// car is. Where the curve hasn't been driven yet, the other car's estimate is scaled to the
    /// player's lap: close for a BoP'd model of the same class, rougher between classes.
    ///
    /// Folded to the nearest half lap rather than counting laps: CarIdxLap only counts laps since the
    /// session started, so in Practice/Qualifying a car dozens of laps ahead in count can be running
    /// right alongside. Lapped and lapping come from race distance. Position is live while the race
    /// is running (<see cref="LiveRaceRanks"/>) and Standings' official one otherwise.
    ///
    /// Always includes the player, even alone with no one else on track.
    /// </summary>
    public static List<object> BuildRelative(
        TelemetrySnapshot telemetry,
        IracingSessionInfo? session,
        int maxEachSide = DriverTableOptions.DefaultRelativeFocusSize,
        IReadOnlyList<StandingsRow>? standings = null,
        IReadOnlyDictionary<int, PitStop>? lastPitStops = null,
        EstTimeProfile? estTimeProfile = null)
    {
        if (session?.DriverInfo is not { } driverInfo || !HasTrackPositions(telemetry))
        {
            return [];
        }

        var carIdxLap = telemetry.GetIntArray(TelemetryVarNames.CarIdxLap);
        var carIdxLapDistPct = telemetry.GetFloatArray(TelemetryVarNames.CarIdxLapDistPct);
        var carIdxEstTime = TryGetFloatArray(telemetry, TelemetryVarNames.CarIdxEstTime);
        var positions = TryGetIntArray(telemetry, TelemetryVarNames.CarIdxPosition);
        var lastLaps = TryGetFloatArray(telemetry, TelemetryVarNames.CarIdxLastLapTime);
        var bestLaps = TryGetFloatArray(telemetry, TelemetryVarNames.CarIdxBestLapTime);
        var onPitRoad = TryGetBoolArray(telemetry, TelemetryVarNames.CarIdxOnPitRoad);

        // "The player" here is the car the table is centred on: the one the camera is on, so
        // spectating a car reads as being in it.
        var playerCarIdx = FocusCarIdx(telemetry, driverInfo);
        if (playerCarIdx < 0 || playerCarIdx >= carIdxLap.Length)
        {
            return [];
        }

        var penaltiesOf = FlagBuilder.ReadCarPenalties(telemetry, driverInfo.DriverCarIdx);
        var compoundOf = TireCompoundsOf(telemetry, driverInfo);

        var racing = Racing(driverInfo);
        var isMultiClass = racing.Select(d => d.CarClassID).Distinct().Count() > 1;

        // A ghost player still sees their own row and the cars around them; they just aren't ranked.
        var ghost = GhostPlayer(driverInfo);
        var listed = ghost is null ? racing : [.. racing, ghost];
        var player = listed.FirstOrDefault(d => d.CarIdx == playerCarIdx);
        var playerLapTime = player?.CarClassEstLapTime ?? 0;

        // Reads the scoring table too: on a mid-session attach the telemetry lap arrays are still empty.
        var results = CurrentSession.Results(telemetry, session);
        var grid = StartingGrid(telemetry, session);
        var laps = new LapTimeSource(bestLaps, lastLaps, results);
        var fastestLapByClass = FastestLapByClass(racing, laps.Best);
        var isRace = IsRaceSession(telemetry, session);

        // Relative shows the same columns as Standings, so it needs the same per-driver figures.
        // They come from the one place that has them for cars currently off track too.
        int LapCountOf(int carIdx)
        {
            var live = carIdx >= 0 && carIdx < carIdxLap.Length ? carIdxLap[carIdx] : -1;
            if (live >= 0)
            {
                return live;
            }

            return results.TryGetValue(carIdx, out var scored) && scored.LapsComplete > 0 ? scored.LapsComplete : live;
        }

        // While the race is running, position follows the cars on track: a pass shows the moment it
        // happens, as RaceLab does, instead of waiting for the line like Standings and iRacing's own
        // relative. Everything else in the row is untouched by it.
        var liveRanks = isRace && IsRacing(telemetry)
            ? LiveRaceRanks(racing, carIdxLap, carIdxLapDistPct, results, positions)
            : null;

        // Otherwise race position comes from the standings order when it's available. iRacing's own
        // CarIdxPosition is only assigned in scored sessions — it sits at 0 through practice and
        // test sessions, which is why this column read "0" while Standings, which computes its own
        // order, had it right. Sharing that order also keeps the two widgets from disagreeing about
        // the same driver, and carries the iRating estimate across, which needs the whole field to
        // compute and so can't be derived here.
        var standingsByCarIdx = new Dictionary<int, StandingsRow>();
        foreach (var row in standings ?? [])
        {
            standingsByCarIdx[row.CarIdx] = row;
        }

        int PositionOf(int carIdx)
        {
            if (carIdx == ghost?.CarIdx)
            {
                return 0;
            }

            if (liveRanks is not null && liveRanks.TryGetValue(carIdx, out var live))
            {
                return live.Position;
            }

            if (standingsByCarIdx.TryGetValue(carIdx, out var ranked))
            {
                return ranked.Position;
            }

            if (positions is not null && carIdx < positions.Length && positions[carIdx] > 0)
            {
                return positions[carIdx];
            }

            return results.TryGetValue(carIdx, out var scored) ? scored.Position : 0;
        }

        int ClassPositionOf(int carIdx)
        {
            if (carIdx == ghost?.CarIdx)
            {
                return 0;
            }

            if (liveRanks is not null && liveRanks.TryGetValue(carIdx, out var live))
            {
                return live.ClassPosition;
            }

            if (standingsByCarIdx.TryGetValue(carIdx, out var ranked))
            {
                return ranked.ClassPosition;
            }

            return results.TryGetValue(carIdx, out var scored) && scored.ClassPosition > 0
                ? scored.ClassPosition
                : PositionOf(carIdx);
        }

        double IRatingDeltaOf(int carIdx) =>
            standingsByCarIdx.TryGetValue(carIdx, out var ranked) ? ranked.IRatingDelta : 0;

        var playerOnTrack = TrackPosition.Read(carIdxLap, carIdxLapDistPct, playerCarIdx);

        // Where a car is, as seconds from the line on the player's EstTime clock.
        double? OnPlayersClock(DriverEntry driver, TrackPosition where)
        {
            if (carIdxEstTime is null || player is null || playerLapTime <= 0 || driver.CarIdx >= carIdxEstTime.Length)
            {
                return null;
            }

            var own = (double)carIdxEstTime[driver.CarIdx];
            if (EstTimeProfile.SharesClock(driver, player))
            {
                return own;
            }

            if (estTimeProfile is not null && estTimeProfile.EstLapTime == playerLapTime &&
                estTimeProfile.EstTimeAt(where.LapDistPct) is { } learned)
            {
                return learned;
            }

            return driver.CarClassEstLapTime > 0 ? own / driver.CarClassEstLapTime * playerLapTime : null;
        }

        // Negative ahead, positive behind, folded to the nearest half of the player's lap.
        double GapSecondsTo(DriverEntry driver, TrackPosition theirs, TrackPosition mine)
        {
            if (OnPlayersClock(player!, mine) is { } myTime && OnPlayersClock(driver, theirs) is { } theirTime)
            {
                var ahead = theirTime - myTime;
                return -(ahead - playerLapTime * Math.Floor(ahead / playerLapTime + 0.5));
            }

            // No estimate to read: the distance at the player's estimated lap, or nothing.
            return playerLapTime > 0 ? -mine.OnTrackGapTo(theirs) * playerLapTime : double.NaN;
        }

        // Race distance, never the folded gap: half a lap is where the fold flips, so a car more than
        // half a lap up in the race sits behind the player on the road only because it is about to lap
        // them, and a lapped car just up the road is the one the player is about to lap. Only in a
        // race; in practice and qualifying lap counts are just time on track.
        LapRelation LapRelationOf(TrackPosition theirs, double gapSeconds)
        {
            if (!isRace || playerOnTrack is not { } mine)
            {
                return LapRelation.SameLap;
            }

            return mine.RaceGapTo(theirs) switch
            {
                > 0.5 => LapRelation.Lapping,
                < -0.5 when gapSeconds is < 0 and >= -BeingLappedWithinSeconds => LapRelation.BeingLapped,
                < -0.5 => LapRelation.Lapped,
                _ => LapRelation.SameLap,
            };
        }

        var placed = new List<(RelativeRow Row, double OrderKey, double LapsAhead)>();
        foreach (var driver in listed)
        {
            if (driver.CarIdx >= carIdxLap.Length)
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
                || (driver.CarIdx < carIdxLapDistPct.Length && carIdxLapDistPct[driver.CarIdx] > 0);
            if (!hasStarted)
            {
                continue; // car not yet out on track this session
            }

            // Only cars that can be placed against the player. With the player out of the world
            // (garage, tow) that is nobody else; the player's own row still shows.
            var onTrack = TrackPosition.Read(carIdxLap, carIdxLapDistPct, driver.CarIdx);
            var lapsAhead = 0.0;
            var gapSeconds = 0.0;
            if (!isPlayer)
            {
                if (onTrack is not { } theirs || playerOnTrack is not { } mine)
                {
                    continue;
                }

                lapsAhead = mine.OnTrackGapTo(theirs);
                gapSeconds = GapSecondsTo(driver, theirs, mine);
            }

            var bestLapTime = laps.Best(driver.CarIdx);
            var penalties = penaltiesOf(driver.CarIdx);
            placed.Add((new RelativeRow
            {
                CarIdx = driver.CarIdx,
                Position = PositionOf(driver.CarIdx),
                ClassPosition = ClassPositionOf(driver.CarIdx),
                StartPosition = grid.TryGetValue(driver.CarIdx, out var slot) ? slot.Position : 0,
                StartClassPosition = slot.ClassPosition,
                Name = driver.UserName,
                CarNumber = driver.CarNumber,
                IsPlayer = isPlayer,
                IsGhost = driver.IsGhost,
                GapSeconds = gapSeconds,
                OnPitRoad = onPitRoad is not null && driver.CarIdx < onPitRoad.Length && onPitRoad[driver.CarIdx],
                HasBlackFlag = penalties.Black,
                HasFurledFlag = penalties.Furled,
                HasMeatballFlag = penalties.Meatball,
                LastPitStop = LastPitStopOf(lastPitStops, driver.CarIdx),
                TireCompound = compoundOf(driver.CarIdx),
                CarBrand = CarBrand.ForCar(driver.CarScreenName),
                FlairName = driver.FlairName,
                CurrentLap = LapCountOf(driver.CarIdx),
                LastLapTime = laps.Last(driver.CarIdx),
                BestLapTime = bestLapTime,
                IsMultiClass = isMultiClass,
                IRating = driver.IRating,
                LicString = driver.LicString,
                IRatingDelta = driver.IsGhost ? 0 : IRatingDeltaOf(driver.CarIdx),
                IsSessionFastestLap = !driver.IsGhost && bestLapTime > 0 && bestLapTime <= fastestLapByClass.GetValueOrDefault(driver.CarClassID),
                ClassColor = ClassColorFormat.Normalize(driver.CarClassColor),
                CarClassID = driver.CarClassID,
                CarClassName = driver.CarClassShortName,
                LapRelation = !isPlayer && onTrack is { } position ? LapRelationOf(position, gapSeconds) : LapRelation.SameLap,
            }, double.IsNaN(gapSeconds) ? -lapsAhead : gapSeconds, lapsAhead));
        }

        // In the order of the gaps shown, so a sign never contradicts a place: furthest up the road
        // first. Without a clock to read, by place on the road.
        var rows = placed
            .OrderBy(p => p.OrderKey)
            .ThenByDescending(p => p.LapsAhead)
            .Select(p => p.Row)
            .ToList();

        var playerIndex = rows.FindIndex(r => r.IsPlayer);
        if (playerIndex < 0)
        {
            return rows.Cast<object>().ToList();
        }

        var window = new List<object>();
        var start = Math.Max(0, playerIndex - maxEachSide);
        var end = Math.Min(rows.Count, playerIndex + maxEachSide + 1);
        for (var i = start; i < end; i++)
        {
            window.Add(rows[i]);
        }

        // Cars drift in and out of the window constantly as they lap or get lapped, and a table that
        // shrinks and grows with them resizes the widget mid-corner. Holding the configured number
        // of slots keeps the height fixed — but only up to what the session could ever fill, so a
        // solo practice stays a single row instead of a column of blanks.
        var slots = Math.Min(maxEachSide * 2 + 1, listed.Count);
        while (window.Count < slots)
        {
            window.Add(new RelativePlaceholderRow());
        }

        return window;
    }

    /// <summary>
    /// A race is classified the way iRacing's own timing does it, and only changes as cars cross the
    /// line: the order is iRacing's official CarIdxPosition, and each car's gap is measured at the
    /// line by <paramref name="crossings"/> — how long after the first car of its class to complete
    /// that lap it completed it — and holds until it next crosses. A car a lap or more down shows
    /// laps instead. Before the first crossing a gap is unknown and shows as a dash.
    ///
    /// Everything that is live state rather than classification — pit road, penalty flags, tyre,
    /// last pit stop — is read fresh on every build.
    ///
    /// This replaced a table re-sorted continuously from track position, which reshuffled through
    /// every lap; the official order holding between crossings is what a race classification is.
    /// Without official positions (an unscored test session) the same rule applies by hand: most laps
    /// completed first, then who completed the latest of them first.
    ///
    /// Practice and Qualifying rank by fastest lap instead (<see cref="BuildFastestLapStandings"/>).
    /// </summary>
    public static List<StandingsRow> BuildStandings(
        TelemetrySnapshot telemetry,
        IracingSessionInfo? session,
        SessionBestLapTracker? bestLapTracker = null,
        IReadOnlyDictionary<int, PitStop>? lastPitStops = null,
        LineCrossingTracker? crossings = null)
    {
        if (session?.DriverInfo is not { } driverInfo || !HasTrackPositions(telemetry))
        {
            return [];
        }

        var currentLaps = telemetry.GetIntArray(TelemetryVarNames.CarIdxLap);
        var lapDistPct = telemetry.GetFloatArray(TelemetryVarNames.CarIdxLapDistPct);
        var positions = TryGetIntArray(telemetry, TelemetryVarNames.CarIdxPosition);
        var lastLaps = TryGetFloatArray(telemetry, TelemetryVarNames.CarIdxLastLapTime);
        var bestLaps = TryGetFloatArray(telemetry, TelemetryVarNames.CarIdxBestLapTime);
        var onPitRoad = TryGetBoolArray(telemetry, TelemetryVarNames.CarIdxOnPitRoad);
        // Highlighted and centred on: the car the camera is on (see FocusCarIdx).
        var playerCarIdx = FocusCarIdx(telemetry, driverInfo);
        var penaltiesOf = FlagBuilder.ReadCarPenalties(telemetry, driverInfo.DriverCarIdx);
        var compoundOf = TireCompoundsOf(telemetry, driverInfo);

        var distinctClasses = driverInfo.Drivers
            .Where(d => !d.IsPaceCar && !d.IsGhost)
            .Select(d => d.CarClassID)
            .Distinct()
            .Count();
        var isMultiClass = distinctClasses > 1;

        var results = CurrentSession.Results(telemetry, session);
        var grid = StartingGrid(telemetry, session);
        var laps = new LapTimeSource(bestLaps, lastLaps, results);

        // CarIdxLap is -1 whenever a car isn't on track — sitting in the garage, or back in the pit
        // stall after a run — which says nothing about how many laps they actually ran. The last lap
        // seen live comes first: it counts the same way, where the scoring table's "completed" is one
        // fewer, and switching between the two made a car's count drop by one each time it went
        // missing — seen live, spectating a race. The scoring table covers a car never seen live:
        // a parked car has no lap in progress, so its completed figure is the right one to show.
        // Still -1 with nothing scored either means the car genuinely has never been out.
        int LapCountOf(int carIdx)
        {
            var live = carIdx >= 0 && carIdx < currentLaps.Length ? currentLaps[carIdx] : -1;
            if (live >= 0)
            {
                return live;
            }

            if (crossings?.LastLapStarted(carIdx) is { } lastSeen)
            {
                return lastSeen;
            }

            return results.TryGetValue(carIdx, out var scored) && scored.LapsComplete > 0 ? scored.LapsComplete : live;
        }

        // Practice and Qualifying both need every driver in the session ranked by their own best
        // lap time, not the on-track running order: a driver who's set a fast lap and driven back to
        // their pit stall still needs to show up (and know their grid slot / where their pace ranks),
        // even though they're now stationary in the pits and iRacing can drop their live
        // CarIdxBestLapTime/CarIdxLastLapTime/CarIdxLap back toward the "not on track" values that
        // the race classification's eligibility check would otherwise exclude them for.
        if (IsPracticeOrQualifyingSession(telemetry, session))
        {
            return BuildFastestLapStandings(
                driverInfo, LapCountOf, laps, onPitRoad, penaltiesOf, compoundOf, playerCarIdx, isMultiClass,
                bestLapTracker ?? new SessionBestLapTracker(),
                CurrentSession.Number(telemetry, session),
                lastPitStops);
        }

        var racing = Racing(driverInfo);
        var lapsCompleted = LineCrossingTracker.LapsCompleted(telemetry) ?? currentLaps;

        int LapsCompletedOf(int carIdx) =>
            carIdx < lapsCompleted.Length && lapsCompleted[carIdx] >= 0 ? lapsCompleted[carIdx]
            : results.TryGetValue(carIdx, out var scored) ? scored.LapsComplete
            : -1;

        // After the chequered flag a finished car keeps its place whatever it does next: drives
        // to the garage, disconnects, or drops out of iRacing's live order.
        int OfficialPositionOf(int carIdx) =>
            crossings?.HeldPosition(carIdx) is { } held ? held
            : positions is not null && carIdx < positions.Length && positions[carIdx] > 0 ? positions[carIdx] : 0;

        var eligible = new List<DriverEntry>();
        foreach (var driver in racing)
        {
            var isPlayer = driver.CarIdx == playerCarIdx;
            var lap = driver.CarIdx < currentLaps.Length ? currentLaps[driver.CarIdx] : -1;
            var hasOfficialPosition = OfficialPositionOf(driver.CarIdx) > 0;

            if (!isPlayer && lap < 0)
            {
                // CurrentLap == -1 is iRacing's "not in the world" sentinel. A classified car that has
                // raced keeps its place while it is towed or disconnected. One that never ran a lap
                // stays out — confirmed live: a solo Test session's placeholder AI roster all sat at
                // Lap -1 yet carried an assigned CarIdxPosition, and showed up as a full grid of cars
                // tied on one meaningless gap.
                var hasRaced = LapsCompletedOf(driver.CarIdx) > 0 || crossings?.LastCrossing(driver.CarIdx) is not null;
                if (!hasOfficialPosition || !hasRaced)
                {
                    continue;
                }
            }
            else if (!isPlayer && !hasOfficialPosition && lap == 0 &&
                     (driver.CarIdx >= lapDistPct.Length || lapDistPct[driver.CarIdx] <= 0))
            {
                continue; // car not yet out on track this session
            }

            eligible.Add(driver);
        }

        double LastCrossedAt(DriverEntry driver) => crossings?.LastCrossing(driver.CarIdx)?.Time ?? double.MaxValue;

        // Official order first. Ties only arise without it, and are settled the way the official
        // order is: laps completed, then who completed the latest of them first. Stable, so cars
        // with nothing to tell them apart keep the roster order rather than shuffling.
        var ordered = eligible
            .OrderBy(d => OfficialPositionOf(d.CarIdx) is > 0 and var official ? official : int.MaxValue)
            .ThenByDescending(d => LapsCompletedOf(d.CarIdx))
            .ThenBy(LastCrossedAt)
            .ToList();

        // Gaps are measured against each class's own leader, not the overall one: telling a GT3
        // driver they are 45s behind a prototype is a number they can do nothing with.
        var carsByClass = racing
            .GroupBy(d => d.CarClassID)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<int>)g.Select(d => d.CarIdx).ToList());
        var firstCrossings = new Dictionary<(int ClassId, int Lap), double?>();

        double? FirstOfClassToComplete(int classId, int lap)
        {
            if (!firstCrossings.TryGetValue((classId, lap), out var first))
            {
                first = crossings?.FirstCrossing(carsByClass[classId], lap);
                firstCrossings[(classId, lap)] = first;
            }

            return first;
        }

        // At the moment it crossed: how far behind the first of its class to complete that lap, and
        // how many further laps that car had already completed by then.
        (double Seconds, int LapsDown) GapAtTheLine(DriverEntry driver)
        {
            if (crossings?.LastCrossing(driver.CarIdx) is not { } crossed ||
                FirstOfClassToComplete(driver.CarClassID, crossed.Lap) is not { } first)
            {
                return (double.NaN, 0);
            }

            var lapsDown = 0;
            while (FirstOfClassToComplete(driver.CarClassID, crossed.Lap + lapsDown + 1) is { } ahead && ahead < crossed.Time)
            {
                lapsDown++;
            }

            return (crossed.Time - first, lapsDown);
        }

        var fastestLapByClass = FastestLapByClass(ordered, laps.Best);
        var iRatingDeltaByCarIdx = EstimateIRatingDeltas(ordered);

        // Ranked last of all and only now: a ghost player is listed, but takes no place, gap or
        // iRating duel from anyone in the race.
        if (GhostPlayer(driverInfo) is { } ghost)
        {
            ordered.Add(ghost);
        }

        var classRank = new Dictionary<int, int>();
        var rows = new List<StandingsRow>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var driver = ordered[i];
            var rank = driver.IsGhost ? 0 : classRank.GetValueOrDefault(driver.CarClassID) + 1;
            if (!driver.IsGhost)
            {
                classRank[driver.CarClassID] = rank;
            }

            var bestLapTime = laps.Best(driver.CarIdx);
            var penalties = penaltiesOf(driver.CarIdx);
            var gap = driver.IsGhost ? (Seconds: double.NaN, LapsDown: 0) : GapAtTheLine(driver);

            rows.Add(new StandingsRow
            {
                CarIdx = driver.CarIdx,
                Position = driver.IsGhost ? 0 : i + 1,
                ClassPosition = rank,
                StartPosition = grid.TryGetValue(driver.CarIdx, out var slot) ? slot.Position : 0,
                StartClassPosition = slot.ClassPosition,
                Name = driver.UserName,
                CarNumber = driver.CarNumber,
                IsPlayer = driver.CarIdx == playerCarIdx,
                IsGhost = driver.IsGhost,
                OnPitRoad = onPitRoad is not null && driver.CarIdx < onPitRoad.Length && onPitRoad[driver.CarIdx],
                HasBlackFlag = penalties.Black,
                HasFurledFlag = penalties.Furled,
                HasMeatballFlag = penalties.Meatball,
                LastPitStop = LastPitStopOf(lastPitStops, driver.CarIdx),
                TireCompound = compoundOf(driver.CarIdx),
                CarBrand = CarBrand.ForCar(driver.CarScreenName),
                FlairName = driver.FlairName,
                CurrentLap = LapCountOf(driver.CarIdx),
                GapToLeaderSeconds = gap.Seconds,
                LapsDown = gap.LapsDown,
                LastLapTime = laps.Last(driver.CarIdx),
                BestLapTime = bestLapTime,
                IsMultiClass = isMultiClass,
                IRating = driver.IRating,
                LicString = driver.LicString,
                IRatingDelta = iRatingDeltaByCarIdx.GetValueOrDefault(driver.CarIdx, 0),
                IsSessionFastestLap = !driver.IsGhost && bestLapTime > 0 && bestLapTime <= fastestLapByClass.GetValueOrDefault(driver.CarClassID),
                ClassColor = ClassColorFormat.Normalize(driver.CarClassColor),
                CarClassID = driver.CarClassID,
                // The category, never the car: blank for a spec series, where the header falls back
                // to the class id.
                CarClassName = driver.CarClassShortName,
            });
        }

        return rows;
    }

    /// <summary>
    /// Every car's place in the race right now, overall and in its class, by how far round the race
    /// it is (<see cref="TrackPosition.RaceDistance"/>). It knows only where cars are, not what
    /// official scoring decides on its own — that catches up as each car crosses the line. A car
    /// iRacing can't place this tick (towed) is counted at the start of the lap after its last
    /// scored one; one that has never been out isn't counted. Grid ties fall to official order.
    /// </summary>
    private static Dictionary<int, (int Position, int ClassPosition)> LiveRaceRanks(
        IReadOnlyList<DriverEntry> racing,
        int[] laps,
        float[] lapDistPct,
        IReadOnlyDictionary<int, SessionResultPosition> results,
        int[]? positions)
    {
        var placed = new List<(DriverEntry Driver, double RaceDistance)>();
        foreach (var driver in racing)
        {
            var raceDistance = TrackPosition.Read(laps, lapDistPct, driver.CarIdx)?.RaceDistance
                ?? (results.TryGetValue(driver.CarIdx, out var scored) && scored.LapsComplete > 0 ? scored.LapsComplete + 1 : null);
            if (raceDistance is { } distance)
            {
                placed.Add((driver, distance));
            }
        }

        int Official(int carIdx) =>
            positions is not null && carIdx < positions.Length && positions[carIdx] > 0 ? positions[carIdx] : int.MaxValue;

        var ranks = new Dictionary<int, (int, int)>();
        var classRank = new Dictionary<int, int>();
        var position = 0;
        foreach (var (driver, _) in placed.OrderByDescending(p => p.RaceDistance).ThenBy(p => Official(p.Driver.CarIdx)))
        {
            var inClass = classRank.GetValueOrDefault(driver.CarClassID) + 1;
            classRank[driver.CarClassID] = inClass;
            ranks[driver.CarIdx] = (++position, inClass);
        }

        return ranks;
    }

    /// <summary>Green flag to chequered: the stretch where cars are actually racing for position.
    /// On the grid, in formation and after the finish the official order is the one that counts.</summary>
    private static bool IsRacing(TelemetrySnapshot telemetry) =>
        telemetry.HasVariable(TelemetryVarNames.SessionState) &&
        telemetry.GetInt(TelemetryVarNames.SessionState) == SessionStateRacing;

    /// <summary>irsdk_SessionState: 4 = racing.</summary>
    private const int SessionStateRacing = 4;

    /// <summary>The cars in the race: no pace car, and no spectators driving as ghosts — they are
    /// invisible to the field and not scored, so they take no place and no part in the iRating.</summary>
    private static List<DriverEntry> Racing(DriverInfoSection driverInfo) =>
        driverInfo.Drivers.Where(d => !d.IsPaceCar && !d.IsGhost && d.CarIdx >= 0).ToList();

    /// <summary>
    /// The car the tables treat as the player's: highlighted, and the one Relative and Standings are
    /// centred on. It is the car the camera is on (CamCarIdx), which is the player's own while they
    /// drive; spectating, in the garage or in a replay it is whoever they are watching, as if they
    /// were in that car. A camera on the pace car, or on nothing in the field, keeps the player's own.
    /// </summary>
    internal static int FocusCarIdx(TelemetrySnapshot telemetry, DriverInfoSection driverInfo)
    {
        if (telemetry.HasVariable(TelemetryVarNames.CamCarIdx))
        {
            var camera = telemetry.GetInt(TelemetryVarNames.CamCarIdx);
            if (camera != driverInfo.DriverCarIdx && Racing(driverInfo).Any(d => d.CarIdx == camera))
            {
                return camera;
            }
        }

        return driverInfo.DriverCarIdx;
    }

    /// <summary>The player, when they are a spectator driving as a ghost; null otherwise. The tables
    /// still list them, unranked. Other ghosts aren't listed at all.</summary>
    private static DriverEntry? GhostPlayer(DriverInfoSection driverInfo) =>
        driverInfo.Drivers.FirstOrDefault(d => d.CarIdx == driverInfo.DriverCarIdx && d.CarIdx >= 0 && d.IsGhost && !d.IsPaceCar);

    private static bool HasTrackPositions(TelemetrySnapshot telemetry) =>
        telemetry.HasVariable(TelemetryVarNames.CarIdxLap) && telemetry.HasVariable(TelemetryVarNames.CarIdxLapDistPct);

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

        return StrengthOf(driverInfo.Drivers.Where(d => !d.IsPaceCar && !d.IsGhost).Select(d => d.IRating));
    }

    /// <summary>iRacing's strength-of-field formula over a set of iRatings; unrated (0) drivers
    /// are left out, and nobody rated gives 0. Used for the whole field and for each class.</summary>
    internal static double StrengthOf(IEnumerable<int> iratings)
    {
        var rated = iratings.Where(rating => rating > 0).Select(rating => (double)rating).ToList();
        if (rated.Count == 0)
        {
            return 0;
        }

        var br1 = 1600.0 / Math.Log(2);
        var sum = rated.Sum(r => Math.Exp(-r / br1));
        return sum > 0 ? br1 * Math.Log(rated.Count / sum) : 0;
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
    /// rest of Standings. In multiclass each class is rated as its own race, as iRacing does: a car
    /// only duels the cars of its class, and the field size is its class's.
    /// </summary>
    private static Dictionary<int, double> EstimateIRatingDeltas(List<DriverEntry> ordered)
    {
        var result = new Dictionary<int, double>();
        foreach (var classField in ordered.GroupBy(d => d.CarClassID))
        {
            EstimateClassIRatingDeltas(classField.ToList(), result);
        }

        return result;
    }

    private static void EstimateClassIRatingDeltas(List<DriverEntry> ordered, Dictionary<int, double> result)
    {
        var rated = new List<(int CarIdx, int IRating, int Position)>();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].IRating > 0)
            {
                rated.Add((ordered[i].CarIdx, ordered[i].IRating, i));
            }
        }

        var n = rated.Count;
        if (n < 2)
        {
            return;
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
                ClassName = ClassHeaderLabel(className, classId),
                ClassColor = classRows[0].ClassColor,
                DriverCount = classRows.Count(row => !row.IsGhost),
                Sof = StrengthOf(classRows.Where(row => !row.IsGhost).Select(row => row.IRating)),
            });

            display.AddRange(classId == playerClassId ? classRows : classRows.Take(otherClassLimit));
        }

        return display;
    }

    /// <summary>
    /// Cuts the full field down to what a compact overlay can usefully show: the podium, always
    /// pinned, plus a block of <paramref name="maxDynamicDrivers"/> cars centred on the player. That
    /// answers the two questions a driver actually asks mid-race — who's winning, and who is right
    /// around me — without a table that grows with the entry list.
    ///
    /// The dynamic window never starts above <paramref name="topCount"/>, which is what keeps a car
    /// from appearing twice when the player is running near the front. When the window would run off
    /// either end of the field it slides back inside rather than shrinking, so the widget keeps the
    /// same height whether the player is P1, P12 or last. A separator row goes between the two
    /// blocks whenever there is a block below the podium at all, so the layout doesn't reshuffle the
    /// moment a car is skipped or stops being skipped.
    ///
    /// Small sessions degrade cleanly by construction: fewer drivers than the window means the whole
    /// field is listed with nothing padded, and a single-entry session is a single row.
    /// </summary>
    public static List<object> BuildFocusedView(
        IReadOnlyList<StandingsRow> rows,
        int maxDynamicDrivers = DriverTableOptions.DefaultStandingsFocusSize,
        int topCount = 3)
    {
        var display = new List<object>();
        AppendFocusedBlock(display, OwnClassOnly(rows), maxDynamicDrivers, topCount);
        return display;
    }

    /// <summary>In a multiclass session the one block this view shows is the player's own class.
    /// The overall podium belongs to whichever category is quickest and is a race the player isn't
    /// in — pinning it would spend three of the few rows available on drivers they can never be
    /// classified against. Single-class sessions pass straight through.</summary>
    private static IReadOnlyList<StandingsRow> OwnClassOnly(IReadOnlyList<StandingsRow> rows)
    {
        if (rows.Count == 0 || !rows[0].IsMultiClass)
        {
            return rows;
        }

        // Spectating (no player row) falls back to the leading class, which is the one the overall
        // order already puts first.
        var playerClassId = rows.FirstOrDefault(r => r.IsPlayer)?.CarClassID ?? rows[0].CarClassID;
        return rows.Where(r => r.CarClassID == playerClassId).ToList();
    }

    /// <summary>
    /// The multiclass layout: every class gets its own header and its own pinned top
    /// <paramref name="topCount"/>, and the player's class additionally gets the block of cars
    /// around them. Other classes stop at their podium — the point of showing them is context on who
    /// leads the categories sharing the track, not a second race to follow.
    ///
    /// Classes are laid out with the player's own first, then the rest in the order their leaders sit
    /// overall. Single-class sessions fall straight through to <see cref="BuildFocusedView"/>: a
    /// header naming the only class in the session is pure noise.
    /// </summary>
    public static List<object> BuildMulticlassView(
        IReadOnlyList<StandingsRow> rows,
        int maxDynamicDrivers = DriverTableOptions.DefaultStandingsFocusSize,
        int topCount = 3)
    {
        if (rows.Count == 0 || !rows[0].IsMultiClass)
        {
            return BuildFocusedView(rows, maxDynamicDrivers, topCount);
        }

        // Rows arrive in overall order, so grouping by first appearance keeps each class's own rows
        // in class order and makes the first row recorded for a class that class's leader.
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

        var playerClassId = rows.FirstOrDefault(r => r.IsPlayer)?.CarClassID ?? classOrder[0];

        var display = new List<object>();
        foreach (var classId in classOrder.OrderBy(id => id == playerClassId ? 0 : 1))
        {
            var classRows = byClass[classId];
            var className = classRows[0].CarClassName;
            display.Add(new StandingsHeaderRow
            {
                ClassName = ClassHeaderLabel(className, classId),
                ClassColor = classRows[0].ClassColor,
                DriverCount = classRows.Count(row => !row.IsGhost),
                Sof = StrengthOf(classRows.Where(row => !row.IsGhost).Select(row => row.IRating)),
            });

            if (classId == playerClassId)
            {
                AppendFocusedBlock(display, classRows, maxDynamicDrivers, topCount);
            }
            else
            {
                // Take, not a fixed count: a class with two entries contributes two rows, never a
                // padded-out block.
                display.AddRange(classRows.Take(topCount));
            }
        }

        return display;
    }

    private static void AppendFocusedBlock(
        List<object> display,
        IReadOnlyList<StandingsRow> rows,
        int maxDynamicDrivers,
        int topCount)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var pinned = Math.Min(topCount, rows.Count);
        for (var i = 0; i < pinned; i++)
        {
            display.Add(rows[i]);
        }

        if (rows.Count == pinned)
        {
            return;
        }

        var windowSize = Math.Clamp(maxDynamicDrivers, 1, rows.Count - pinned);

        var playerIndex = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].IsPlayer)
            {
                playerIndex = i;
                break;
            }
        }

        // Spectating, or the player is still in the podium block: either way the useful thing to
        // show underneath is simply the next cars down the order.
        var anchor = playerIndex < pinned ? pinned : playerIndex;

        // Bias the window so the extra car of an even-sized block goes behind the player: what's
        // coming up behind matters more than one more car already up the road.
        var start = anchor - (windowSize - 1) / 2;
        start = Math.Clamp(start, pinned, rows.Count - windowSize);

        display.Add(new StandingsSeparatorRow { SkippedCount = start - pinned });
        for (var i = start; i < start + windowSize; i++)
        {
            display.Add(rows[i]);
        }
    }

    /// <summary>
    /// A class block's header: the class name in capitals without the word "Class" iRacing often
    /// appends ("GT3 Class" → "GT3"). A spec series leaves the name blank, and a name that is only
    /// "Class" says nothing either; both fall back to "CLASS n", the one place the word still earns
    /// its keep, since a bare number wouldn't read as a class.
    /// </summary>
    internal static string ClassHeaderLabel(string? className, int classId)
    {
        var name = Regex.Replace(className ?? "", @"\bclass\b", " ", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\s+", " ").Trim();
        return name.Length == 0 ? $"CLASS {classId}" : name.ToUpperInvariant();
    }

    /// <summary>
    /// The running session, as the Standings header names it: RACE, QUALIFYING or PRACTICE, then
    /// WARMUP or TESTING; any other type in capitals, and empty when the session can't be identified.
    /// Read from the type rather than the name, since the name is free text an event can set.
    /// </summary>
    public static string SessionTypeLabel(TelemetrySnapshot telemetry, IracingSessionInfo? session) =>
        SessionTypeLabel(CurrentSession.Entry(telemetry, session)?.SessionType);

    internal static string SessionTypeLabel(string? type) => (type ?? "").Trim() switch
    {
        "" => "",
        var t when t.Contains("Race", StringComparison.OrdinalIgnoreCase) => "RACE",
        var t when t.Contains("Qualify", StringComparison.OrdinalIgnoreCase) => "QUALIFYING",
        var t when t.Contains("Practice", StringComparison.OrdinalIgnoreCase) => "PRACTICE",
        var t when t.Contains("Warmup", StringComparison.OrdinalIgnoreCase) => "WARMUP",
        var t when t.Contains("Testing", StringComparison.OrdinalIgnoreCase) => "TESTING",
        var t => t.ToUpperInvariant(),
    };

    /// <summary>Each car's starting grid slot (1-based, overall and in class), keyed by CarIdx.
    /// Empty outside a race: the grid block also sits in the YAML through the weekend's practice
    /// and qualifying, where "places since the start" means nothing.</summary>
    internal static Dictionary<int, (int Position, int ClassPosition)> StartingGrid(TelemetrySnapshot telemetry, IracingSessionInfo? session)
    {
        var grid = new Dictionary<int, (int, int)>();
        if (!IsRaceSession(telemetry, session))
        {
            return grid;
        }

        foreach (var slot in session?.QualifyResultsInfo?.Results ?? [])
        {
            if (slot.CarIdx >= 0 && slot.Position >= 0)
            {
                grid[slot.CarIdx] = (slot.Position + 1, slot.ClassPosition + 1);
            }
        }

        return grid;
    }

    private static bool IsRaceSession(TelemetrySnapshot telemetry, IracingSessionInfo? session) =>
        (CurrentSession.Entry(telemetry, session)?.SessionType ?? "").Contains("Race", StringComparison.OrdinalIgnoreCase);

    private static bool IsPracticeOrQualifyingSession(TelemetrySnapshot telemetry, IracingSessionInfo? session)
    {
        // No identifiable session means the race path, which is the one that degrades gracefully:
        // it orders by laps and track position, both of which are meaningful in every session type.
        var type = CurrentSession.Entry(telemetry, session)?.SessionType ?? "";
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
        DriverInfoSection driverInfo,
        Func<int, int> lapCountOf,
        LapTimeSource laps,
        bool[]? onPitRoad,
        Func<int, CarPenalties> penaltiesOf,
        Func<int, TireCompound?> compoundOf,
        int playerCarIdx,
        bool isMultiClass,
        SessionBestLapTracker bestLapTracker,
        int sessionNum,
        IReadOnlyDictionary<int, PitStop>? lastPitStops)
    {
        var drivers = Racing(driverInfo);
        var ghost = GhostPlayer(driverInfo);
        var cachedBest = bestLapTracker.Update(
            sessionNum, drivers.Select(d => d.CarIdx).Concat(ghost is null ? [] : [ghost.CarIdx]), laps);

        double QualTime(DriverEntry d) => cachedBest.TryGetValue(d.CarIdx, out var t) && t > 0 ? t : double.MaxValue;

        // Drivers with no time yet sort to the bottom (double.MaxValue), stable-tied by CarIdx. A
        // ghost player goes after everyone, unranked: their lap isn't in the session's order.
        var ordered = drivers.OrderBy(QualTime).ThenBy(d => d.CarIdx).ToList();
        if (ghost is not null)
        {
            ordered.Add(ghost);
        }

        var fastestLapByClass = FastestLapByClass(drivers, carIdx => cachedBest.GetValueOrDefault(carIdx, 0));

        // Same reasoning as the race path: the benchmark is the class's own pole, not the outright
        // fastest car in the session. `ordered` is sorted fastest-first, so the first car seen for a
        // class holds that class's best time.
        var classPoleTime = new Dictionary<int, double>();
        foreach (var driver in drivers.OrderBy(QualTime).ThenBy(d => d.CarIdx))
        {
            if (!classPoleTime.ContainsKey(driver.CarClassID))
            {
                classPoleTime[driver.CarClassID] = QualTime(driver);
            }
        }

        var classRank = new Dictionary<int, int>();
        var rows = new List<StandingsRow>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var driver = ordered[i];
            var rank = 0;
            if (!driver.IsGhost)
            {
                rank = classRank.GetValueOrDefault(driver.CarClassID) + 1;
                classRank[driver.CarClassID] = rank;
            }

            var bestLapTime = cachedBest.GetValueOrDefault(driver.CarIdx, 0);
            var thisTime = QualTime(driver);
            var poleTime = classPoleTime.GetValueOrDefault(driver.CarClassID, double.MaxValue);
            var penalties = penaltiesOf(driver.CarIdx);

            rows.Add(new StandingsRow
            {
                CarIdx = driver.CarIdx,
                Position = driver.IsGhost ? 0 : i + 1,
                ClassPosition = rank,
                Name = driver.UserName,
                CarNumber = driver.CarNumber,
                IsPlayer = driver.CarIdx == playerCarIdx,
                IsGhost = driver.IsGhost,
                OnPitRoad = onPitRoad is not null && driver.CarIdx < onPitRoad.Length && onPitRoad[driver.CarIdx],
                HasBlackFlag = penalties.Black,
                HasFurledFlag = penalties.Furled,
                HasMeatballFlag = penalties.Meatball,
                LastPitStop = LastPitStopOf(lastPitStops, driver.CarIdx),
                TireCompound = compoundOf(driver.CarIdx),
                CarBrand = CarBrand.ForCar(driver.CarScreenName),
                FlairName = driver.FlairName,
                CurrentLap = lapCountOf(driver.CarIdx),
                GapToLeaderSeconds = thisTime < double.MaxValue && poleTime < double.MaxValue ? thisTime - poleTime : 0,
                LastLapTime = laps.Last(driver.CarIdx),
                BestLapTime = bestLapTime,
                IsMultiClass = isMultiClass,
                IRating = driver.IRating,
                LicString = driver.LicString,
                // A single pairwise-duel iRating estimate makes no sense against a fastest-lap order.
                IRatingDelta = 0,
                IsSessionFastestLap = !driver.IsGhost && bestLapTime > 0 && bestLapTime <= fastestLapByClass.GetValueOrDefault(driver.CarClassID),
                ClassColor = ClassColorFormat.Normalize(driver.CarClassColor),
                CarClassID = driver.CarClassID,
                CarClassName = driver.CarClassShortName,
            });
        }

        return rows;
    }

    /// <summary>Each class's fastest lap, keyed by CarClassID. Purple marks the best of every class, not
    /// only the field's overall fastest: a GT4 never out-laps a GTP, yet its drivers still need to see
    /// who is quickest among them. 0 (no valid lap yet) never counts.</summary>
    private static Dictionary<int, double> FastestLapByClass(IEnumerable<DriverEntry> drivers, Func<int, double> bestLapOf)
    {
        var fastest = new Dictionary<int, double>();
        foreach (var driver in drivers)
        {
            var best = bestLapOf(driver.CarIdx);
            if (best > 0 && (!fastest.TryGetValue(driver.CarClassID, out var classBest) || best < classBest))
            {
                fastest[driver.CarClassID] = best;
            }
        }

        return fastest;
    }

    /// <summary>Each car's current tyre, from CarIdxTireCompound named through the session's
    /// compound table. Resolved once per compound index per build.</summary>
    private static Func<int, TireCompound?> TireCompoundsOf(TelemetrySnapshot telemetry, DriverInfoSection driverInfo)
    {
        var compounds = TryGetIntArray(telemetry, TelemetryVarNames.CarIdxTireCompound);
        if (compounds is null)
        {
            return _ => null;
        }

        var named = new Dictionary<int, TireCompound?>();
        return carIdx =>
        {
            if (carIdx < 0 || carIdx >= compounds.Length)
            {
                return null;
            }

            var index = compounds[carIdx];
            if (!named.TryGetValue(index, out var compound))
            {
                compound = TireCompound.Resolve(index, driverInfo.DriverTires);
                named[index] = compound;
            }

            return compound;
        };
    }

    private static PitStop? LastPitStopOf(IReadOnlyDictionary<int, PitStop>? lastPitStops, int carIdx) =>
        lastPitStops is not null && lastPitStops.TryGetValue(carIdx, out var stop) ? stop : null;

    private static bool[]? TryGetBoolArray(TelemetrySnapshot telemetry, string name) =>
        telemetry.HasVariable(name) ? telemetry.GetBoolArray(name) : null;

    private static float[]? TryGetFloatArray(TelemetrySnapshot telemetry, string name) =>
        telemetry.HasVariable(name) ? telemetry.GetFloatArray(name) : null;

    private static int[]? TryGetIntArray(TelemetrySnapshot telemetry, string name) =>
        telemetry.HasVariable(name) ? telemetry.GetIntArray(name) : null;
}
