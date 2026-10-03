using System.Diagnostics;
using System.Globalization;

namespace IRacingOverlay.App.Diagnostics;

/// <summary>
/// Temporary UI-thread profiler for the FPS investigation (docs/RENDIMIENTO-FPS-APRENDIZAJES.md).
/// Times and counts allocations per named section and, every <see cref="WindowSeconds"/>, writes one
/// "Perf" line to the log with the process-wide GC picture alongside. UI thread only: sections are
/// not thread-safe, and the allocation figure is the calling thread's own.
/// </summary>
internal static class PerfProbe
{
    public static readonly bool Enabled = true;
    private const int WindowSeconds = 5;

    private static readonly Dictionary<string, Stat> Sections = new();
    private static readonly Dictionary<string, RowStat> RowTables = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static double _windowStartMs;
    private static int _gen0;
    private static int _gen1;
    private static int _gen2;
    private static long _processAllocated;
    private static TimeSpan _gcPause;
    private static bool _started;
    private static int _window;

    public readonly record struct Mark(double StartMs, long StartBytes);

    public static Mark Begin() =>
        Enabled ? new Mark(Clock.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread()) : default;

    public static void End(string section, Mark mark)
    {
        if (!Enabled)
        {
            return;
        }

        Record(section, Clock.Elapsed.TotalMilliseconds - mark.StartMs, GC.GetAllocatedBytesForCurrentThread() - mark.StartBytes);
    }

    /// <summary>For figures measured elsewhere (e.g. a dispatcher delay), with no allocation part.</summary>
    public static void Record(string section, double ms, long bytes = 0)
    {
        if (!Enabled)
        {
            return;
        }

        if (!Sections.TryGetValue(section, out var stat))
        {
            stat = new Stat();
            Sections[section] = stat;
        }

        stat.Count++;
        stat.TotalMs += ms;
        stat.MaxMs = Math.Max(stat.MaxMs, ms);
        stat.TotalBytes += bytes;
        if (ms > 50)
        {
            stat.Over50++;
        }
    }

    /// <summary>One driver-table update: how many rows arrived identical to the one their slot
    /// already shows, and the first property that differed for each row that changed.</summary>
    public static void TallyRows(string table, int rows, int unchanged, IReadOnlyList<string>? differences)
    {
        if (!Enabled)
        {
            return;
        }

        if (!RowTables.TryGetValue(table, out var stat))
        {
            stat = new RowStat();
            RowTables[table] = stat;
        }

        stat.Updates++;
        stat.Rows += rows;
        stat.Unchanged += unchanged;
        if (unchanged == rows)
        {
            stat.AllUnchanged++;
        }

        if (differences is not null)
        {
            foreach (var difference in differences)
            {
                stat.Differences[difference] = stat.Differences.GetValueOrDefault(difference) + 1;
            }
        }
    }

    /// <summary>Called once per UI tick; writes and resets the window when it is due.</summary>
    public static void MaybeFlush()
    {
        if (!Enabled)
        {
            return;
        }

        var now = Clock.Elapsed.TotalMilliseconds;
        if (!_started)
        {
            StartWindow(now);
            _started = true;
            return;
        }

        if (now - _windowStartMs < WindowSeconds * 1000)
        {
            return;
        }

        var seconds = (now - _windowStartMs) / 1000;
        var gcPause = GC.GetTotalPauseDuration() - _gcPause;
        var data = new Dictionary<string, string>
        {
            ["window_s"] = F(seconds),
            ["gc_per_s"] = $"{F((GC.CollectionCount(0) - _gen0) / seconds)}/{F((GC.CollectionCount(1) - _gen1) / seconds)}/{F((GC.CollectionCount(2) - _gen2) / seconds)}",
            ["alloc_MB_per_s"] = F((GC.GetTotalAllocatedBytes() - _processAllocated) / 1048576.0 / seconds),
            ["gc_pause_ms_per_s"] = F(gcPause.TotalMilliseconds / seconds),
            ["heap_MB"] = F(GC.GetTotalMemory(false) / 1048576.0),
        };

        // Heaviest first, by total time: "n=calls avg/max ms, KB per call, calls over 50 ms".
        var order = 0;
        foreach (var (name, stat) in Sections.OrderByDescending(s => s.Value.TotalMs))
        {
            data[$"{order++:00} {name}"] =
                $"n={stat.Count} {F(stat.TotalMs / stat.Count)}/{F(stat.MaxMs)} ms {F(stat.TotalBytes / 1024.0 / stat.Count)} KB" +
                (stat.Over50 > 0 ? $" >50ms={stat.Over50}" : "");
        }

        // "n=updates rows/update, % of rows identical to what was shown, % of updates with every row
        // identical, then the properties that most often made a row differ (first difference only)."
        foreach (var (table, stat) in RowTables)
        {
            var top = string.Join(" ", stat.Differences
                .OrderByDescending(d => d.Value)
                .Take(6)
                .Select(d => $"{d.Key}={d.Value}"));
            data[$"rows {table}"] =
                $"n={stat.Updates} {F((double)stat.Rows / stat.Updates)} rows unchanged={F(100.0 * stat.Unchanged / Math.Max(1, stat.Rows))}% " +
                $"all-unchanged={F(100.0 * stat.AllUnchanged / stat.Updates)}% diff: {top}";
        }

        // Numbered: the log drops a line identical to one from the last minute.
        AppLog.Info("Perf", $"UI thread window {++_window}", data);
        Sections.Clear();
        RowTables.Clear();
        StartWindow(now);
    }

    private static void StartWindow(double now)
    {
        _windowStartMs = now;
        _gen0 = GC.CollectionCount(0);
        _gen1 = GC.CollectionCount(1);
        _gen2 = GC.CollectionCount(2);
        _processAllocated = GC.GetTotalAllocatedBytes();
        _gcPause = GC.GetTotalPauseDuration();
    }

    private static string F(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private sealed class Stat
    {
        public int Count;
        public double TotalMs;
        public double MaxMs;
        public long TotalBytes;
        public int Over50;
    }

    private sealed class RowStat
    {
        public int Updates;
        public int Rows;
        public int Unchanged;
        public int AllUnchanged;
        public readonly Dictionary<string, int> Differences = new();
    }
}
