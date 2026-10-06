namespace IRacingOverlay.Sdk;

/// <summary>
/// Strongly-typed subset of iRacing's session-info YAML blob. Only the fields the v1 overlays
/// (Relative/Standings/dashboard) need are mapped; iRacing's YAML has many more sections.
/// </summary>
public sealed class IracingSessionInfo
{
    public WeekendInfoSection? WeekendInfo { get; set; }
    public DriverInfoSection? DriverInfo { get; set; }
    public SessionInfoSection? SessionInfo { get; set; }
    public QualifyResultsInfoSection? QualifyResultsInfo { get; set; }

    /// <summary>Parses with the same repair steps as the live connection; see <see cref="SessionInfoParser"/>.</summary>
    public static IracingSessionInfo Parse(string yaml) => SessionInfoParser.Parse(yaml).Session;
}

public sealed class WeekendInfoSection
{
    public string? TrackName { get; set; }
    public string? TrackDisplayName { get; set; }
    public string? TrackDisplayShortName { get; set; }
    public string? TrackLength { get; set; }

    /// <summary>The event's chance of rain, e.g. "15 %". Absent on builds without dynamic rain.</summary>
    public string? TrackPrecipitation { get; set; }

    /// <summary>1 in a team race, 0 otherwise.</summary>
    public int TeamRacing { get; set; }

    public WeekendOptionsSection? WeekendOptions { get; set; }

    /// <summary>Identifies the specific room the driver is in. iRacing's telemetry YAML carries no
    /// split *index* ("split 2 of 7" only exists in the web API), so this id is the closest thing
    /// the SDK offers to "which of the splits am I in".</summary>
    public int SubSessionID { get; set; }
}

public sealed class WeekendOptionsSection
{
    /// <summary>Incident points before disqualification, e.g. "17", or "unlimited".</summary>
    public string? IncidentLimit { get; set; }
}

public sealed class DriverInfoSection
{
    public int DriverCarIdx { get; set; }
    public double DriverCarSLFirstRPM { get; set; }
    public double DriverCarSLShiftRPM { get; set; }
    public double DriverCarSLLastRPM { get; set; }
    public double DriverCarSLBlinkRPM { get; set; }
    public double DriverCarRedLine { get; set; }
    /// <summary>Physical fuel tank capacity, in liters.</summary>
    public double DriverCarFuelMaxLtr { get; set; }
    /// <summary>Fraction of the tank the series allows to be filled — some series run a fuel
    /// restriction, so this is not always 1.0 and the two have to be multiplied to get the real
    /// usable capacity.</summary>
    public double DriverCarMaxFuelPct { get; set; }

    /// <summary>The compounds the player's car can run, indexed the way CarIdxTireCompound reports
    /// them. Only the player's car is described: another model may number its tyres differently.</summary>
    public List<DriverTireEntry> DriverTires { get; set; } = [];

    public List<DriverEntry> Drivers { get; set; } = [];
}

public sealed class DriverTireEntry
{
    public int TireIndex { get; set; }

    /// <summary>e.g. "Hard", "Soft", "Wet".</summary>
    public string TireCompoundType { get; set; } = "";
}

public sealed class DriverEntry
{
    public int CarIdx { get; set; }
    public string UserName { get; set; } = "";
    public string TeamName { get; set; } = "";
    public string CarNumber { get; set; } = "";
    public int CarNumberRaw { get; set; }
    public int CarClassID { get; set; }
    public string CarClassShortName { get; set; } = "";
    /// <summary>Seconds. Per car, not per class (BoP'd models of one class differ); CarIdxEstTime runs on this clock.</summary>
    public double CarClassEstLapTime { get; set; }
    public int CarID { get; set; }
    public string CarScreenNameShort { get; set; } = "";
    /// <summary>e.g. "Porsche 911 GT3 R (992)": the full model name, which starts with the make.</summary>
    public string CarScreenName { get; set; } = "";
    public int CarIsPaceCar { get; set; }
    public int CarIsAI { get; set; }
    public int IRating { get; set; }
    public string LicString { get; set; } = "";
    public string CarClassColor { get; set; } = "";
    /// <summary>The flag the member picked on their iRacing profile, e.g. "Spain", "England",
    /// "Global"; "-none-" when they picked none. The closest thing iRacing has to a nationality.</summary>
    public string FlairName { get; set; } = "";
    public int FlairID { get; set; }

    public bool IsPaceCar => CarIsPaceCar != 0;
    public bool IsAi => CarIsAI != 0;
}

public sealed class SessionInfoSection
{
    /// <summary>Never populated by iRacing: its own SDK docs state SessionInfo has a single child
    /// parameter, Sessions. Which session is running comes from the <c>SessionNum</c> telemetry
    /// variable instead. Kept only so callers have something to fall back to when no telemetry is
    /// available.</summary>
    public int CurrentSessionNum { get; set; }
    public List<SessionEntry> Sessions { get; set; } = [];
}

public sealed class SessionEntry
{
    public int SessionNum { get; set; }
    public string SessionType { get; set; } = "";
    public string SessionName { get; set; } = "";
    public string SessionLaps { get; set; } = "";
    public string SessionTime { get; set; } = "";
    public string SessionTrackRubberState { get; set; } = "";

    /// <summary>The server's own scoring table for this session. Unlike the CarIdx* telemetry
    /// arrays, which only carry what this client has observed since it connected, this is the
    /// authoritative history and is already complete the moment the overlay attaches.</summary>
    public List<SessionResultPosition> ResultsPositions { get; set; } = [];
}

/// <summary>One car's line in a session's scoring table.</summary>
public sealed class SessionResultPosition
{
    public int CarIdx { get; set; }
    public int Position { get; set; }
    public int ClassPosition { get; set; }
    public int Lap { get; set; }
    public int LapsComplete { get; set; }

    /// <summary>Best lap time in seconds, or -1 when the car has never set one.</summary>
    public double FastestTime { get; set; }

    /// <summary>Last lap time in seconds, or -1 when the car has never completed one.</summary>
    public double LastTime { get; set; }
}

/// <summary>The race's starting grid. Present in a race even without a qualifying session (the
/// grid is then set some other way, e.g. by the AI roster); absent in practice-only and test
/// sessions.</summary>
public sealed class QualifyResultsInfoSection
{
    public List<QualifyResult> Results { get; set; } = [];
}

/// <summary>One car's slot on the starting grid. Unlike <see cref="SessionResultPosition"/>, both
/// positions here are 0-based.</summary>
public sealed class QualifyResult
{
    public int CarIdx { get; set; }
    public int Position { get; set; }
    public int ClassPosition { get; set; }
}
