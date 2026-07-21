using System.IO.MemoryMappedFiles;
using System.Threading;
using IRacingOverlay.Sdk.Interop;

namespace IRacingOverlay.Sdk;

/// <summary>
/// Owns the connection to iRacing's shared-memory block: detects the sim starting/stopping,
/// waits on iRacing's data-ready event for each tick, and publishes typed telemetry + session info.
/// One instance should be shared app-wide (e.g. one per App.xaml.cs) rather than opened per widget.
/// </summary>
public sealed class IRacingConnection : IDisposable
{
    private const int DisconnectedPollMs = 1000;
    private const int DataEventTimeoutMs = 250;
    private const int MaxBufferReadRetries = 5;

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    private int _lastSessionInfoUpdate = -1;

    public bool IsConnected { get; private set; }
    public TelemetrySnapshot? Latest { get; private set; }
    public IracingSessionInfo? Session { get; private set; }

    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<TelemetrySnapshot>? TelemetryUpdated;
    public event EventHandler<IracingSessionInfo>? SessionInfoUpdated;

    public void Start()
    {
        lock (_gate)
        {
            if (_loopTask is not null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => RunLoop(_cts.Token));
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            try
            {
                _loopTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // expected on cancellation
            }

            _cts?.Dispose();
            _cts = null;
            _loopTask = null;
        }
    }

    public void Dispose() => Stop();

    private void RunLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            MemoryMappedFile? mmf = null;
            EventWaitHandle? dataEvent = null;
            try
            {
                mmf = TryOpenMemoryMappedFile();
                if (mmf is null)
                {
                    SetDisconnected();
                    token.WaitHandle.WaitOne(DisconnectedPollMs);
                    continue;
                }

                dataEvent = TryOpenDataEvent();
                using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                RunConnectedLoop(accessor, dataEvent, token);
            }
            catch (IOException)
            {
                // sim closed mid-read; loop will retry the disconnected-poll path
            }
            finally
            {
                dataEvent?.Dispose();
                mmf?.Dispose();
                SetDisconnected();
            }
        }
    }

    private void RunConnectedLoop(MemoryMappedViewAccessor accessor, EventWaitHandle? dataEvent, CancellationToken token)
    {
        List<IrsdkVarHeader>? varHeaders = null;
        Dictionary<string, IrsdkVarHeader>? varsByName = null;

        while (!token.IsCancellationRequested)
        {
            var headerBytes = new byte[IrsdkConstants.HeaderTotalSize];
            accessor.ReadArray(0, headerBytes, 0, headerBytes.Length);
            var header = IrsdkParser.ParseHeader(headerBytes);

            if (!header.IsConnected)
            {
                SetDisconnected();
                token.WaitHandle.WaitOne(DisconnectedPollMs);
                continue;
            }

            if (varHeaders is null)
            {
                var rawVarHeaders = new byte[header.NumVars * IrsdkConstants.VarHeaderEntrySize];
                accessor.ReadArray(header.VarHeaderOffset, rawVarHeaders, 0, rawVarHeaders.Length);
                varHeaders = IrsdkParser.ParseVarHeaders(rawVarHeaders, 0, header.NumVars);
                varsByName = varHeaders.ToDictionary(v => v.Name, v => v);
            }

            if (header.SessionInfoUpdate != _lastSessionInfoUpdate)
            {
                var yamlBytes = new byte[header.SessionInfoLen];
                accessor.ReadArray(header.SessionInfoOffset, yamlBytes, 0, yamlBytes.Length);
                var yaml = IrsdkParser.ReadSessionInfoYaml(yamlBytes, 0, header.SessionInfoLen);
                Session = IracingSessionInfo.Parse(yaml);
                _lastSessionInfoUpdate = header.SessionInfoUpdate;
                SessionInfoUpdated?.Invoke(this, Session);
            }

            var snapshot = ReadLatestTickWithRetry(accessor, header, varsByName!);
            if (snapshot is not null)
            {
                Latest = snapshot;
                SetConnected();
                TelemetryUpdated?.Invoke(this, snapshot);
            }

            if (dataEvent is not null)
            {
                dataEvent.WaitOne(DataEventTimeoutMs);
            }
            else
            {
                token.WaitHandle.WaitOne(16);
            }
        }
    }

    internal static TelemetrySnapshot? ReadLatestTickWithRetry(
        MemoryMappedViewAccessor accessor, IrsdkHeader header, Dictionary<string, IrsdkVarHeader> varsByName)
    {
        for (var attempt = 0; attempt < MaxBufferReadRetries; attempt++)
        {
            var latestBuf = header.VarBufs
                .Select((buf, index) => (buf, index))
                .OrderByDescending(x => x.buf.TickCount)
                .First();

            var tickCountBefore = latestBuf.buf.TickCount;
            var data = new byte[header.BufLen];
            accessor.ReadArray(latestBuf.buf.BufOffset, data, 0, data.Length);

            var tickCountAfterBytes = new byte[4];
            accessor.ReadArray(
                IrsdkConstants.HeaderVarBufArrayOffset + latestBuf.index * IrsdkConstants.VarBufEntrySize,
                tickCountAfterBytes, 0, 4);
            var tickCountAfter = BitConverter.ToInt32(tickCountAfterBytes);

            if (tickCountAfter == tickCountBefore && tickCountBefore != 0)
            {
                return new TelemetrySnapshot(data, varsByName, tickCountBefore);
            }

            // Buffer was overwritten mid-read (or header just read is now stale) — re-read the header and retry.
            var headerBytes = new byte[IrsdkConstants.HeaderTotalSize];
            accessor.ReadArray(0, headerBytes, 0, headerBytes.Length);
            header = IrsdkParser.ParseHeader(headerBytes);
        }

        return null;
    }

    private void SetConnected()
    {
        if (!IsConnected)
        {
            IsConnected = true;
            Connected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetDisconnected()
    {
        if (IsConnected)
        {
            IsConnected = false;
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private static MemoryMappedFile? TryOpenMemoryMappedFile()
    {
        try
        {
            return MemoryMappedFile.OpenExisting(IrsdkConstants.MemoryMappedFileName, MemoryMappedFileRights.Read);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static EventWaitHandle? TryOpenDataEvent()
    {
        try
        {
            return EventWaitHandle.OpenExisting(IrsdkConstants.DataValidEventName);
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return null;
        }
    }
}
