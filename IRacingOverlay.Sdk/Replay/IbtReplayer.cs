using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using IRacingOverlay.Sdk.Interop;

namespace IRacingOverlay.Sdk.Replay;

/// <summary>
/// Plays an .ibt recording into the same named shared memory iRacing writes, at the recorded speed,
/// so anything reading iRacing (the overlays included) sees the session as if it were live. Only
/// works while iRacing itself is closed: the shared memory can only have one writer.
/// </summary>
public sealed class IbtReplayer : IDisposable
{
    private const int Buffers = 3;

    private readonly IbtRecording _recording;
    private readonly string _mapName;
    private readonly string _eventName;
    private CancellationTokenSource? _cts;
    private Task? _task;
    private int _row;

    public IbtReplayer(IbtRecording recording)
        : this(recording, IrsdkConstants.MemoryMappedFileName, IrsdkConstants.DataValidEventName)
    {
    }

    internal IbtReplayer(IbtRecording recording, string mapName, string eventName)
    {
        _recording = recording;
        _mapName = mapName;
        _eventName = eventName;
    }

    /// <summary>The row being played.</summary>
    public int Row => Volatile.Read(ref _row);

    public bool IsPlaying => _task is { IsCompleted: false };

    /// <summary>Raised on the replay thread when the recording ends, is stopped, or fails (with the error).</summary>
    public event Action<Exception?>? Finished;

    /// <summary>Lays out the shared memory and starts playing from the first row (or <paramref name="fromRow"/>).</summary>
    /// <exception cref="InvalidOperationException">iRacing, or another replay, already owns the shared memory.</exception>
    public void Start(int fromRow = 0, bool loop = false)
    {
        if (_task is not null)
        {
            throw new InvalidOperationException("Already playing.");
        }

        var layout = SharedMemoryLayout.For(_recording);
        MemoryMappedFile map;
        try
        {
            map = MemoryMappedFile.CreateNew(_mapName, layout.TotalSize, MemoryMappedFileAccess.ReadWrite);
        }
        catch (IOException e)
        {
            throw new InvalidOperationException("iRacing is running (or another replay is playing).", e);
        }

        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, _eventName);
        var accessor = map.CreateViewAccessor(0, layout.TotalSize);
        layout.WriteHeaders(accessor, _recording);

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _row = Math.Clamp(fromRow, 0, Math.Max(0, _recording.Rows - 1));
        _task = Task.Factory.StartNew(() =>
        {
            Exception? error = null;
            try
            {
                Play(layout, accessor, signal, loop, token);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                error = e;
            }
            finally
            {
                // Tell readers the "sim" has gone, the way iRacing does on exit.
                try
                {
                    accessor.Write(IrsdkConstants.HeaderStatusOffset, 0);
                    signal.Set();
                }
                catch (ObjectDisposedException)
                {
                }

                accessor.Dispose();
                map.Dispose();
                signal.Dispose();
            }

            Finished?.Invoke(error);
        }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public void Stop()
    {
        var cts = _cts;
        var task = _task;
        _cts = null;
        _task = null;
        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        try
        {
            task?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        cts.Dispose();
    }

    public void Dispose()
    {
        Stop();
        _recording.Dispose();
    }

    private void Play(SharedMemoryLayout layout, MemoryMappedViewAccessor accessor, EventWaitHandle signal, bool loop, CancellationToken token)
    {
        var row = new byte[_recording.BufLen];
        var clock = Stopwatch.StartNew();
        var start = _row;
        var tick = 0;
        var secondsPerRow = 1.0 / _recording.TickRate;
        while (!token.IsCancellationRequested)
        {
            var index = Volatile.Read(ref _row);
            if (index >= _recording.Rows)
            {
                if (!loop)
                {
                    return;
                }

                start = 0;
                index = 0;
                Volatile.Write(ref _row, 0);
                clock.Restart();
            }

            _recording.ReadRow(index, row);
            tick++;
            var slot = tick % Buffers;
            // Data first, then the tick count: a reader takes the buffer with the newest tick count.
            accessor.WriteArray(layout.BufferOffset(slot), row, 0, row.Length);
            accessor.Write(SharedMemoryLayout.VarBufEntry(slot), tick);
            signal.Set();
            Volatile.Write(ref _row, index + 1);

            // Hold the recorded pace: row n goes out n/60 s after the start.
            var due = (index + 1 - start) * secondsPerRow;
            var wait = due - clock.Elapsed.TotalSeconds;
            if (wait > 0.002)
            {
                token.WaitHandle.WaitOne(TimeSpan.FromSeconds(wait));
            }
        }
    }

    /// <summary>Where everything goes in the shared memory: header, variable headers, YAML, then the rotating tick buffers.</summary>
    internal sealed record SharedMemoryLayout(int VarHeaderOffset, int SessionInfoOffset, int FirstBufferOffset, int BufLen, int TotalSize)
    {
        public static SharedMemoryLayout For(IbtRecording recording)
        {
            var varHeaderOffset = IrsdkConstants.HeaderTotalSize;
            var sessionInfoOffset = varHeaderOffset + recording.VarHeaders.Length;
            // Room for the YAML and its terminating zero, rounded up so the buffers start aligned.
            var firstBuffer = Align(sessionInfoOffset + recording.SessionInfo.Length + 1, 16);
            return new SharedMemoryLayout(varHeaderOffset, sessionInfoOffset, firstBuffer, recording.BufLen,
                firstBuffer + Buffers * recording.BufLen);
        }

        public int BufferOffset(int slot) => FirstBufferOffset + slot * BufLen;

        public static int VarBufEntry(int slot) => IrsdkConstants.HeaderVarBufArrayOffset + slot * IrsdkConstants.VarBufEntrySize;

        public byte[] HeaderBytes(IbtRecording recording)
        {
            var header = new byte[IrsdkConstants.HeaderTotalSize];
            var span = header.AsSpan();
            BinaryPrimitives.WriteInt32LittleEndian(span[IrsdkConstants.HeaderVerOffset..], 2);
            BinaryPrimitives.WriteInt32LittleEndian(span[IrsdkConstants.HeaderStatusOffset..], IrsdkConstants.StatusConnected);
            BinaryPrimitives.WriteInt32LittleEndian(span[IrsdkConstants.HeaderTickRateOffset..], recording.TickRate);
            BinaryPrimitives.WriteInt32LittleEndian(span[IrsdkConstants.HeaderSessionInfoUpdateOffset..], 1);
            BinaryPrimitives.WriteInt32LittleEndian(span[IrsdkConstants.HeaderSessionInfoLenOffset..], recording.SessionInfo.Length);
            BinaryPrimitives.WriteInt32LittleEndian(span[IrsdkConstants.HeaderSessionInfoOffsetOffset..], SessionInfoOffset);
            BinaryPrimitives.WriteInt32LittleEndian(span[IrsdkConstants.HeaderNumVarsOffset..], recording.NumVars);
            BinaryPrimitives.WriteInt32LittleEndian(span[IrsdkConstants.HeaderVarHeaderOffsetOffset..], VarHeaderOffset);
            BinaryPrimitives.WriteInt32LittleEndian(span[IrsdkConstants.HeaderNumBufOffset..], Buffers);
            BinaryPrimitives.WriteInt32LittleEndian(span[IrsdkConstants.HeaderBufLenOffset..], BufLen);
            for (var slot = 0; slot < Buffers; slot++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(span[VarBufEntry(slot)..], 0);
                BinaryPrimitives.WriteInt32LittleEndian(span[(VarBufEntry(slot) + 4)..], BufferOffset(slot));
            }

            return header;
        }

        public void WriteHeaders(MemoryMappedViewAccessor accessor, IbtRecording recording)
        {
            accessor.WriteArray(VarHeaderOffset, recording.VarHeaders, 0, recording.VarHeaders.Length);
            accessor.WriteArray(SessionInfoOffset, recording.SessionInfo, 0, recording.SessionInfo.Length);
            accessor.Write(SessionInfoOffset + recording.SessionInfo.Length, (byte)0);
            var header = HeaderBytes(recording);
            accessor.WriteArray(0, header, 0, header.Length);
        }

        private static int Align(int value, int to) => (value + to - 1) / to * to;
    }
}
