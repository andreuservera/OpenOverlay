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
    /// Built in two separate layers. Which cars show, and in what order, comes only from where each
    /// car physically is (<see cref="TrackPosition"/>), folded to the nearest half lap around the
    /// player — continuous, needing no lap time at all, and impossible for classes running different
    /// paces to reshuffle. Seconds come afterwards: that separation priced at one lap time for the
    /// whole table, the pace of the player's class (<see cref="ReferencePace"/>), so the same distance
    /// reads as the same gap whichever class the other car is in.
    ///
    /// The fold only picks who is near the player on the road. Everything about the race itself —
    /// position, class position, lapped and lapping — comes from absolute race distance, the running
    /// order Standings uses (<see cref="RaceOrder"/>). Folding throws whole laps away, so a car a lap
    /// down right behind would otherwise look like a rival on the same lap.
    ///
    /// Why the fold for the window rather than race distance: CarIdxLap only counts laps since the
    /// session started, so in Practice/Qualifying a car dozens of laps ahead in count can be running
    /// right alongside. Pricing that lap-count difference produced gaps of thousands of seconds for
    /// cars side by side (reported live).
    ///
    /// Always includes the player, even alone with no one else on track.
    /// </summary>
    public static List<object> BuildRelative(
        TelemetrySnapshot telemetry,
        IracingSessionInfo? session,
        int maxEachSide = DriverTableOptions.DefaultRelativeFocusSize,
        IReadOnlyList<StandingsRow>? standings = null,
        IReadOnlyDictionary<int, PitStop>? lastPitStops = null)
    {
        if (session?.DriverInfo is not { } driverInfo || !HasTrackPositions(telemetry))
        {
            return [];
        }

        var carIdxLap = telemetry.GetIntArray(TelemetryVarNames.CarIdxLap);
        var carIdxLapDistPct = telemetry.GetFloatArray(TelemetryVarNames.CarIdxLapDistPct);
        var positions = TryGetIntArray(telemetry, TelemetryVarNames.CarIdxPosition);
        var lastLaps = TryGetFloatArray(telemetry, TelemetryVarNames.CarIdxLastLapTime);
        var bestLaps = TryGetFloatArray(telemetry, TelemetryVarNames.CarIdxBestLapTime);
        var onPitRoad = TryGetBoolArray(telemetry, TelemetryVarNames.CarIdxOnPitRoad);

        var playerCarIdx = driverInfo.DriverCarIdx;
        if (playerCarIdx < 0 || playerCarIdx >= carIdxLap.Length)
        {
            return [];
        }

        var penaltiesOf = FlagBuilder.ReadCarPenalties(telemetry, playerCarIdx);
        var compoundOf = TireCompoundsOf(telemetry, driverInfo);

        var racing = Racing(driverInfo);
        var isMultiClass = racing.Select(d => d.CarClassID).Distinct().Count() > 1;
        var playerClassId = racing.FirstOrDefault(d => d.CarIdx == playerCarIdx)?.CarClassID;

        // Reads the scoring table too: on a mid-session attach the telemetry lap arrays are still empty.
        var results = CurrentSession.Results(telemetry, session);
        var laps = new LapTimeSource(bestLaps, lastLaps, results);
        var pace = new ReferencePace(laps, racing, telemetry, session);
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

        // Wherever Standings runs the race order, position and class position come from that same
        // RaceOrder, worked out afresh from this very tick. Standings itself only rebuilds about once a
        // second, and borrowing its rows let a car the Relative already showed ahead keep the worse
        // position for up to that long. In practice and qualifying the position is Standings'
        // fastest-lap ranking, which needs its memory of parked cars' times and only changes as laps
        // complete. iRacing's own CarIdxPosition is only a fallback: it sits at 0 through practice and
        // test sessions. The iRating estimate needs the whole field and always comes from Standings.
        var raceRanks = IsPracticeOrQualifyingSession(telemetry, session)
            ? null
            : Rank(RaceOrder(racing, carIdxLap, carIdxLapDistPct, positions, playerCarIdx));
        var standingsByCarIdx = new Dictionary<int, StandingsRow>();
        foreach (var row in standings ?? [])
        {
            standingsByCarIdx[row.CarIdx] = row;
        }

        int PositionOf(int carIdx)
        {
            if (raceRanks is not null && raceRanks.TryGetValue(carIdx, out var rank))
            {
                return rank.Position;
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
            if (raceRanks is not null && raceRanks.TryGetValue(carIdx, out var rank))
            {
                return rank.ClassPosition;
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

        var placed = new List<(RelativeRow Row, double LapsAhead)>();
        foreach (var driver in racing)
        {
            if (driver.CarIdx >= carIdxLap.Length)
            {
                continue;
            }

            var isPlayer = driver.CarIdx == playerCarIdx;

            // CurrentLap == -1 is iRacing's own "never left the garage this session" sentinel —
            // never include such a car regardless of any other signal (see RaceOrder for the
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
            if (!isPlayer)
            {
                if (onTrack is not { } theirs || playerOnTrack is not { } mine)
                {
                    continue;
                }

                lapsAhead = mine.OnTrackGapTo(theirs);
            }

            // The pricing layer: separation on the road to seconds at the player's class pace,
            // negative ahead. NaN (shown as a dash) until anyone in the class has a lap to go on.
            var gapSeconds = isPlayer ? 0
                : playerClassId is { } classId ? pace.SecondsFor(-lapsAhead, classId)
                : double.NaN;
            var bestLapTime = laps.Best(driver.CarIdx);
            var penalties = penaltiesOf(driver.CarIdx);
            placed.Add((new RelativeRow
            {
                CarIdx = driver.CarIdx,
                Position = PositionOf(driver.CarIdx),
                ClassPosition = ClassPositionOf(driver.CarIdx),
                Name = driver.UserName,
                CarNumber = driver.CarNumber,
                IsPlayer = isPlayer,
                GapSeconds = gapSeconds,
                OnPitRoad = onPitRoad is not null && driver.CarIdx < onPitRoad.Length && onPitRoad[driver.CarIdx],
                HasBlackFlag = penalties.Black,
                HasFurledFlag = penalties.Furled,
                HasMeatballFlag = penalties.Meatball,
                LastPitStop = LastPitStopOf(lastPitStops, driver.CarIdx),
                TireCompound = compoundOf(driver.CarIdx),
                CurrentLap = LapCountOf(driver.CarIdx),
                LastLapTime = laps.Last(driver.CarIdx),
                BestLapTime = bestLapTime,
                IsMultiClass = isMultiClass,
                IRating = driver.IRating,
                LicString = driver.LicString,
                IRatingDelta = IRatingDeltaOf(driver.CarIdx),
                IsSessionFastestLap = bestLapTime > 0 && bestLapTime <= fastestLapByClass.GetValueOrDefault(driver.CarClassID),
                ClassColor = ClassColorFormat.Normalize(driver.CarClassColor),
                CarClassID = driver.CarClassID,
                CarClassName = driver.CarClassShortName,
                LapRelation = !isPlayer && onTrack is { } position ? LapRelationOf(position, gapSeconds) : LapRelation.SameLap,
            }, lapsAhead));
        }

        // Ordered by position on the road, never by the seconds derived from it: furthest up first.
        var rows = placed.OrderByDescending(p => p.LapsAhead).Select(p => p.Row).ToList();

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
        var slots = Math.Min(maxEachSide * 2 + 1, racing.Count);
        while (window.Count < slots)
        {
            window.Add(new RelativePlaceholderRow());
        }

        return window;
    }

    /// <summary>
    /// In a race the order is <see cref="RaceOrder"/> — the same one Relative takes its positions
    /// from — recomputed continuously rather than read from iRacing's own CarIdxPosition/CarIdxF2Time.
    /// Those official values are only recomputed at scoring-line crossings (effectively once per lap),
    /// which is exactly the "standings only updates when finishing a lap" behavior reported live —
    /// using them made the whole table look frozen mid-lap.
    ///
    /// Each gap is the race distance to the car's class leader, whole laps included, priced at that
    /// class's pace (<see cref="ReferencePace"/>): a lap down reads as a lap of that class, and a gap
    /// runs on smoothly as the leader crosses the line instead of jumping by a lap time.
    ///
    /// Practice and Qualifying rank by fastest lap instead (<see cref="BuildFastestLapStandings"/>).
    /// </summary>
    public static List<StandingsRow> BuildStandings(
        TelemetrySnapshot telemetry,
        IracingSessionInfo? session,
        SessionBestLapTracker? bestLapTracker = null,
        IReadOnlyDictionary<int, PitStop>? lastPitStops = null)
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
        var playerCarIdx = driverInfo.DriverCarIdx;
        var penaltiesOf = FlagBuilder.ReadCarPenalties(telemetry, playerCarIdx);
        var compoundOf = TireCompoundsOf(telemetry, driverInfo);

        var distinctClasses = driverInfo.Drivers
            .Where(d => !d.IsPaceCar)
            .Select(d => d.CarClassID)
            .Distinct()
            .Count();
        var isMultiClass = distinctClasses > 1;

        var results = CurrentSession.Results(telemetry, session);
        var laps = new LapTimeSource(bestLaps, lastLaps, results);

        // CarIdxLap is -1 whenever a car isn't on track — sitting in the garage, or back in the pit
        // stall after a run — which says nothing about how many laps they actually ran. The scoring
        // table keeps the real count, and since a parked car has no lap in progress its "completed"
        // figure is exactly the right number to show. Still -1 with nothing scored either means the
        // car genuinely has never been out.
        int LapCountOf(int carIdx)
        {
            var live = carIdx >= 0 && carIdx < currentLaps.Length ? currentLaps[carIdx] : -1;
            if (live >= 0)
            {
                return live;
            }

            return results.TryGetValue(carIdx, out var scored) && scored.LapsComplete > 0 ? scored.LapsComplete : live;
        }

        // Practice and Qualifying both need every driver in the session ranked by their own best
        // lap time, not the on-track running order: a driver who's set a fast lap and driven back to
        // their pit stall still needs to show up (and know their grid slot / where their pace ranks),
        // even though they're now stationary in the pits and iRacing can drop their live
        // CarIdxBestLapTime/CarIdxLastLapTime/CarIdxLap back toward the "not on track" values that
        // RaceOrder's eligibility check would otherwise exclude them for.
        if (IsPracticeOrQualifyingSession(telemetry, session))
        {
            return BuildFastestLapStandings(
                driverInfo, LapCountOf, laps, onPitRoad, penaltiesOf, compoundOf, playerCarIdx, isMultiClass,
                bestLapTracker ?? new SessionBestLapTracker(),
                CurrentSession.Number(telemetry, session),
                lastPitStops);
        }

        var racing = Racing(driverInfo);
        var order = RaceOrder(racing, currentLaps, lapDistPct, positions, playerCarIdx);
        var ranks = Rank(order);
        var pace = new ReferencePace(laps, racing, telemetry, session);

        // Gaps are measured against each class's own leader, not the overall one: telling a GT3
        // driver they are 45s behind a prototype is a number they can do nothing with. `order` is
        // in overall order, so the first car seen for a class is that class's leader. In a
        // single-class session this is simply the race leader.
        var classLeaderDistance = new Dictionary<int, double>();
        foreach (var (driver, raceDistance) in order)
        {
            classLeaderDistance.TryAdd(driver.CarClassID, raceDistance);
        }

        var ordered = order.Select(o => o.Driver).ToList();
        var fastestLapByClass = FastestLapByClass(ordered, laps.Best);
        var iRatingDeltaByCarIdx = EstimateIRatingDeltas(ordered);

        var rows = new List<StandingsRow>();
        foreach (var (driver, raceDistance) in order)
        {
            var rank = ranks[driver.CarIdx];
            var bestLapTime = laps.Best(driver.CarIdx);
            var penalties = penaltiesOf(driver.CarIdx);

            rows.Add(new StandingsRow
            {
                CarIdx = driver.CarIdx,
                Position = rank.Position,
                ClassPosition = rank.ClassPosition,
                Name = driver.UserName,
                CarNumber = driver.CarNumber,
                IsPlayer = driver.CarIdx == playerCarIdx,
                OnPitRoad = onPitRoad is not null && driver.CarIdx < onPitRoad.Length && onPitRoad[driver.CarIdx],
                HasBlackFlag = penalties.Black,
                HasFurledFlag = penalties.Furled,
                HasMeatballFlag = penalties.Meatball,
                LastPitStop = LastPitStopOf(lastPitStops, driver.CarIdx),
                TireCompound = compoundOf(driver.CarIdx),
                CurrentLap = LapCountOf(driver.CarIdx),
                GapToLeaderSeconds = pace.SecondsFor(classLeaderDistance[driver.CarClassID] - raceDistance, driver.CarClassID),
                LastLapTime = laps.Last(driver.CarIdx),
                BestLapTime = bestLapTime,
                IsMultiClass = isMultiClass,
                IRating = driver.IRating,
                LicString = driver.LicString,
                IRatingDelta = iRatingDeltaByCarIdx.GetValueOrDefault(driver.CarIdx, 0),
                IsSessionFastestLap = bestLapTime > 0 && bestLapTime <= fastestLapByClass.GetValueOrDefault(driver.CarClassID),
                ClassColor = ClassColorFormat.Normalize(driver.CarClassColor),
                CarClassID = driver.CarClassID,
                // The category, never the car: blank for a spec series, where the header falls back
                // to the class id.
                CarClassName = driver.CarClassShortName,
            });
        }

        return rows;
    }

    /// <summary>A car's place in the race: overall, and within its own class.</summary>
    private readonly record struct RaceRank(int Position, int ClassPosition);

    private static List<DriverEntry> Racing(DriverInfoSection driverInfo) =>
        driverInfo.Drivers.Where(d => !d.IsPaceCar && d.CarIdx >= 0).ToList();

    private static bool HasTrackPositions(TelemetrySnapshot telemetry) =>
        telemetry.HasVariable(TelemetryVarNames.CarIdxLap) && telemetry.HasVariable(TelemetryVarNames.CarIdxLapDistPct);

    /// <summary>
    /// The race running order, and the one definition of position Standings and Relative share:
    /// every car that has started, furthest round the race first by absolute
    /// <see cref="TrackPosition.RaceDistance"/> — laps plus the fraction of the current one.
    ///
    /// No lap time takes part, so nothing can weigh laps against track position. The order used to
    /// be laps * referenceLapTime + estTime, which is only valid while that reference is at least as
    /// long as any car's lap: attached to a race already running, the player had no lap time yet,
    /// too small a stand-in (a faster class, or simply a quicker car) let cars a lap down outrank cars
    /// a lap ahead, and no value at all reduced the sort to within-lap position. After that it read
    /// CarIdxEstTime, which runs on each car's own CarClassEstLapTime clock and so placed cars of
    /// different classes, or BoP'd models of one, slightly differently from where they really were.
    /// </summary>
    private static List<(DriverEntry Driver, double RaceDistance)> RaceOrder(
        IEnumerable<DriverEntry> racing, int[] laps, float[] lapDistPct, int[]? positions, int playerCarIdx)
    {
        var eligible = new List<(DriverEntry Driver, double RaceDistance)>();
        foreach (var driver in racing)
        {
            var isPlayer = driver.CarIdx == playerCarIdx;

            // CurrentLap == -1 is iRacing's own "never left the garage this session" sentinel.
            // Confirmed live: a solo Test session's placeholder AI roster all sat at Lap -1 but
            // still carried an assigned CarIdxPosition, which let them slip through as "eligible" and
            // show up as a full grid of cars all tied on an identical, meaningless gap. A real
            // position assignment does not override a car that plainly never went on track.
            var lap = driver.CarIdx < laps.Length ? laps[driver.CarIdx] : -1;
            if (!isPlayer && lap < 0)
            {
                continue;
            }

            var hasOfficialPosition = positions is not null && driver.CarIdx < positions.Length && positions[driver.CarIdx] > 0;
            var hasStarted = isPlayer
                || hasOfficialPosition
                || lap > 0
                || (driver.CarIdx < lapDistPct.Length && lapDistPct[driver.CarIdx] > 0);
            if (!hasStarted)
            {
                continue; // car not yet out on track this session
            }

            // A car iRacing can't place this tick keeps the laps it has run.
            var raceDistance = TrackPosition.Read(laps, lapDistPct, driver.CarIdx)?.RaceDistance ?? lap;
            eligible.Add((driver, raceDistance));
        }

        // Real bug reported live: right at a race's start, many cars can sit at near-identical track
        // position (everyone still on the grid). OrderBy is a *stable* sort, so ties fall back to
        // `eligible`'s original order — which is just DriverInfo's roster/YAML order, unrelated to
        // actual grid position. That let a driver who legitimately started last in their class appear
        // ahead of faster-starting classmates purely by roster-order coincidence. Breaking ties by
        // iRacing's own official CarIdxPosition (already assigned at grid formation, well before
        // anyone's first lap timing data exists) fixes this without reintroducing the "frozen until
        // lap end" staleness that's the whole reason official position isn't the *primary* sort key.
        int TieBreakPosition(int carIdx) =>
            positions is not null && carIdx < positions.Length && positions[carIdx] > 0 ? positions[carIdx] : int.MaxValue;

        return eligible
            .OrderByDescending(e => e.RaceDistance)
            .ThenBy(e => TieBreakPosition(e.Driver.CarIdx))
            .ToList();
    }

    /// <summary>Overall and class positions read off a running order.</summary>
    private static Dictionary<int, RaceRank> Rank(IReadOnlyList<(DriverEntry Driver, double RaceDistance)> order)
    {
        var ranks = new Dictionary<int, RaceRank>();
        var classRank = new Dictionary<int, int>();
        for (var i = 0; i < order.Count; i++)
        {
            var driver = order[i].Driver;
            var rank = classRank.GetValueOrDefault(driver.CarClassID) + 1;
            classRank[driver.CarClassID] = rank;
            ranks[driver.CarIdx] = new RaceRank(i + 1, rank);
        }

        return ranks;
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
                ClassName = string.IsNullOrWhiteSpace(className) ? $"CLASS {classId}" : className.ToUpperInvariant(),
                ClassColor = classRows[0].ClassColor,
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

    /// <summary>The category (class) the player races in, for the panel header — e.g. "GT3". Never
    /// the car's name: iRacing leaves the class name blank for a spec series, and then so is this.</summary>
    public static string PlayerClassName(IracingSessionInfo? session)
    {
        if (session?.DriverInfo is not { } driverInfo)
        {
            return "";
        }

        var racing = driverInfo.Drivers.Where(d => !d.IsPaceCar).ToList();
        if (racing.Count == 0)
        {
            return "";
        }

        // Spectating has no player entry: the first car's class stands in.
        var car = racing.FirstOrDefault(d => d.CarIdx == driverInfo.DriverCarIdx) ?? racing[0];
        return car.CarClassShortName?.Trim() ?? "";
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
        var drivers = driverInfo.Drivers.Where(d => !d.IsPaceCar && d.CarIdx >= 0).ToList();
        var cachedBest = bestLapTracker.Update(sessionNum, drivers.Select(d => d.CarIdx), laps);

        double QualTime(DriverEntry d) => cachedBest.TryGetValue(d.CarIdx, out var t) && t > 0 ? t : double.MaxValue;

        // Drivers with no time yet sort to the bottom (double.MaxValue), stable-tied by CarIdx.
        var ordered = drivers.OrderBy(QualTime).ThenBy(d => d.CarIdx).ToList();

        var fastestLapByClass = FastestLapByClass(drivers, carIdx => cachedBest.GetValueOrDefault(carIdx, 0));

        // Same reasoning as the race path: the benchmark is the class's own pole, not the outright
        // fastest car in the session. `ordered` is sorted fastest-first, so the first car seen for a
        // class holds that class's best time.
        var classPoleTime = new Dictionary<int, double>();
        foreach (var driver in ordered)
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
            classRank.TryGetValue(driver.CarClassID, out var rank);
            rank++;
            classRank[driver.CarClassID] = rank;

            var bestLapTime = cachedBest.GetValueOrDefault(driver.CarIdx, 0);
            var thisTime = QualTime(driver);
            var poleTime = classPoleTime[driver.CarClassID];
            var penalties = penaltiesOf(driver.CarIdx);

            rows.Add(new StandingsRow
            {
                CarIdx = driver.CarIdx,
                Position = i + 1,
                ClassPosition = rank,
                Name = driver.UserName,
                CarNumber = driver.CarNumber,
                IsPlayer = driver.CarIdx == playerCarIdx,
                OnPitRoad = onPitRoad is not null && driver.CarIdx < onPitRoad.Length && onPitRoad[driver.CarIdx],
                HasBlackFlag = penalties.Black,
                HasFurledFlag = penalties.Furled,
                HasMeatballFlag = penalties.Meatball,
                LastPitStop = LastPitStopOf(lastPitStops, driver.CarIdx),
                TireCompound = compoundOf(driver.CarIdx),
                CurrentLap = lapCountOf(driver.CarIdx),
                GapToLeaderSeconds = thisTime < double.MaxValue && poleTime < double.MaxValue ? thisTime - poleTime : 0,
                LastLapTime = laps.Last(driver.CarIdx),
                BestLapTime = bestLapTime,
                IsMultiClass = isMultiClass,
                IRating = driver.IRating,
                LicString = driver.LicString,
                // A single pairwise-duel iRating estimate makes no sense against a fastest-lap order.
                IRatingDelta = 0,
                IsSessionFastestLap = bestLapTime > 0 && bestLapTime <= fastestLapByClass.GetValueOrDefault(driver.CarClassID),
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
