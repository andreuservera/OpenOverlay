using System.ComponentModel;
using System.Runtime.CompilerServices;
using IRacingOverlay.Coaching.Reports;
using static IRacingOverlay.Coaching.Ui.CoachingText;

namespace IRacingOverlay.Coaching.Ui;

/// <summary>A lap that can be picked for comparing: one of the session's, or the best lap ever.</summary>
/// <param name="Lap">The session's lap number; 0 for a stored lap (<paramref name="Other"/>).</param>
public sealed record LapOption(string Label, int Lap, OtherLap? Other = null)
{
    public bool IsReference => Other is not null;

    public override string ToString() => Label;
}

/// <summary>A lap from outside the session: the best lap ever, or a faster driver's.</summary>
/// <param name="Label">Shown in the list ("Best lap ever · 2:03.456").</param>
/// <param name="Short">Said in a sentence ("your best lap ever").</param>
public sealed record OtherLap(string Label, string Short, Func<StoredLap?> Load);

/// <summary>A stretch of the lap to compare: the whole lap or one corner with its approach and exit.</summary>
public sealed record RangeOption(string Label, int FromM, int ToM)
{
    public override string ToString() => Label;
}

/// <summary>
/// Any two laps of a session (or a lap and the best lap ever for the track and car) overlaid,
/// for the whole lap or one corner, with the time between them. The laps' channels are read from
/// the <see cref="LapTraceStore"/> when picked.
/// </summary>
public sealed class LapComparePanel : INotifyPropertyChanged
{
    /// <summary>Metres shown before a corner's start and after its end.</summary>
    private const int ApproachM = 80, ExitM = 150;

    private readonly SessionReport _report;
    private readonly LapTraceStore _traces;
    private readonly Dictionary<LapOption, StoredLap?> _cache = [];
    private LapOption? _a;
    private LapOption? _b;
    private RangeOption? _range;
    private LapCompareData? _data;
    private string _summary = "";

    public LapComparePanel(SessionReport report, LapTraceStore traces, IReadOnlyList<int> kept, IReadOnlyList<OtherLap> others)
    {
        _report = report;
        _traces = traces;

        var rows = report.Laps.ToDictionary(l => l.Number);
        var options = kept.Select(n =>
        {
            var row = rows.GetValueOrDefault(n);
            var time = row is null ? "" : " · " + Phrases.LapTime(row.Time);
            var note = n == report.BestLapNumber ? " · " + T("coaching.best") : row is { Clean: false } ? " · " + T("coaching.laps.notClean") : "";
            return new LapOption(SessionSummaryText.Capital(F("coaching.lapN", n)) + time + note, n);
        }).ToList();
        options.AddRange(others.Select(o => new LapOption(o.Label, 0, o)));

        Laps = options;
        Ranges = new[] { new RangeOption(T("coaching.laps.wholeLap"), 0, report.TrackLengthMetres) }
            .Concat(report.Corners.Select(c => new RangeOption(c.Name, Math.Max(0, c.StartM - ApproachM), Math.Min(report.TrackLengthMetres, c.EndM + ExitM))))
            .ToList();

        // First the best lap against a typical clean one: what a normal lap gives away, and where.
        var clean = report.Laps.Where(l => l.Clean && l.Number != report.BestLapNumber && kept.Contains(l.Number)).OrderBy(l => l.Time).ToList();
        _a = Laps.FirstOrDefault(o => !o.IsReference && o.Lap == report.BestLapNumber) ?? Laps.FirstOrDefault();
        _b = clean.Count > 0 ? Laps.First(o => !o.IsReference && o.Lap == clean[clean.Count / 2].Number) : Laps.FirstOrDefault(o => o != _a);
        _range = Ranges[0];
        Update();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<LapOption> Laps { get; }

    public IReadOnlyList<RangeOption> Ranges { get; }

    public LapOption? A
    {
        get => _a;
        set => Set(ref _a, value);
    }

    public LapOption? B
    {
        get => _b;
        set => Set(ref _b, value);
    }

    public RangeOption? Range
    {
        get => _range;
        set => Set(ref _range, value);
    }

    public LapCompareData? Data
    {
        get => _data;
        private set
        {
            _data = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasData));
        }
    }

    public bool HasData => _data is not null;

    /// <summary>"Lap 9 is 0.41 s slower than lap 7 here."</summary>
    public string Summary
    {
        get => _summary;
        private set
        {
            _summary = value;
            OnPropertyChanged();
        }
    }

    public string LegendA => _a?.Label ?? "";

    public string LegendB => _b?.Label ?? "";

    /// <summary>Shows a corner of the session in the chart (when a corner is opened in detail).</summary>
    public void ShowCorner(string name)
    {
        if (Ranges.FirstOrDefault(r => r.Label == name) is { } r)
        {
            Range = r;
        }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(name);
        Update();
    }

    private StoredLap? Load(LapOption o)
    {
        if (!_cache.TryGetValue(o, out var lap))
        {
            lap = o.Other is { } other ? other.Load() : _traces.Load(_report.Id, o.Lap);
            _cache[o] = lap;
        }

        return lap;
    }

    private void Update()
    {
        OnPropertyChanged(nameof(LegendA));
        OnPropertyChanged(nameof(LegendB));
        if (_a is null || _b is null || _range is null || Load(_a) is not { } a || Load(_b) is not { } b || Math.Abs(a.Length - b.Length) > 25)
        {
            Data = null;
            Summary = _a is not null && _b is not null ? T("coaching.laps.unavailable") : "";
            return;
        }

        var to = Math.Min(_range.ToM, Math.Min(a.Length, b.Length) - 1);
        var delta = LapDelta.Between(a, b, _range.FromM, to);
        Data = new LapCompareData(a, b, _range.FromM, to, delta, _report.Corners.Select(c => (c.StartM, c.EndM, c.Name)).ToList());
        var d = delta.Length > 0 ? delta[^1] : 0;
        // "here" reads oddly over a whole lap, so that range has its own wording.
        var lap = ReferenceEquals(_range, Ranges[0]) ? "Lap" : "";
        Summary = Math.Abs(d) < 0.005
            ? F("coaching.laps.same" + lap, _b.Label, _a.Label)
            : F((d > 0 ? "coaching.laps.slower" : "coaching.laps.quicker") + lap, SessionSummaryText.Capital(ShortName(_b)), Phrases.Seconds(Math.Abs(d)), ShortName(_a));
    }

    private static string ShortName(LapOption o) => o.Other?.Short ?? F("coaching.lapN", o.Lap);

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
