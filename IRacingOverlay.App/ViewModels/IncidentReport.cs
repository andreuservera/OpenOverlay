using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>What iRacing logged an incident as — irsdk_IncidentFlags' report byte.</summary>
public enum IncidentKind
{
    Unknown,
    LossOfControl,
    OffTrack,
    /// <summary>Contact with the world: walls, barriers, cones.</summary>
    WallContact,
    CarContact,
}

/// <summary>One incident as the sim itself reported it, numbered so a repeat can be told from a new one.</summary>
public sealed record IncidentReport(IncidentKind Kind, int Points, long Sequence)
{
    public string Label => Kind switch
    {
        IncidentKind.LossOfControl => "LOSS OF CONTROL",
        IncidentKind.OffTrack => "OFF TRACK",
        IncidentKind.WallContact => "WALL CONTACT",
        IncidentKind.CarContact => "CAR CONTACT",
        _ => "INCIDENT",
    };

    /// <summary>
    /// irsdk_IncidentFlags: the low byte says what happened, the next one what it cost.
    ///   report 1 loss of control, 2/3 off track, 4/5/6 contact with the world, 7/8 contact with a car;
    ///   penalty 1 = 0x, 2 = 1x, 3 = 2x, 4 = 4x.
    /// Null for "nothing logged" or a report byte this build doesn't know.
    /// </summary>
    public static IncidentReport? Decode(uint raw, long sequence)
    {
        var kind = (raw & 0xFF) switch
        {
            1 => IncidentKind.LossOfControl,
            2 or 3 => IncidentKind.OffTrack,
            4 or 5 or 6 => IncidentKind.WallContact,
            7 or 8 => IncidentKind.CarContact,
            _ => IncidentKind.Unknown,
        };
        int? points = ((raw >> 8) & 0xFF) switch
        {
            1 => 0,
            2 => 1,
            3 => 2,
            4 => 4,
            _ => null,
        };

        return kind == IncidentKind.Unknown || points is not { } value ? null : new IncidentReport(kind, value, sequence);
    }
}

/// <summary>
/// Catches iRacing's PlayerIncidents log on every sim tick. It may only be set for the tick the
/// incident is assessed on, which the 10 Hz UI loop would usually miss, so this runs on the
/// telemetry reader's thread and keeps the latest report until the next one.
/// </summary>
internal sealed class IncidentReportLatch
{
    private uint _lastRaw;
    private long _sequence;
    private IncidentReport? _latest;

    public IncidentReport? Latest => Volatile.Read(ref _latest);

    /// <summary>Called for every tick, on the telemetry thread. Never throws.</summary>
    public void Observe(TelemetrySnapshot telemetry)
    {
        if (!telemetry.HasVariable(TelemetryVarNames.PlayerIncidents))
        {
            return;
        }

        uint raw;
        try
        {
            raw = telemetry.GetBitField(TelemetryVarNames.PlayerIncidents);
        }
        catch (InvalidOperationException)
        {
            raw = unchecked((uint)telemetry.GetInt(TelemetryVarNames.PlayerIncidents));
        }

        // Only a change is a new incident: the value may stay up for longer than a tick.
        if (raw != 0 && raw != _lastRaw && IncidentReport.Decode(raw, Interlocked.Increment(ref _sequence)) is { } report)
        {
            Volatile.Write(ref _latest, report);
        }

        _lastRaw = raw;
    }

    /// <summary>Disconnected: nothing logged before belongs to the next session.</summary>
    public void Reset()
    {
        _lastRaw = 0;
        Volatile.Write(ref _latest, null);
    }
}
