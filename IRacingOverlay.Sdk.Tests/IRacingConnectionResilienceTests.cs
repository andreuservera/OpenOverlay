using System.Collections.Concurrent;
using System.IO.MemoryMappedFiles;
using System.Text;
using IRacingOverlay.Sdk.Interop;

namespace IRacingOverlay.Sdk.Tests;

/// <summary>
/// Drives the real reader loop against a named shared-memory block and data event that stand in
/// for iRacing, covering the failure modes the reader has to survive and report: stalls, restarts,
/// corrupt session info, misbehaving subscribers and the sim going away.
/// </summary>
public sealed class IRacingConnectionResilienceTests
{
    private static readonly ConnectionTimings Fast = new(
        DisconnectedPoll: TimeSpan.FromMilliseconds(20),
        DataWait: TimeSpan.FromMilliseconds(10),
        PollWithoutEvent: TimeSpan.FromMilliseconds(5),
        StaleAfter: TimeSpan.FromMilliseconds(200),
        ReconnectAfterStale: TimeSpan.FromMilliseconds(600),
        InitialBackoff: TimeSpan.FromMilliseconds(20),
        MaxBackoff: TimeSpan.FromMilliseconds(100),
        SettleDelay: TimeSpan.FromMilliseconds(10),
        LayoutRecheck: TimeSpan.FromMilliseconds(100));

    private const string Yaml = """
        ---
        WeekendInfo:
         TrackName: spa
         SubSessionID: 12345
        DriverInfo:
         DriverCarIdx: 0
         Drivers:
         - CarIdx: 0
           UserName: Test Driver
           TeamName: [OO] Racing
        ...
        """;

    [Fact]
    public void Connects_PublishesTicksAndSessionInfo()
    {
        using var sim = new FakeSim(Yaml);
        using var connection = sim.Connect();

        WaitUntil(() => sim.TickAnd(() => connection.IsConnected && connection.Session is not null));
        sim.Tick();
        WaitUntil(() => connection.Latest?.GetFloat("Speed") == sim.LastSpeed);

        Assert.Equal(ConnectionState.Connected, connection.Health.State);
        Assert.Equal(12345, connection.Session!.WeekendInfo!.SubSessionID);
        Assert.Equal("[OO] Racing", connection.Session.DriverInfo!.Drivers[0].TeamName);
    }

    [Fact]
    public void MissingSim_StaysDisconnectedWithoutCountingFailures()
    {
        using var connection = new IRacingConnection($"Local\\OpenOverlayTest-missing-{Guid.NewGuid():N}", "none", Fast);
        connection.Start();

        Thread.Sleep(200);

        Assert.False(connection.IsConnected);
        Assert.Equal(ConnectionState.Disconnected, connection.Health.State);
        Assert.Equal(0, connection.Health.TotalFailures);
        Assert.Null(connection.LastError);
    }

    [Fact]
    public void SilentSim_GoesStale_ThenLetsGoWithoutRepublishingTheFrozenTick()
    {
        using var sim = new FakeSim(Yaml);
        using var connection = sim.Connect();
        var faults = Collect(connection);
        WaitUntil(() => sim.TickAnd(() => connection.IsConnected));

        WaitUntil(() => connection.Health.State == ConnectionState.Stale);
        Assert.NotNull(connection.Latest);

        WaitUntil(() => faults.Any(f => f.Stage == "watchdog"));
        WaitUntil(() => !connection.IsConnected);
        // Reopening finds the same frozen buffer; it must not come back as "connected".
        Thread.Sleep(300);
        Assert.False(connection.IsConnected);
        Assert.Null(connection.Latest);

        WaitUntil(() => sim.TickAnd(() => connection.IsConnected));
        Assert.Equal(1, connection.Health.Reconnects);
        Assert.Null(connection.LastError);
    }

    [Fact]
    public void StatusBitCleared_DisconnectsAndDropsSessionData_ThenReconnects()
    {
        using var sim = new FakeSim(Yaml);
        using var connection = sim.Connect();
        var disconnected = 0;
        connection.Disconnected += (_, _) => Interlocked.Increment(ref disconnected);
        WaitUntil(() => sim.TickAnd(() => connection.IsConnected && connection.Session is not null));

        sim.SetConnected(false);
        WaitUntil(() => !connection.IsConnected);

        Assert.Equal(1, Volatile.Read(ref disconnected));
        Assert.Null(connection.Latest);
        Assert.Null(connection.Session);

        sim.SetConnected(true);
        WaitUntil(() => sim.TickAnd(() => connection.IsConnected && connection.Session is not null));
    }

    [Fact]
    public void UnparseableSessionInfo_IsReportedButTelemetryKeepsFlowing()
    {
        using var sim = new FakeSim("WeekendInfo: [\nSessionInfo: {\nDriverInfo: ]\n");
        using var connection = sim.Connect();
        var faults = Collect(connection);

        WaitUntil(() => sim.TickAnd(() => connection.IsConnected && faults.Any(f => f.Stage == "session-info")));
        Assert.Null(connection.Session);
        Assert.True(connection.Health.SessionInfoFailures >= 1);

        sim.PublishSessionInfo(Yaml);
        WaitUntil(() => sim.TickAnd(() => connection.Session is not null));
        Assert.Equal(12345, connection.Session!.WeekendInfo!.SubSessionID);
    }

    [Fact]
    public void ThrowingSubscriber_IsContainedAndTicksContinue()
    {
        using var sim = new FakeSim(Yaml);
        using var connection = sim.Connect();
        var faults = Collect(connection);
        connection.TelemetryUpdated += (_, _) => throw new InvalidOperationException("subscriber bug");

        WaitUntil(() => sim.TickAnd(() => faults.Any(f => f.Stage == "subscriber:TelemetryUpdated")));
        var received = connection.Health.TicksReceived;
        WaitUntil(() => sim.TickAnd(() => connection.Health.TicksReceived >= received + 5));

        Assert.True(connection.IsConnected);
        Assert.Equal(0, connection.Health.TotalFailures);
    }

    [Fact]
    public void SimRestartedInPlace_ReReadsLayoutAndSessionInfo()
    {
        using var sim = new FakeSim(Yaml);
        using var connection = sim.Connect();
        for (var i = 0; i < 20; i++)
        {
            sim.Tick();
        }

        WaitUntil(() => sim.TickAnd(() => connection.Session?.WeekendInfo?.SubSessionID == 12345));

        // A new sim instance: variables in a different order, counters from the start, new event.
        sim.Reinitialize(lapFirst: true, Yaml.Replace("12345", "777"));
        WaitUntil(() => sim.TickAnd(() => connection.Session?.WeekendInfo?.SubSessionID == 777));
        sim.Tick();

        // Read through the new layout: a stale one would return Speed's bytes as the lap.
        WaitUntil(() => connection.Latest is { } latest
            && latest.GetInt("Lap") == sim.LastLap
            && latest.GetFloat("Speed") == sim.LastSpeed);
    }

    [Fact]
    public void Stop_IsPromptAndRepeatable()
    {
        using var sim = new FakeSim(Yaml);
        var connection = sim.Connect();
        WaitUntil(() => sim.TickAnd(() => connection.IsConnected));

        var started = Environment.TickCount64;
        connection.Stop();
        connection.Stop();
        connection.Dispose();

        Assert.True(Environment.TickCount64 - started < 2000);
    }

    private static ConcurrentQueue<ConnectionFault> Collect(IRacingConnection connection)
    {
        var faults = new ConcurrentQueue<ConnectionFault>();
        connection.Fault += (_, fault) => faults.Enqueue(fault);
        return faults;
    }

    private static void WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return;
            }

            Thread.Sleep(10);
        }

        Assert.Fail("Timed out waiting for the condition.");
    }

    /// <summary>A stand-in for iRacing's side of the shared-memory contract.</summary>
    private sealed class FakeSim : IDisposable
    {
        private const int Capacity = 64 * 1024;
        private const int SessionInfoRoom = 4096;

        private readonly string _mapName = $"Local\\OpenOverlayTest-{Guid.NewGuid():N}";
        private readonly string _eventName = $"Local\\OpenOverlayTestEvent-{Guid.NewGuid():N}";
        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _accessor;
        private readonly EventWaitHandle _event;
        private SyntheticMemoryBuilder _builder = null!;
        private int _bufLen;
        private int _tick;
        private int _sessionInfoUpdate = 1;

        public FakeSim(string yaml)
        {
            _mmf = MemoryMappedFile.CreateNew(_mapName, Capacity);
            _accessor = _mmf.CreateViewAccessor();
            _event = new EventWaitHandle(false, EventResetMode.AutoReset, _eventName);
            Reinitialize(lapFirst: false, yaml);
        }

        public float LastSpeed { get; private set; }

        public int LastLap => _tick;

        public IRacingConnection Connect()
        {
            var connection = new IRacingConnection(_mapName, _eventName, Fast);
            connection.Start();
            return connection;
        }

        /// <summary>Writes a whole new image, as a freshly started sim would: the tick counter restarts.</summary>
        public void Reinitialize(bool lapFirst, string yaml)
        {
            _builder = new SyntheticMemoryBuilder();
            if (lapFirst)
            {
                _builder.AddVar("Lap", IrsdkVarType.Int);
                _builder.AddVar("Speed", IrsdkVarType.Float);
            }
            else
            {
                _builder.AddVar("Speed", IrsdkVarType.Float);
                _builder.AddVar("Lap", IrsdkVarType.Int);
            }

            _bufLen = _builder.TotalSize;
            _tick = 0;
            var padded = yaml + new string('\0', SessionInfoRoom - Encoding.UTF8.GetByteCount(yaml));
            var image = _builder.BuildFullImage(60, true, padded, _bufLen, [(0, new byte[_bufLen])]);
            _accessor.WriteArray(0, new byte[Capacity], 0, Capacity);
            _accessor.WriteArray(0, image, 0, image.Length);
            _accessor.Write(IrsdkConstants.HeaderSessionInfoUpdateOffset, _sessionInfoUpdate);
        }

        public void Tick()
        {
            _tick++;
            LastSpeed = _tick * 1.5f;
            var data = _builder.BuildTickBuffer(_bufLen, w =>
            {
                w.SetFloat("Speed", LastSpeed);
                w.SetInt("Lap", _tick);
            });
            var bufOffset = _accessor.ReadInt32(IrsdkConstants.HeaderVarBufArrayOffset + 4);
            _accessor.WriteArray(bufOffset, data, 0, data.Length);
            _accessor.Write(IrsdkConstants.HeaderVarBufArrayOffset, _tick);
            _event.Set();
        }

        /// <summary>Ticks once, then evaluates the condition — so waits see a live sim.</summary>
        public bool TickAnd(Func<bool> condition)
        {
            Tick();
            return condition();
        }

        public void SetConnected(bool connected) =>
            _accessor.Write(IrsdkConstants.HeaderStatusOffset, connected ? IrsdkConstants.StatusConnected : 0);

        public void PublishSessionInfo(string yaml)
        {
            var offset = _accessor.ReadInt32(IrsdkConstants.HeaderSessionInfoOffsetOffset);
            var bytes = Encoding.UTF8.GetBytes(yaml + "\0");
            _accessor.WriteArray(offset, bytes, 0, bytes.Length);
            _accessor.Write(IrsdkConstants.HeaderSessionInfoUpdateOffset, ++_sessionInfoUpdate);
        }

        public void Dispose()
        {
            _event.Dispose();
            _accessor.Dispose();
            _mmf.Dispose();
        }
    }
}
