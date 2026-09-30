using System.IO.MemoryMappedFiles;
using System.Threading;
using IRacingOverlay.Sdk.Interop;

namespace IRacingOverlay.Sdk;

/// <summary>
/// Owns the connection to iRacing's shared-memory block: detects the sim starting/stopping,
/// waits on iRacing's data-ready event for each tick, and publishes typed telemetry + session info.
/// One instance should be shared app-wide (e.g. one per App.xaml.cs) rather than opened per widget.
///
/// No failure ends the reader, and every one is reported through <see cref="Fault"/>. Errors are retried
/// with exponential backoff under a supervisor, a sim that stops ticking is detected and the mapping
/// reopened, the variable layout is re-read whenever it can have changed, and session info that
/// fails to parse never stops telemetry. Subscribers' exceptions are contained rather than tearing
/// the connection down.
/// </summary>
public sealed class IRacingConnection : IDisposable
{
    private const int MaxBufferReadRetries = 5;

    private readonly string _mapName;
    private readonly string _eventName;
    private readonly ConnectionTimings _timings;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    // Written by the reader thread only; read from any thread.
    private volatile TelemetrySnapshot? _latest;
    private volatile IracingSessionInfo? _session;
    private volatile bool _isConnected;
    private volatile ConnectionState _state;
    private volatile string? _lastError;
    private volatile string? _lastErrorDetail;
    private volatile bool _sessionInfoDegraded;
    private long _connectedSinceTicks;
    private long _lastTickUtcTicks;
    private long _lastErrorUtcTicks;
    private long _ticksReceived;
    private long _totalFailures;
    private int _lastTickCount;
    private int _reconnects;
    private int _consecutiveFailures;
    private int _sessionInfoUpdates;
    private int _sessionInfoFailures;
    private int _loopRestarts;

    // Reader thread only.
    private int _lastSessionInfoUpdate = -1;
    private int _failedSessionInfoUpdate = -1;
    private bool _wasEverConnected;

    public IRacingConnection()
        : this(IrsdkConstants.MemoryMappedFileName, IrsdkConstants.DataValidEventName, ConnectionTimings.Default)
    {
    }

    /// <summary>Test seam: a private mapping name and short timings.</summary>
    internal IRacingConnection(string mapName, string eventName, ConnectionTimings timings)
    {
        _mapName = mapName;
        _eventName = eventName;
        _timings = timings;
    }

    public bool IsConnected => _isConnected;

    /// <summary>Connected, but no new tick for a while; <see cref="Latest"/> is the last one received.</summary>
    public bool IsStale => _state == ConnectionState.Stale;

    /// <summary>The current unexpected failure reading telemetry; cleared once a tick reads cleanly
    /// or iRacing is found to be closed.</summary>
    public string? LastError => _lastError;

    public TelemetrySnapshot? Latest => _latest;
    public IracingSessionInfo? Session => _session;

    public ConnectionHealth Health => new()
    {
        State = _state,
        ConnectedSinceUtc = ToUtc(Interlocked.Read(ref _connectedSinceTicks)),
        LastTickUtc = ToUtc(Interlocked.Read(ref _lastTickUtcTicks)),
        LastTickCount = Volatile.Read(ref _lastTickCount),
        TicksReceived = Interlocked.Read(ref _ticksReceived),
        Reconnects = Volatile.Read(ref _reconnects),
        ConsecutiveFailures = Volatile.Read(ref _consecutiveFailures),
        TotalFailures = Interlocked.Read(ref _totalFailures),
        SessionInfoUpdates = Volatile.Read(ref _sessionInfoUpdates),
        SessionInfoFailures = Volatile.Read(ref _sessionInfoFailures),
        SessionInfoDegraded = _sessionInfoDegraded,
        LoopRestarts = Volatile.Read(ref _loopRestarts),
        LastError = _lastErrorDetail,
        LastErrorUtc = ToUtc(Interlocked.Read(ref _lastErrorUtcTicks)),
    };

    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<TelemetrySnapshot>? TelemetryUpdated;
    public event EventHandler<IracingSessionInfo>? SessionInfoUpdated;

    /// <summary>Raised on the reader thread for every contained failure. For logging only: the reader
    /// has already recovered or scheduled a retry.</summary>
    public event EventHandler<ConnectionFault>? Fault;

    public void Start()
    {
        lock (_gate)
        {
            if (_loopTask is not null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            // A dedicated thread: the reader blocks on waits for the whole session.
            _loopTask = Task.Factory.StartNew(
                () => Supervise(token), token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    public void Stop()
    {
        Task? task;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            task = _loopTask;
            cts = _cts;
            _loopTask = null;
            _cts = null;
        }

        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        var finished = true;
        try
        {
            finished = task?.Wait(TimeSpan.FromSeconds(2)) ?? true;
        }
        catch (AggregateException)
        {
            // Supervise never faults; Stop must not throw during shutdown regardless.
        }

        // A reader that hasn't finished still waits on this token; disposing it would throw there.
        if (finished)
        {
            cts.Dispose();
        }
    }

    public void Dispose() => Stop();

    private void Supervise(CancellationToken token)
    {
        try
        {
            Thread.CurrentThread.Name ??= "iRacing telemetry reader";
        }
        catch (InvalidOperationException)
        {
            // Naming is cosmetic.
        }

        while (!token.IsCancellationRequested)
        {
            try
            {
                RunLoop(token);
            }
            catch (Exception e) when (!token.IsCancellationRequested)
            {
                // RunLoop contains its own failures; this only runs if that containment failed too.
                Interlocked.Increment(ref _loopRestarts);
                RecordFailure("supervisor", e, _timings.MaxBackoff);
                Sleep(token, _timings.MaxBackoff);
            }
            catch (Exception)
            {
                return;
            }
        }
    }

    private void RunLoop(CancellationToken token)
    {
        var backoff = new Backoff(_timings.InitialBackoff, _timings.MaxBackoff);
        while (!token.IsCancellationRequested)
        {
            var delay = TimeSpan.Zero;
            MemoryMappedFile? mmf = null;
            EventWaitHandle? dataEvent = null;
            try
            {
                mmf = TryOpenMemoryMappedFile();
                if (mmf is null)
                {
                    // iRacing closed is the normal idle state, not an error streak.
                    _lastError = null;
                    Volatile.Write(ref _consecutiveFailures, 0);
                    _state = ConnectionState.Disconnected;
                    backoff.Reset();
                    delay = _timings.DisconnectedPoll;
                }
                else
                {
                    dataEvent = TryOpenDataEvent();
                    using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                    RunConnectedLoop(accessor, dataEvent, backoff, token);
                }
            }
            catch (Exception e) when (!token.IsCancellationRequested)
            {
                // Includes the IOException of a sim closing mid-read: the first retry comes after
                // about a second and finds it gone (or back), which clears the error again.
                delay = backoff.Next();
                RecordFailure("read", e, delay);
            }
            finally
            {
                dataEvent?.Dispose();
                mmf?.Dispose();
                SetDisconnected();
            }

            Sleep(token, delay);
        }
    }

    /// <summary>Returns normally when the mapping should be reopened, throws on unexpected failure.</summary>
    private void RunConnectedLoop(
        MemoryMappedViewAccessor accessor, EventWaitHandle? dataEvent, Backoff backoff, CancellationToken token)
    {
        var capacity = accessor.Capacity;
        var headerBytes = new byte[IrsdkConstants.HeaderTotalSize];
        WaitHandle[]? waitHandles = dataEvent is null ? null : [dataEvent, token.WaitHandle];
        VarLayout? layout = null;
        // A killed sim can't clear its status bit, and the block outlives it while any other process
        // holds it open. Only a tick that follows the first one seen proves the sim is alive.
        var lastTickCount = int.MinValue;
        var published = false;
        var lastProgressMs = Environment.TickCount64;

        while (!token.IsCancellationRequested)
        {
            accessor.ReadArray(0, headerBytes, 0, headerBytes.Length);
            var header = IrsdkParser.ParseHeader(headerBytes);
            var nowMs = Environment.TickCount64;

            if (!header.IsConnected)
            {
                // Left the session, but the mapping outlives it while we hold it open: whatever
                // joins next may publish a different variable layout and restart its counters.
                layout = null;
                lastTickCount = int.MinValue;
                published = false;
                lastProgressMs = nowMs;
                SetDisconnected();
                Sleep(token, _timings.DisconnectedPoll);
                continue;
            }

            if (!IrsdkParser.IsPlausible(header, capacity))
            {
                // The sim is still laying the block out. Give it time, but not forever.
                layout = null;
                if (nowMs - lastProgressMs >= _timings.ReconnectAfterStale.TotalMilliseconds)
                {
                    throw new InvalidDataException(
                        $"Shared-memory header still inconsistent after {_timings.ReconnectAfterStale.TotalSeconds:0} s.");
                }

                Sleep(token, _timings.SettleDelay);
                continue;
            }

            if (layout is null || !layout.Matches(header)
                || (nowMs - layout.CheckedAtMs >= _timings.LayoutRecheck.TotalMilliseconds && !layout.IsCurrent(accessor, nowMs)))
            {
                layout = VarLayout.Read(accessor, header, nowMs);
            }

            var snapshot = ReadLatestTickWithRetry(accessor, header, layout.VarsByName);
            if (snapshot is not null && lastTickCount == int.MinValue)
            {
                lastTickCount = snapshot.TickCount;
            }
            else if (snapshot is not null && snapshot.TickCount != lastTickCount)
            {
                if (snapshot.TickCount < lastTickCount)
                {
                    // The counter restarted: a new sim instance re-initialised the mapping we still
                    // hold. Re-read everything before trusting a single value from it.
                    layout = null;
                    _session = null;
                    _lastSessionInfoUpdate = -1;
                    _failedSessionInfoUpdate = -1;
                    lastTickCount = int.MinValue;
                    continue;
                }

                lastTickCount = snapshot.TickCount;
                lastProgressMs = nowMs;
                published = true;
                backoff.Reset();
                ReadSessionInfoIfChanged(accessor, header);
                PublishTick(snapshot);
            }
            else if (nowMs - lastProgressMs >= _timings.ReconnectAfterStale.TotalMilliseconds)
            {
                if (!published)
                {
                    // Nothing live ever came through: a dead sim's leftover block, i.e. iRacing is closed.
                    _lastError = null;
                    Volatile.Write(ref _consecutiveFailures, 0);
                    _state = ConnectionState.Disconnected;
                    return;
                }

                // Still flagged connected but silent: most likely the sim crashed, which leaves the
                // status bit set. Letting go of the mapping lets a restarted sim start clean.
                RecordFailure(
                    "watchdog",
                    new TimeoutException($"No new telemetry for {_timings.ReconnectAfterStale.TotalSeconds:0} s; reopening shared memory."),
                    retryIn: null);
                return;
            }
            else if (nowMs - lastProgressMs >= _timings.StaleAfter.TotalMilliseconds && _state == ConnectionState.Connected)
            {
                _state = ConnectionState.Stale;
            }

            if (waitHandles is not null)
            {
                WaitHandle.WaitAny(waitHandles, _timings.DataWait);
            }
            else
            {
                Sleep(token, _timings.PollWithoutEvent);
            }
        }
    }

    private void ReadSessionInfoIfChanged(MemoryMappedViewAccessor accessor, IrsdkHeader header)
    {
        var update = header.SessionInfoUpdate;
        if (update == _lastSessionInfoUpdate || update == _failedSessionInfoUpdate)
        {
            return;
        }

        try
        {
            var yamlBytes = new byte[header.SessionInfoLen];
            accessor.ReadArray(header.SessionInfoOffset, yamlBytes, 0, yamlBytes.Length);

            // iRacing started rewriting the block while it was being copied: take it next time round.
            if (accessor.ReadInt32(IrsdkConstants.HeaderSessionInfoUpdateOffset) != update)
            {
                return;
            }

            var yaml = IrsdkParser.ReadSessionInfoYaml(yamlBytes, 0, yamlBytes.Length);
            var result = SessionInfoParser.Parse(yaml, _session);
            _session = result.Session;
            _lastSessionInfoUpdate = update;
            _failedSessionInfoUpdate = -1;
            _sessionInfoDegraded = result.IsDegraded;
            Interlocked.Increment(ref _sessionInfoUpdates);

            if (result.Error is { } repaired)
            {
                var stage = result.FailedSections.Count > 0
                    ? $"session-info (kept previous {string.Join(", ", result.FailedSections)})"
                    : "session-info (repaired)";
                RaiseFault(new ConnectionFault(stage, repaired, 0, null));
            }

            Raise(SessionInfoUpdated, result.Session, nameof(SessionInfoUpdated));
        }
        catch (Exception e)
        {
            // Keep the last good session info and try again when iRacing publishes the next update.
            _failedSessionInfoUpdate = update;
            _sessionInfoDegraded = true;
            Interlocked.Increment(ref _sessionInfoFailures);
            RaiseFault(new ConnectionFault("session-info", e, 0, null));
        }
    }

    internal static TelemetrySnapshot? ReadLatestTickWithRetry(
        MemoryMappedViewAccessor accessor, IrsdkHeader header, Dictionary<string, IrsdkVarHeader> varsByName)
    {
        var headerBytes = new byte[IrsdkConstants.HeaderTotalSize];
        var tickCountAfterBytes = new byte[4];
        for (var attempt = 0; attempt < MaxBufferReadRetries; attempt++)
        {
            var latestIndex = 0;
            for (var i = 1; i < Math.Clamp(header.NumBuf, 1, IrsdkConstants.MaxBufs); i++)
            {
                if (header.VarBufs[i].TickCount > header.VarBufs[latestIndex].TickCount)
                {
                    latestIndex = i;
                }
            }

            var latestBuf = header.VarBufs[latestIndex];
            var tickCountBefore = latestBuf.TickCount;
            var data = new byte[header.BufLen];
            accessor.ReadArray(latestBuf.BufOffset, data, 0, data.Length);

            accessor.ReadArray(
                IrsdkConstants.HeaderVarBufArrayOffset + latestIndex * IrsdkConstants.VarBufEntrySize,
                tickCountAfterBytes, 0, 4);
            var tickCountAfter = BitConverter.ToInt32(tickCountAfterBytes);

            if (tickCountAfter == tickCountBefore && tickCountBefore != 0)
            {
                return new TelemetrySnapshot(data, varsByName, tickCountBefore);
            }

            // Buffer was overwritten mid-read (or header just read is now stale) — re-read the header and retry.
            accessor.ReadArray(0, headerBytes, 0, headerBytes.Length);
            header = IrsdkParser.ParseHeader(headerBytes);
            if (!IrsdkParser.IsPlausible(header, accessor.Capacity))
            {
                return null;
            }
        }

        return null;
    }

    private void PublishTick(TelemetrySnapshot snapshot)
    {
        _latest = snapshot;
        Volatile.Write(ref _lastTickCount, snapshot.TickCount);
        Interlocked.Exchange(ref _lastTickUtcTicks, DateTime.UtcNow.Ticks);
        Interlocked.Increment(ref _ticksReceived);
        Volatile.Write(ref _consecutiveFailures, 0);
        SetConnected();
        Raise(TelemetryUpdated, snapshot, nameof(TelemetryUpdated));
    }

    private void SetConnected()
    {
        _lastError = null;
        _state = ConnectionState.Connected;
        if (_isConnected)
        {
            return;
        }

        _isConnected = true;
        Interlocked.Exchange(ref _connectedSinceTicks, DateTime.UtcNow.Ticks);
        if (_wasEverConnected)
        {
            Interlocked.Increment(ref _reconnects);
        }

        _wasEverConnected = true;
        Raise(Connected, nameof(Connected));
    }

    private void SetDisconnected()
    {
        // Nothing read before the sim went away describes the next session, so none of it is kept.
        _latest = null;
        _session = null;
        _lastSessionInfoUpdate = -1;
        _failedSessionInfoUpdate = -1;
        _sessionInfoDegraded = false;
        if (_state != ConnectionState.Recovering)
        {
            _state = ConnectionState.Disconnected;
        }

        if (!_isConnected)
        {
            return;
        }

        _isConnected = false;
        Interlocked.Exchange(ref _connectedSinceTicks, 0);
        Raise(Disconnected, nameof(Disconnected));
    }

    private void RecordFailure(string stage, Exception exception, TimeSpan? retryIn)
    {
        var failures = Interlocked.Increment(ref _consecutiveFailures);
        Interlocked.Increment(ref _totalFailures);
        _lastError = exception.Message;
        _lastErrorDetail = $"{stage}: {exception.GetType().Name}: {exception.Message}";
        Interlocked.Exchange(ref _lastErrorUtcTicks, DateTime.UtcNow.Ticks);
        _state = ConnectionState.Recovering;
        RaiseFault(new ConnectionFault(stage, exception, failures, retryIn));
    }

    private void Raise(EventHandler? handler, string name)
    {
        if (handler is null)
        {
            return;
        }

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler)subscriber)(this, EventArgs.Empty);
            }
            catch (Exception e)
            {
                RaiseFault(new ConnectionFault($"subscriber:{name}", e, 0, null));
            }
        }
    }

    private void Raise<T>(EventHandler<T>? handler, T args, string name)
    {
        if (handler is null)
        {
            return;
        }

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<T>)subscriber)(this, args);
            }
            catch (Exception e)
            {
                RaiseFault(new ConnectionFault($"subscriber:{name}", e, 0, null));
            }
        }
    }

    private void RaiseFault(ConnectionFault fault)
    {
        if (Fault is not { } handler)
        {
            return;
        }

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<ConnectionFault>)subscriber)(this, fault);
            }
            catch (Exception)
            {
                // A failing fault reporter has nowhere left to report to.
            }
        }
    }

    private static void Sleep(CancellationToken token, TimeSpan delay)
    {
        if (delay > TimeSpan.Zero)
        {
            token.WaitHandle.WaitOne(delay);
        }
    }

    private static DateTime? ToUtc(long ticks) => ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);

    private MemoryMappedFile? TryOpenMemoryMappedFile()
    {
        try
        {
            return MemoryMappedFile.OpenExisting(_mapName, MemoryMappedFileRights.Read);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private EventWaitHandle? TryOpenDataEvent()
    {
        try
        {
            return EventWaitHandle.OpenExisting(_eventName);
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return null;
        }
    }

    /// <summary>The variable table as last read, plus what it was read against so a change is seen.</summary>
    private sealed class VarLayout
    {
        private readonly int _version;
        private readonly int _numVars;
        private readonly int _varHeaderOffset;
        private readonly int _bufLen;
        private readonly byte[] _raw;

        private VarLayout(IrsdkHeader header, byte[] raw, long checkedAtMs)
        {
            _version = header.Version;
            _numVars = header.NumVars;
            _varHeaderOffset = header.VarHeaderOffset;
            _bufLen = header.BufLen;
            _raw = raw;
            CheckedAtMs = checkedAtMs;
            VarsByName = IrsdkParser.BuildVarMap(IrsdkParser.ParseVarHeaders(raw, 0, header.NumVars), header.BufLen);
        }

        public Dictionary<string, IrsdkVarHeader> VarsByName { get; }

        public long CheckedAtMs { get; private set; }

        public static VarLayout Read(MemoryMappedViewAccessor accessor, IrsdkHeader header, long nowMs)
        {
            var raw = new byte[header.NumVars * IrsdkConstants.VarHeaderEntrySize];
            accessor.ReadArray(header.VarHeaderOffset, raw, 0, raw.Length);
            return new VarLayout(header, raw, nowMs);
        }

        public bool Matches(IrsdkHeader header) =>
            header.Version == _version && header.NumVars == _numVars
            && header.VarHeaderOffset == _varHeaderOffset && header.BufLen == _bufLen;

        /// <summary>Byte-compares the live table with the cached one; catches a layout change that
        /// kept the same size (a sim restart that was never seen disconnecting).</summary>
        public bool IsCurrent(MemoryMappedViewAccessor accessor, long nowMs)
        {
            var live = new byte[_raw.Length];
            accessor.ReadArray(_varHeaderOffset, live, 0, live.Length);
            CheckedAtMs = nowMs;
            return live.AsSpan().SequenceEqual(_raw);
        }
    }

    private sealed class Backoff(TimeSpan initial, TimeSpan max)
    {
        private int _attempt;

        public void Reset() => _attempt = 0;

        /// <summary>Doubling delay with ±20 % jitter, capped at the maximum.</summary>
        public TimeSpan Next()
        {
            var exponential = initial.TotalMilliseconds * Math.Pow(2, Math.Min(_attempt++, 16));
            var jittered = Math.Min(max.TotalMilliseconds, exponential) * (0.8 + (Random.Shared.NextDouble() * 0.4));
            return TimeSpan.FromMilliseconds(Math.Min(max.TotalMilliseconds, jittered));
        }
    }
}

/// <summary>Every interval the reader uses, in one place so tests can shrink them.</summary>
internal sealed record ConnectionTimings(
    TimeSpan DisconnectedPoll,
    TimeSpan DataWait,
    TimeSpan PollWithoutEvent,
    TimeSpan StaleAfter,
    TimeSpan ReconnectAfterStale,
    TimeSpan InitialBackoff,
    TimeSpan MaxBackoff,
    TimeSpan SettleDelay,
    TimeSpan LayoutRecheck)
{
    /// <summary>The 30 s reconnect matches the official irsdk client's connection timeout.</summary>
    public static ConnectionTimings Default { get; } = new(
        DisconnectedPoll: TimeSpan.FromSeconds(1),
        DataWait: TimeSpan.FromMilliseconds(250),
        PollWithoutEvent: TimeSpan.FromMilliseconds(16),
        StaleAfter: TimeSpan.FromSeconds(5),
        ReconnectAfterStale: TimeSpan.FromSeconds(30),
        InitialBackoff: TimeSpan.FromSeconds(1),
        MaxBackoff: TimeSpan.FromSeconds(30),
        SettleDelay: TimeSpan.FromMilliseconds(100),
        LayoutRecheck: TimeSpan.FromSeconds(5));
}
