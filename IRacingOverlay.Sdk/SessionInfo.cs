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
}

public sealed class DriverInfoSection
{
    public int DriverCarIdx { get; set; }
    public double DriverCarSLFirstRPM { get; set; }
    public double DriverCarSLShiftRPM { get; set; }
    public double DriverCarSLLastRPM { get; set; }
    public double DriverCarSLBlinkRPM { get; set; }
    public double DriverCarRedLine { get; set; }
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
}
