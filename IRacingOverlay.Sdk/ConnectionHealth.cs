namespace IRacingOverlay.Sdk;

public enum ConnectionState
{
    /// <summary>iRacing isn't running, or isn't in a session.</summary>
    Disconnected,

    /// <summary>In a session and ticks are arriving.</summary>
    Connected,

    /// <summary>In a session, but no new tick for a while: the sim is loading, stalled or has
    /// crashed. The last snapshot is kept until the reader gives up and reconnects.</summary>
    Stale,

    /// <summary>The reader hit an unexpected error and is retrying with backoff.</summary>
    Recovering,
}

/// <summary>Point-in-time view of the telemetry reader, for health monitoring and diagnostics.</summary>
public sealed record ConnectionHealth
{
    public ConnectionState State { get; init; }
    public DateTime? ConnectedSinceUtc { get; init; }
    public DateTime? LastTickUtc { get; init; }
    public int LastTickCount { get; init; }
    public long TicksReceived { get; init; }

    /// <summary>Times a live session was dropped and picked up again (sim restarts, stalls, errors).</summary>
    public int Reconnects { get; init; }

    public int ConsecutiveFailures { get; init; }
    public long TotalFailures { get; init; }
    public int SessionInfoUpdates { get; init; }
    public int SessionInfoFailures { get; init; }

    /// <summary>The latest session info needed repairing, or kept sections from an older update.</summary>
    public bool SessionInfoDegraded { get; init; }

    /// <summary>Times the reader loop itself had to be restarted by its supervisor.</summary>
    public int LoopRestarts { get; init; }

    public string? LastError { get; init; }
    public DateTime? LastErrorUtc { get; init; }
}

/// <summary>A failure inside the telemetry reader. The reader has already recovered or scheduled a
/// retry by the time this is raised; it is reported for logging only.</summary>
/// <param name="Stage">Where it happened: "read", "session-info", "watchdog", "subscriber", "supervisor".</param>
/// <param name="RetryIn">Delay before the next attempt, when the reader is backing off.</param>
public sealed record ConnectionFault(string Stage, Exception Exception, int ConsecutiveFailures, TimeSpan? RetryIn);
