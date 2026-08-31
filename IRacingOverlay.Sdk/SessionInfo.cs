using YamlDotNet.Serialization;

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

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    public static IracingSessionInfo Parse(string yaml) =>
        Deserializer.Deserialize<IracingSessionInfo>(yaml) ?? new IracingSessionInfo();
}

public sealed class WeekendInfoSection
{
    public string? TrackName { get; set; }
    public string? TrackDisplayName { get; set; }
    public string? TrackDisplayShortName { get; set; }
    public string? TrackLength { get; set; }

    /// <summary>Identifies the specific room the driver is in. iRacing's telemetry YAML carries no
    /// split *index* ("split 2 of 7" only exists in the web API), so this id is the closest thing
    /// the SDK offers to "which of the splits am I in".</summary>
    public int SubSessionID { get; set; }
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
    public List<DriverEntry> Drivers { get; set; } = [];
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
    public int CarID { get; set; }
    public string CarScreenNameShort { get; set; } = "";
    public int CarIsPaceCar { get; set; }
    public int CarIsAI { get; set; }
    public int IRating { get; set; }
    public string LicString { get; set; } = "";
    public string CarClassColor { get; set; } = "";

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
