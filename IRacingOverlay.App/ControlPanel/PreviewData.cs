using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// A believable race, frozen at one instant, for the preview to render.
///
/// The values are chosen to exercise the things a user is actually deciding between when they open
/// this panel: a mid-pack player rather than the leader (so the podium/focus split is visible), one
/// car in the pits and a purple fastest lap in each class (so both row states show up), a spread of
/// iRatings and licences (so those columns are worth their width), and a fuel picture that is
/// genuinely short of the finish (so the strategy figures aren't all neutral grey). A preview where
/// everything is nominal teaches nothing.
///
/// Nothing here reads telemetry or touches the SDK: the preview works with iRacing closed, which is
/// when people configure their overlay.
/// </summary>
public static class PreviewData
{
    // Position 9 of 24: below the podium, deep enough that a focus block of any size still has cars
    // on both sides of the player.
    private const int PlayerPosition = 9;

    private sealed record Entry(string Name, string Number, int IRating, string Licence, double Pace, int ClassIndex);

    private static readonly (string Name, string Color)[] Classes =
    [
        ("GT3 CLASS", "#33CEFF"),
        ("GT4 CLASS", "#FFB238"),
        ("TCR CLASS", "#FF4F8B"),
    ];

    // Names are invented but shaped like the real thing — a mix of lengths, including two long
    // enough to exercise DriverRow's abbreviation, so the driver column is judged at its worst case.
    private static readonly Entry[] Field =
    [
        new("Carlos Carrasco", "17", 6420, "A 4.21", 0.000, 0),
        new("Andreu Servera", "3", 5870, "A 3.88", 0.412, 0),
        new("Jan Kowalczyk", "44", 5240, "A 2.97", 1.104, 0),
        new("Marco Bianchi", "9", 4980, "B 4.55", 2.318, 1),
        new("Ana Ruiz Delgado", "28", 4610, "B 3.72", 3.006, 1),
        new("Kenji Nakamura", "71", 4330, "B 2.41", 4.221, 0),
        new("Owen Radcliffe", "5", 3980, "C 4.03", 5.517, 2),
        new("Pierre Lacroix", "62", 3720, "C 3.55", 6.884, 1),
        new("You", "24", 3510, "C 3.12", 7.902, 0),
        new("Diego Santoro", "11", 3340, "C 2.88", 8.741, 2),
        new("Hannah Brooks", "88", 3110, "C 2.10", 9.630, 1),
        new("Viktor Sorensen", "40", 2870, "D 4.44", 11.208, 0),
        new("Samir El Amrani", "56", 2640, "D 3.61", 12.855, 2),
        new("Rui Gonçalves", "7", 2410, "D 2.95", 14.402, 1),
        new("Ethan Caldwell", "33", 2180, "D 2.18", 16.117, 0),
        new("Nils Bergstrom", "19", 1960, "D 1.74", 18.330, 2),
        new("Paulo Cardoso", "51", 1740, "R 3.90", 20.541, 1),
        new("Mika Virtanen", "66", 1520, "R 3.02", 23.008, 0),
        new("Grace Whitmore", "12", 1380, "R 2.55", 25.774, 2),
        new("Andrei Popescu", "84", 1210, "R 2.01", 28.362, 1),
        new("Felix Wagner", "30", 1090, "R 1.66", 31.115, 0),
        new("Sofia Marchetti", "22", 980, "R 1.20", 34.008, 2),
        new("Liam O'Sullivan", "45", 860, "R 0.94", 37.442, 1),
        new("Noah Lindqvist", "2", 740, "R 0.55", 41.006, 0),
    ];

    private const double BaseLapTime = 92.418;

    private static readonly TireCompound PreviewHard = TireCompound.FromName("Hard");
    private static readonly TireCompound PreviewSoft = TireCompound.FromName("Soft");
    private static readonly TireCompound PreviewWet = TireCompound.FromName("Wet");

    /// <summary>A timed race a third of the way in: the lap total is the estimate from pace.</summary>
    public static SessionProgress Progress() => new()
    {
        CurrentLap = 7,
        EstimatedTotalLaps = 23.8,
        ElapsedSeconds = 1395,
        TotalSeconds = 3600,
    };

    /// <summary>The full field in running order, ready for the same builders the live widget uses.
    /// Feeding synthetic rows through <c>StandingsBuilder</c> rather than hand-assembling the
    /// display list is what makes the preview trustworthy: class headers, the podium split and the
    /// "+N cars" separator are produced by the production code path, so they can never disagree with
    /// what a race actually shows.</summary>
    public static List<StandingsRow> StandingsField(bool multiClass)
    {
        var rows = new List<StandingsRow>(Field.Length);
        var classPositions = new int[Classes.Length];

        for (var i = 0; i < Field.Length; i++)
        {
            var entry = Field[i];
            var classIndex = multiClass ? entry.ClassIndex : 0;
            classPositions[classIndex]++;

            rows.Add(new StandingsRow
            {
                CarIdx = i,
                Position = i + 1,
                ClassPosition = classPositions[classIndex],
                Name = entry.Name,
                CarNumber = entry.Number,
                IsPlayer = i == PlayerPosition - 1,
                OnPitRoad = i == 12,
                // Every penalty tag: black, furled black, meatball, and two at once split down the middle.
                HasBlackFlag = i == 6 || i == 10,
                HasFurledFlag = i == 5 || i == 15,
                HasMeatballFlag = i == 10 || i == 12 || i == 15,
                // Every third car has stopped, plus the one in the lane now, so both pit states show.
                LastPitStop = i % 3 == 1 || i == 12 ? new PitStop(9 + (i % 5), 64 + (i * 1.7)) : null,
                // Mostly slicks, a few gambling on wets, one on softs: every ring colour shows.
                TireCompound = i % 7 == 4 ? PreviewWet : i % 11 == 2 ? PreviewSoft : PreviewHard,
                CurrentLap = i < 3 ? 18 : 17,
                // Car 8 has just set a personal best, so the green last-lap state shows too.
                LastLapTime = i == 7
                    ? BaseLapTime + (entry.Pace * 0.05)
                    : BaseLapTime + (entry.Pace * 0.07) + ((i % 4) * 0.093),
                BestLapTime = BaseLapTime + (entry.Pace * 0.05),
                IsMultiClass = multiClass,
                IRating = entry.IRating,
                LicString = entry.Licence,
                IRatingDelta = 46 - (i * 4.7),
                // Pace only grows down the field, so each class's first car holds its fastest lap.
                IsSessionFastestLap = classPositions[classIndex] == 1,
                ClassColor = multiClass ? Classes[entry.ClassIndex].Color : "#B9C4CF",
                CarClassID = multiClass ? entry.ClassIndex + 1 : 1,
                CarClassName = multiClass ? Classes[entry.ClassIndex].Name : "GT3 CLASS",
                GapToLeaderSeconds = entry.Pace,
            });
        }

        return rows;
    }

    /// <summary>The Relative table's own rows: the player in the middle, the requested number of
    /// cars each side, gaps measured to the player and signed accordingly. Always mixed-class
    /// traffic, since that is where Relative earns its keep — and it shows the class bars.</summary>
    public static List<object> RelativeRows(int eachSide)
    {
        var field = StandingsField(multiClass: true);
        var playerIndex = PlayerPosition - 1;
        var first = Math.Max(0, playerIndex - eachSide);
        var last = Math.Min(field.Count - 1, playerIndex + eachSide);
        var playerPace = field[playerIndex].GapToLeaderSeconds;

        var rows = new List<object>();
        for (var i = first; i <= last; i++)
        {
            var source = field[i];
            rows.Add(new RelativeRow
            {
                CarIdx = source.CarIdx,
                Position = source.Position,
                ClassPosition = source.ClassPosition,
                Name = source.Name,
                CarNumber = source.CarNumber,
                IsPlayer = source.IsPlayer,
                OnPitRoad = source.OnPitRoad,
                HasBlackFlag = source.HasBlackFlag,
                HasFurledFlag = source.HasFurledFlag,
                HasMeatballFlag = source.HasMeatballFlag,
                LastPitStop = source.LastPitStop,
                TireCompound = source.TireCompound,
                CurrentLap = source.CurrentLap,
                LastLapTime = source.LastLapTime,
                BestLapTime = source.BestLapTime,
                IsMultiClass = true,
                IRating = source.IRating,
                LicString = source.LicString,
                IRatingDelta = source.IRatingDelta,
                IsSessionFastestLap = source.IsSessionFastestLap,
                ClassColor = source.ClassColor,
                CarClassID = source.CarClassID,
                CarClassName = source.CarClassName,
                // Cars ahead of the player read negative, behind positive — the sign is the whole
                // point of the column, so both have to appear in the preview.
                GapSeconds = (source.GapToLeaderSeconds - playerPace) * 0.42,
                // Traffic from both directions: a backmarker up the road, the one you're about to
                // lap just ahead, and a faster car closing to lap you.
                LapRelation = source.IsPlayer ? LapRelation.SameLap
                    : i == playerIndex - 1 ? LapRelation.BeingLapped
                    : i == first ? LapRelation.Lapped
                    : i == last ? LapRelation.Lapping
                    : LapRelation.SameLap,
            });
        }

        return rows;
    }

    public static CockpitState Cockpit() => new()
    {
        UnitSystem = Units.Current,
        Gear = "4",
        ShiftLightsLit = 9,
        ShiftBlink = false,
        AbsActive = false,
        AbsLevel = 3,
        SpeedKph = 214,
        Rpm = 7180,
        // One car half-alongside on the left, nothing on the right: shows both the lit and unlit
        // state of the proximity bars in a single frame.
        LeftProximity = new ProximitySide(0.55, 0.18, 0.73),
        RightProximity = ProximitySide.None,
        FuelLiters = 31.4,
        FuelPct = 0.46,
        Throttle = 0.82,
        Brake = 0,
        WaterTempC = 88,
        OilTempC = 104,
    };

    public static DeltaState Delta() => new()
    {
        DeltaSeconds = -0.184,
        RateOfChange = -0.06,
        IsValid = true,
        ReferenceLabel = "SESSION BEST",
    };

    public sealed record FlagScenario(string Label, IReadOnlyList<ActiveFlag> Flags);

    /// <summary>What the flag preview can simulate: a busy moment first (a primary flag with two
    /// advisories, so the stacking shows), then every flag and variant in
    /// the catalog — generated from it, so a newly added flag is previewable with no change here.</summary>
    public static IReadOnlyList<FlagScenario> FlagScenarios { get; } = BuildFlagScenarios();

    private static List<FlagScenario> BuildFlagScenarios()
    {
        var scenarios = new List<FlagScenario>
        {
            new("Race moment — waving yellow, debris, blue",
                [new(FlagKind.Yellow, FlagVariant.Waving), new(FlagKind.Debris), new(FlagKind.Blue)]),
        };

        foreach (var definition in FlagCatalog.All)
        {
            scenarios.Add(new(definition.Label, [new(definition.Kind)]));
            foreach (var variant in definition.Variants)
            {
                scenarios.Add(new($"{definition.Label} — {variant.Label}", [new(definition.Kind, variant.Variant)]));
            }
        }

        return scenarios;
    }

    /// <summary>Deliberately a few liters short of the finish, so the strategy figures render in
    /// their warning state and the refuel line has a real number in it.</summary>
    public static FuelCalculatorState FuelCalculator() => new()
    {
        UnitSystem = Units.Current,
        LevelLiters = 31.4,
        LevelPct = 0.46,
        LastLapLiters = 2.71,
        AverageLiters = 2.68,
        MinLiters = 2.54,
        MaxLiters = 2.89,
        LapsRemainingWithFuel = 11.7,
        LapsLeftInSession = 14,
        FuelToFinishLiters = 37.5,
        FuelDeltaLiters = -6.1,
        TankCapacityLiters = 68,
    };

    public static IncidentState Incidents() => new()
    {
        MyIncidentCount = 4,
        TeamIncidentCount = null,
        Limit = 17,
    };

    /// <summary>One braking event followed by full throttle, with ABS biting in the middle of the
    /// stop — the exact shape the trace exists to let you study, rather than a flat line.</summary>
    public static PedalTraceState PedalTrace()
    {
        const int samples = 160;
        var throttle = new double[samples];
        var brake = new double[samples];
        var clutch = new double[samples];
        var abs = new bool[samples];

        for (var i = 0; i < samples; i++)
        {
            var t = i / (double)(samples - 1);
            // Two quick clutch dabs: the downshifts under braking.
            clutch[i] = Math.Max(0, 1 - (Math.Min(Math.Abs(t - 0.47), Math.Abs(t - 0.55)) / 0.018));
            if (t < 0.34)
            {
                throttle[i] = 1.0;
            }
            else if (t < 0.42)
            {
                throttle[i] = Math.Max(0, 1 - ((t - 0.34) / 0.08));
                brake[i] = Math.Min(1, (t - 0.34) / 0.05);
            }
            else if (t < 0.62)
            {
                brake[i] = 0.96 - ((t - 0.42) * 1.4);
                abs[i] = t is > 0.44 and < 0.52;
            }
            else
            {
                throttle[i] = Math.Min(1, (t - 0.62) / 0.22);
            }
        }

        return new PedalTraceState
        {
            Throttle = throttle[^1],
            Brake = brake[^1],
            Clutch = 0,
            ThrottleHistory = throttle,
            BrakeHistory = brake,
            ClutchHistory = clutch,
            AbsHistory = abs,
            Positions = Enumerable.Range(0, samples).Select(i => i / (double)(samples - 1)).ToArray(),
        };
    }

    /// <summary>Front tires hotter and more worn than the rears, and the left front worst of all —
    /// an ordinary stint's wear pattern, so every wear bar lands on a different colour band.</summary>
    public static TireInfoState Tires() => new()
    {
        LF = Corner("LF", 172, 82, 96, 91, 0.41, 0.36, 0.33),
        RF = Corner("RF", 175, 79, 92, 88, 0.52, 0.47, 0.44),
        LR = Corner("LR", 168, 74, 84, 80, 0.71, 0.68, 0.66),
        RR = Corner("RR", 170, 72, 82, 79, 0.76, 0.74, 0.72),
    };

    private static TireCornerInfo Corner(
        string label, double pressure, double left, double middle, double right,
        double wearLeft, double wearMiddle, double wearRight) => new()
        {
            Label = label,
            UnitSystem = Units.Current,
            PressureKPa = pressure,
            ColdPressureKPa = pressure - 14,
            TempLeft = left,
            TempMiddle = middle,
            TempRight = right,
            IsSurfaceTemp = true,
            WearLeft = wearLeft,
            WearMiddle = wearMiddle,
            WearRight = wearRight,
            HasWearData = true,
        };

    /// <summary>A warm, breezy afternoon with rain threatening: a crosswind from front-left and a
    /// medium rain chance, so the arrow and the yellow rain colour both show.</summary>
    public static WeatherState Weather() => new()
    {
        UnitSystem = Units.Current,
        AirTempC = 21.4,
        TrackTempC = 33.8,
        HumidityPct = 54,
        WindSpeedMs = 3.6,
        WindFromRelativeDeg = 315,
        HeadingDeg = 60,
        Condition = WeatherCondition.PartlyCloudy,
        RainChancePct = 40,
        TrackWetness = 2,
    };

    public static TrackInfoState TrackInfo() => new()
    {
        UnitSystem = Units.Current,
        Condition = WeatherCondition.PartlyCloudy,
        TrackName = "Spa-Francorchamps",
        SessionLabel = "Race",
        TrackUsage = "moderately high usage",
        AirTempC = 21.4,
        TrackTempC = 33.8,
        WindSpeedMs = 3.6,
        WindDirRad = 2.1,
        HumidityPct = 54,
        TimeRemainingSeconds = 1284,
        Progress = new SessionProgress { CurrentLap = 7, TotalLaps = 23 },
    };

    /// <summary>Cars spread around the lap with the pack bunched where it usually is — behind the
    /// leader — in a multiclass field, so the map shows its class colours.</summary>
    public static IReadOnlyList<TrackMapMarker> TrackMap()
    {
        var field = StandingsField(multiClass: true);
        var markers = new List<TrackMapMarker>(field.Count);
        for (var i = 0; i < field.Count; i++)
        {
            var row = field[i];
            markers.Add(new TrackMapMarker
            {
                CarIdx = row.CarIdx,
                CarNumber = row.CarNumber,
                LapDistPct = (0.94 - (i * 0.037) + 1) % 1,
                IsPlayer = row.IsPlayer,
                OnPitRoad = row.OnPitRoad,
                ClassColor = row.ClassColor,
            });
        }

        return markers;
    }

    /// <summary>Strength of field for the header chip — the average of the mock field's iRatings,
    /// so the number the preview shows is consistent with the rows underneath it.</summary>
    public static double StrengthOfField() => Field.Average(e => e.IRating);

    public static int SubSessionId => 68412907;

    public static string ClassName => "GT3";
}
