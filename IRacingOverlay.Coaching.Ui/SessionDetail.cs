using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using IRacingOverlay.Coaching.History;
using IRacingOverlay.Coaching.References;
using IRacingOverlay.Coaching.Reports;
using static IRacingOverlay.Coaching.Ui.CoachingText;

namespace IRacingOverlay.Coaching.Ui;

/// <summary>One session's post-session analysis, as the page shows it: tiles, summary, recurring
/// mistakes, the corners (one of them open in detail) and the laps. Worded in the app's language
/// when built; the page builds it again when the language changes.</summary>
public sealed class SessionDetail : INotifyPropertyChanged
{
    private CornerItem? _selectedCorner;
    private CornerDetail? _corner;

    private readonly TrackHistory? _history;
    private readonly FocusStore? _focusStore;
    private IReadOnlyList<FocusItem> _focus = [];

    public SessionDetail(SessionReport r, string? selectCornerId = null, TrackHistory? history = null, FocusStore? focus = null,
        LapTraceStore? traces = null, ReferenceLap? reference = null, ReferenceLap? faster = null)
    {
        Report = r;
        var others = new List<OtherLap>();
        if (reference is not null)
        {
            others.Add(new OtherLap(F("coaching.laps.bestEver", Phrases.LapTime(reference.LapTime)), T("coaching.laps.bestEverShort"),
                () => StoredLap.From(0, reference.LapTime, reference.Trace)));
        }

        if (faster is not null)
        {
            others.Add(new OtherLap(F("coaching.laps.faster", Phrases.LapTime(faster.LapTime)), T("coaching.laps.fasterShort"),
                () => StoredLap.From(0, faster.LapTime, faster.Trace)));
        }

        var kept = traces?.Laps(r.Id) ?? [];
        LapCompare = traces is not null && kept.Count + others.Count >= 2 && kept.Count >= 1
            ? new LapComparePanel(r, traces, kept, others)
            : null;
        Faster = new FasterPanel(r, faster, () => SetFasterRequested?.Invoke(), () => ClearFasterRequested?.Invoke());
        _history = history;
        _focusStore = focus;
        History = history is { Sessions.Count: >= 2 } h ? new HistoryPanel(h) : null;
        Replan();
        Title = SessionItem.TrackOf(r);
        Subtitle = Format.Join(
            r.CarName,
            r.SessionType,
            r.StartedUtc == default ? null : r.StartedUtc.ToLocalTime().ToString("D", Culture) + ", " + r.StartedUtc.ToLocalTime().ToString("t", Culture),
            r.Source == "ibt" ? F("coaching.source.ibtFile", Path.GetFileName(r.SourceFile)) : T("coaching.source.live"));

        var clean = r.CleanLapCount;
        Tiles =
        [
            new StatTile(T("coaching.tile.bestLap"), r.BestLapTime is { } b ? Phrases.LapTime(b) : "—",
                r.BestLapNumber is { } n ? F("coaching.tile.bestLap.hint", n) : T("coaching.tile.bestLap.none")),
            new StatTile(T("coaching.tile.theoretical"), r.TheoreticalBestLapTime is { } tb ? Phrases.LapTime(tb) : "—",
                r.TheoreticalBestLapTime is { } t && r.BestLapTime is { } bl ? F("coaching.tile.theoretical.hint", Phrases.Seconds(bl - t)) : T("coaching.tile.theoretical.none")),
            new StatTile(T("coaching.tile.cleanLaps"), $"{clean} / {r.Laps.Count}",
                clean == r.Laps.Count ? T("coaching.tile.cleanLaps.all") : F("coaching.tile.cleanLaps.hint", r.Laps.Count - clean)),
            new StatTile(T("coaching.tile.consistency"), r.CleanLapSpread is { } s ? $"± {Phrases.Seconds(s)}" : "—",
                r.AverageGapToBest is { } g ? F("coaching.tile.consistency.hint", Phrases.Seconds(g)) : T("coaching.tile.consistency.none")),
        ];

        Headline = Phrases.Headline(r);
        Notes = Phrases.Notes(r);
        Mistakes = r.Mistakes.Select((m, i) => new MistakeItem(i + 1, m)).ToList();

        var maxLoss = r.Corners.Select(c => c.MedianLoss ?? 0).DefaultIfEmpty(0).Max();
        Corners = r.Corners.Select(c => new CornerItem(c, maxLoss)).ToList();
        Laps = r.Laps.Select(l => new LapItem(l, r.BestLapNumber)).ToList();

        // Open the corner asked for, else the one a normal lap loses most in: where the summary points first.
        SelectedCorner = Corners.FirstOrDefault(c => c.Report.Id == selectCornerId)
            ?? Corners.Where(c => c.Report.MedianLoss is not null).MaxBy(c => c.Report.MedianLoss)
            ?? Corners.FirstOrDefault();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SessionReport Report { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public IReadOnlyList<StatTile> Tiles { get; }

    public IReadOnlyList<string> Headline { get; }

    public IReadOnlyList<string> Notes { get; }

    public bool HasNotes => Notes.Count > 0;

    public IReadOnlyList<MistakeItem> Mistakes { get; }

    public bool HasMistakes => Mistakes.Count > 0;

    public bool HasNoMistakes => Mistakes.Count == 0;

    public string NoMistakesText => T(Report.CleanLapCount < 3 ? "coaching.mistakes.fewLaps" : "coaching.mistakes.none");

    public IReadOnlyList<CornerItem> Corners { get; }

    public bool HasCorners => Corners.Count > 0;

    public CornerItem? SelectedCorner
    {
        get => _selectedCorner;
        set
        {
            if (ReferenceEquals(_selectedCorner, value))
            {
                return;
            }

            _selectedCorner = value;
            Corner = value is null ? null : new CornerDetail(value.Report, CanPin ? PinState(value.Report) : null);
            if (value is not null)
            {
                LapCompare?.ShowCorner(value.Report.Name);
            }

            OnPropertyChanged();
        }
    }

    public CornerDetail? Corner
    {
        get => _corner;
        private set
        {
            _corner = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCorner));
        }
    }

    public bool HasCorner => _corner is not null;

    /// <summary>Asks the page to pick a faster driver's lap for this session's track and car.</summary>
    public event Action? SetFasterRequested;

    /// <summary>Asks the page to forget the faster driver's lap.</summary>
    public event Action? ClearFasterRequested;

    /// <summary>The session against a faster driver's lap, corner by corner.</summary>
    public FasterPanel Faster { get; }

    /// <summary>Any two laps overlaid; null when the session's laps weren't kept (older reports).</summary>
    public LapComparePanel? LapCompare { get; }

    public bool HasLapCompare => LapCompare is not null;

    /// <summary>Progress across the sessions at this track with this car; null with only one.</summary>
    public HistoryPanel? History { get; }

    public bool HasHistory => History is not null;

    /// <summary>What to practise next session: pinned corners, or the ones with most to gain.</summary>
    public IReadOnlyList<FocusItem> Focus
    {
        get => _focus;
        private set
        {
            _focus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasFocus));
            OnPropertyChanged(nameof(FocusHint));
        }
    }

    public bool HasFocus => _focus.Count > 0;

    public string FocusHint => T(_focus.Any(f => f.Pinned) ? "coaching.focus.hintPinned" : "coaching.focus.hint");

    private bool CanPin => _focusStore is not null && Report.TrackKey is not null && Report.CarName is not null;

    private PinState PinState(CornerReport c)
    {
        var pinned = _focusStore!.IsPinned(Report.TrackKey!, Report.CarName!, c.ApexM);
        return new PinState(pinned, isPinned =>
        {
            try
            {
                _focusStore.Set(Report.TrackKey!, Report.CarName!, c.Id, c.Name, c.ApexM, isPinned);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return false;
            }

            Replan();
            return true;
        });
    }

    private void Replan()
    {
        if (_history is null)
        {
            Focus = [];
            return;
        }

        var pinned = CanPin ? _focusStore!.Pinned(Report.TrackKey!, Report.CarName!) : null;
        Focus = FocusPlanner.Plan(_history, pinned).Select(f => new FocusItem(f)).ToList();
    }

    public IReadOnlyList<LapItem> Laps { get; }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record StatTile(string Label, string Value, string Hint)
{
    public override string ToString() => $"{Label}: {Value}";
}

public sealed class MistakeItem(int rank, RecurringMistake m)
{
    public string Rank { get; } = rank.ToString(CultureInfo.InvariantCulture);

    public string Corner { get; } = m.CornerName;

    public string Problem { get; } = Phrases.Problem(m.Kind, m.TypicalAmount, m.IsRamp, m.Laps, m.OutOf, m.TypicalLoss, m.Problem, m.NoBraking);

    public string Advice { get; } = Phrases.Advice(m.Kind, m.TypicalAmount, m.IsRamp, m.Advice, m.NoBraking) + ".";

    public string Cost { get; } = F("coaching.mistakes.cost", Phrases.Seconds(m.TypicalLoss), m.Laps, m.OutOf);

    public override string ToString() => $"{Rank}. {Corner}: {Problem} {Advice}";
}

/// <summary>A row of the corners table.</summary>
public sealed class CornerItem
{
    /// <summary>Widest loss bar, device-independent pixels.</summary>
    private const double BarWidth = 90;

    public CornerItem(CornerReport c, double maxLoss)
    {
        Report = c;
        Name = c.Name;
        var kind = Phrases.CornerKind(c);
        Kind = c.Name != c.Id ? Format.Join(c.Id, kind) : kind ?? "";
        BestTime = c.BestTime is { } b ? b.ToString("0.000", Format.Culture) : "—";
        BestLap = c.BestLap is { } l ? F("coaching.lapN", l) : "";
        TypicalLoss = c.MedianLoss is { } m ? $"+{m.ToString("0.00", Format.Culture)}" : "—";
        LossBar = c.MedianLoss is { } ml && maxLoss > 0 ? Math.Max(2, BarWidth * ml / maxLoss) : 0;
        BestLapLoss = c.BestLapLoss is { } bll ? $"+{bll.ToString("0.00", Format.Culture)}" : "—";
        var main = FindingContradictions.Without(c.Findings).FirstOrDefault();
        MainFinding = main is null ? "" : $"{Phrases.Advice(main.Kind, main.TypicalAmount, main.IsRamp, main.Advice, main.NoBraking)} ({main.Laps}/{main.OutOf})";
    }

    public CornerReport Report { get; }

    public string Name { get; }

    public string Kind { get; }

    public string BestTime { get; }

    public string BestLap { get; }

    public string TypicalLoss { get; }

    public double LossBar { get; }

    public string BestLapLoss { get; }

    public string MainFinding { get; }

    public override string ToString() => string.IsNullOrEmpty(MainFinding) ? $"{Name}: {TypicalLoss}" : $"{Name}: {TypicalLoss}, {MainFinding}";
}

/// <summary>Whether a corner is pinned to work on, and how to change it (false when it couldn't be saved).</summary>
public sealed record PinState(bool Pinned, Func<bool, bool> Set);

/// <summary>One corner open in detail: the traces, best against typical, and every pass.</summary>
public sealed class CornerDetail : INotifyPropertyChanged
{
    private readonly PinState? _pin;
    private bool _pinned;

    public CornerDetail(CornerReport c, PinState? pin = null)
    {
        Report = c;
        _pin = pin;
        _pinned = pin?.Pinned ?? false;
        TogglePinCommand = new RelayCommand(() =>
        {
            if (_pin is not null && _pin.Set(!_pinned))
            {
                _pinned = !_pinned;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PinText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPinned)));
            }
        });
        Title = c.Name;
        Subtitle = Format.Join(
            c.Name != c.Id ? c.Id : null,
            Phrases.CornerKind(c),
            $"{c.StartM}–{c.EndM} m",
            F(c.CleanPasses == 1 ? "coaching.corner.cleanPass" : "coaching.corner.cleanPasses", c.CleanPasses));
        BestTrace = c.BestTrace;
        TypicalTrace = c.TypicalTrace;
        HasTrace = c.BestTrace is not null;
        BestLegend = c.Best is { } b ? F("coaching.corner.bestPass", b.Lap) : T("coaching.compare.best");
        TypicalLegend = c.Typical is { } t ? F("coaching.corner.typicalPass", t.Lap, (t.Loss ?? 0).ToString("0.00", Format.Culture)) : "";
        HasTypical = c.TypicalTrace is not null;

        var best = c.Best;
        var typ = c.Typical;
        Compare =
        [
            Row("coaching.compare.time", best?.Time, typ?.Time, v => $"{v:0.000} s"),
            Row("coaching.compare.entry", best?.EntryKmh, typ?.EntryKmh, v => $"{v:0} km/h"),
            Row("coaching.compare.brakePoint", best?.BrakePointM, typ?.BrakePointM, v => $"{v:0} m"),
            Row("coaching.compare.peakBrake", best?.PeakBrakePct, typ?.PeakBrakePct, v => $"{v:0}%"),
            Row("coaching.compare.trailBrake", best?.TrailBrakeM, typ?.TrailBrakeM, v => $"{v:0} m"),
            Row("coaching.compare.minSpeed", best?.MinSpeedKmh, typ?.MinSpeedKmh, v => $"{v:0} km/h"),
            Row("coaching.compare.minSpeedAt", best?.MinSpeedAtM, typ?.MinSpeedAtM, v => $"{v:0} m"),
            Row("coaching.compare.throttleAt", best?.ThrottleM, typ?.ThrottleM, v => $"{v:0} m"),
            Row("coaching.compare.exit", best?.ExitKmh, typ?.ExitKmh, v => $"{v:0} km/h"),
            Row("coaching.compare.gear", best?.Gear, typ?.Gear, v => $"{v:0}"),
        ];

        // Opposite advice over a few laps (brake earlier, brake later) only says the corner varies.
        Findings = FindingContradictions.Without(c.Findings).Select(f => new FindingItem(
            Phrases.Advice(f.Kind, f.TypicalAmount, f.IsRamp, f.Advice, f.NoBraking),
            F("coaching.corner.findingDetail", f.Laps, f.OutOf, Phrases.Seconds(f.TypicalLoss)))).ToList();
        Passes = c.Passes.Select(p => new PassItem(p)).ToList();

        var maxPhase = c.LossByPhase.Select(p => Math.Abs(p.Seconds)).DefaultIfEmpty(0).Max();
        Phases = c.LossByPhase.Select(p => new PhaseItem(
            Phrases.Phase(p),
            (p.Seconds >= 0 ? "+" : "−") + Math.Abs(p.Seconds).ToString("0.00", Format.Culture),
            maxPhase > 0 ? Math.Max(1, PhaseBarWidth * Math.Abs(p.Seconds) / maxPhase) : 0,
            p.Seconds < 0)).ToList();

        var insights = new List<string>();
        if (Phrases.DecidedBy(c) is { } d)
        {
            insights.Add(F("coaching.insight.decidedBy", d));
        }

        if (c.Grip is { } g)
        {
            string Pct(double? v) => v is { } x ? $"{Math.Round(x * 100):0}%" : "—";
            if (g.BestBraking is not null || g.TypicalBraking is not null)
            {
                insights.Add(F("coaching.insight.gripBraking", Pct(g.BestBraking), Pct(g.TypicalBraking)));
            }

            if (g.BestCornering is not null || g.TypicalCornering is not null)
            {
                insights.Add(F("coaching.insight.gripCornering", Pct(g.BestCornering), Pct(g.TypicalCornering)));
            }
        }

        if (c.TimeSpread is { } ts)
        {
            var time = ts.ToString("0.00", Format.Culture);
            insights.Add(c.BrakePointSpreadM is { } bps
                ? F("coaching.insight.consistencyBrake", time, bps.ToString("0", Format.Culture))
                : c.LiftPointSpreadM is { } lps
                    ? F("coaching.insight.consistencyLift", time, lps.ToString("0", Format.Culture))
                    : F("coaching.insight.consistency", time));
        }

        Insights = insights;
    }

    /// <summary>Widest phase bar, device-independent pixels.</summary>
    private const double PhaseBarWidth = 140;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The corner can be pinned (the session knows its track and car).</summary>
    public bool CanPin => _pin is not null;

    public bool IsPinned => _pinned;

    public string PinText => T(_pinned ? "coaching.focus.unpin" : "coaching.focus.pin");

    public ICommand TogglePinCommand { get; }

    /// <summary>Where a normal lap loses its time here, phase by phase.</summary>
    public IReadOnlyList<PhaseItem> Phases { get; }

    public bool HasPhases => Phases.Count > 0;

    /// <summary>Grip used, consistency and what decides the time, in sentences.</summary>
    public IReadOnlyList<string> Insights { get; }

    public bool HasInsights => Insights.Count > 0;

    public CornerReport Report { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public CornerTrace? BestTrace { get; }

    public CornerTrace? TypicalTrace { get; }

    public bool HasTrace { get; }

    public bool HasTypical { get; }

    public string BestLegend { get; }

    public string TypicalLegend { get; }

    public IReadOnlyList<CompareRow> Compare { get; }

    public IReadOnlyList<FindingItem> Findings { get; }

    public bool HasFindings => Findings.Count > 0;

    public IReadOnlyList<PassItem> Passes { get; }

    private static CompareRow Row(string labelKey, double? best, double? typical, Func<double, FormattableString> fmt)
    {
        string Show(double? v) => v is { } x ? fmt(x).ToString(Format.Culture) : "—";
        var diff = "";
        if (best is { } b && typical is { } t && Math.Abs(t - b) > 1e-9)
        {
            var d = t - b;
            diff = (d > 0 ? "+" : "−") + fmt(Math.Abs(d)).ToString(Format.Culture);
        }

        return new CompareRow(T(labelKey), Show(best), Show(typical), diff);
    }

    private static CompareRow Row(string labelKey, int? best, int? typical, Func<double, FormattableString> fmt) =>
        Row(labelKey, (double?)best, (double?)typical, fmt);
}

public sealed record PhaseItem(string Phase, string Seconds, double BarWidth, bool IsGain)
{
    /// <summary>What screen readers say for the row (UI Automation names an item by its text).</summary>
    public override string ToString() => $"{Phase}: {Seconds}";
}

/// <summary>The session against a faster driver's lap: per corner, your best and a normal lap
/// against theirs, and what they do differently.</summary>
public sealed class FasterPanel
{
    public FasterPanel(SessionReport r, ReferenceLap? faster, Action pick, Action clear)
    {
        CanSet = r.TrackKey is not null && r.CarName is not null && r.Corners.Count > 0;
        PickCommand = new RelayCommand(pick);
        ClearCommand = new RelayCommand(clear);
        if (faster is null)
        {
            Title = "";
            Rows = [];
            return;
        }

        Title = F("coaching.faster.lap", Phrases.LapTime(faster.LapTime), faster.SourceFile is { } f ? Path.GetFileName(f) : faster.Car);
        Rows = FasterLapComparison.Compare(r, faster).Select(g => new FasterRow(g)).ToList();
        var technique = Rows.Count(x => x.Gap.Verdict == GapVerdict.Technique);
        var consistency = Rows.Count(x => x.Gap.Verdict == GapVerdict.Consistency);
        Summary = r.BestLapTime is { } b
            ? F("coaching.faster.summary", Phrases.Seconds(b - faster.LapTime), technique, consistency)
            : "";
    }

    /// <summary>The session knows its track and car, so a faster lap can be set for them.</summary>
    public bool CanSet { get; }

    public bool HasLap => Rows.Count > 0;

    public bool HasNoLap => !HasLap;

    public string PickText => T(HasLap ? "coaching.faster.replace" : "coaching.faster.pick");

    public string Title { get; }

    public string Summary { get; } = "";

    public IReadOnlyList<FasterRow> Rows { get; }

    public ICommand PickCommand { get; }

    public ICommand ClearCommand { get; }
}

public sealed class FasterRow(CornerGap g)
{
    public CornerGap Gap { get; } = g;

    public string Name { get; } = g.Name;

    public string Times { get; } = $"{(g.YourBest is { } y ? y.ToString("0.000", Format.Culture) : "—")} / {g.FasterTime.ToString("0.000", Format.Culture)}";

    public string GapText { get; } = g.Gap is { } x ? (x >= 0 ? "+" : "−") + Math.Abs(x).ToString("0.00", Format.Culture) : "—";

    public string Normal { get; } = g.NormalLoss is { } n ? $"+{n.ToString("0.00", Format.Culture)}" : "—";

    public string Verdict { get; } = T($"coaching.faster.verdict.{g.Verdict.ToString().ToLowerInvariant()}");

    public bool IsTechnique => g.Verdict == GapVerdict.Technique;

    public string Differences { get; } = string.Join(", ", g.Differences.Select(d =>
        F($"coaching.faster.diff.{d.Measure}.{(d.Amount > 0 ? "more" : "less")}", Math.Round(Math.Abs(d.Amount)).ToString("0", Format.Culture))
        + (d.SetupSensitive ? "*" : "")));
}

/// <summary>One corner of the focus plan, worded.</summary>
public sealed class FocusItem(FocusCorner f)
{
    public string Name { get; } = f.Name;

    public bool Pinned { get; } = f.Pinned;

    public string What { get; } = SessionSummaryText.Capital(f.Habit is { } h
        ? Phrases.Advice(h.Kind, h.TypicalAmount, h.IsRamp, h.Advice, h.NoBraking)
        : T("coaching.focus.traces"));

    public string Aim { get; } = f.AimLoss is { } l ? F("coaching.focus.aim", Phrases.Seconds(l)) : "";

    public string Gain { get; } = f.Gain is { } g ? F("coaching.focus.gain", Phrases.Seconds(g)) : "";

    public override string ToString() => $"{Name}: {What}";
}

/// <summary>Progress across sessions at one track with one car.</summary>
public sealed class HistoryPanel
{
    public HistoryPanel(TrackHistory h)
    {
        Hint = F("coaching.history.hint", h.Sessions.Count, h.Car, h.BestLapEver is { } b ? Phrases.LapTime(b) : "—");
        var laps = h.Sessions.Where(s => s.BestLapTime is not null).ToList();
        var lo = laps.Select(s => s.BestLapTime!.Value).DefaultIfEmpty(0).Min();
        var hi = laps.Select(s => s.BestLapTime!.Value).DefaultIfEmpty(0).Max();
        Sessions = laps.Select(s => new HistorySessionItem(
            s.StartedUtc == default ? "" : s.StartedUtc.ToLocalTime().ToString("d MMM", Culture),
            Phrases.LapTime(s.BestLapTime!.Value),
            hi - lo > 1e-6 ? SparkHeight * (0.25 + 0.75 * (hi - s.BestLapTime!.Value) / (hi - lo)) : SparkHeight * 0.6,
            s.BestLapTime == h.BestLapEver)).ToList();
        Corners = h.Corners.Select(c => new CornerHistoryItem(c)).ToList();
    }

    private const double SparkHeight = 36;

    public string Hint { get; }

    /// <summary>Each session's best lap, oldest first, as bars: taller is quicker.</summary>
    public IReadOnlyList<HistorySessionItem> Sessions { get; }

    public IReadOnlyList<CornerHistoryItem> Corners { get; }
}

public sealed record HistorySessionItem(string Date, string BestLap, double BarHeight, bool IsBest)
{
    /// <summary>What screen readers say for the bar (UI Automation names an item by its text), not the record's fields.</summary>
    public override string ToString() => $"{Date}: {BestLap}";
}

public sealed class CornerHistoryItem(CornerHistory c)
{
    public string Name { get; } = c.Name;

    public string BestEver { get; } = c.BestEver is { } b ? b.ToString("0.000", Format.Culture) : "—";

    public string BestEverWhen { get; } = c.BestEverUtc is { } w ? w.ToLocalTime().ToString("d MMM", Culture) : "";

    /// <summary>Normal loss at first and now: "0.42 → 0.18".</summary>
    public string Loss { get; } = c.RecentLoss is not { } now ? "—"
        : c.EarlyLoss is { } then ? $"{then.ToString("0.00", Format.Culture)} → {now.ToString("0.00", Format.Culture)}"
        : now.ToString("0.00", Format.Culture);

    /// <summary>−1 better, +1 worse, 0 about the same or unknown (a 0.03 s band is noise).</summary>
    public int Trend { get; } = c.EarlyLoss is { } e && c.RecentLoss is { } r && Math.Abs(r - e) >= 0.03 ? Math.Sign(r - e) : 0;

    public string TrendText => Trend switch { < 0 => T("coaching.history.better"), > 0 => T("coaching.history.worse"), _ => "" };

    public bool IsBetter => Trend < 0;

    public bool IsWorse => Trend > 0;

    public string Habit { get; } = c.Habit is not { } h ? ""
        : c.HabitShareFirst is { } first && c.HabitShareLast is { } last
            ? F("coaching.history.habit", SessionSummaryText.Capital(Phrases.Advice(h.Kind, h.TypicalAmount, h.IsRamp, h.Advice, h.NoBraking)), Math.Round(first * 100), Math.Round(last * 100))
            : SessionSummaryText.Capital(Phrases.Advice(h.Kind, h.TypicalAmount, h.IsRamp, h.Advice, h.NoBraking));
}

public sealed record CompareRow(string Label, string Best, string Typical, string Difference)
{
    public override string ToString() => $"{Label}: {Best}, {Typical}, {Difference}";
}

public sealed record FindingItem(string Advice, string Detail)
{
    public override string ToString() => $"{Advice}, {Detail}";
}

public sealed class PassItem(PassRow p)
{
    public string Lap { get; } = p.Lap.ToString(CultureInfo.InvariantCulture);

    public string Time { get; } = p.Time.ToString("0.000", Format.Culture);

    public string Loss { get; } = p.IsBest ? T("coaching.best") : p.Loss is { } l ? $"+{l.ToString("0.00", Format.Culture)}" : Phrases.Flaws(p.Flaws);

    public string MinSpeed { get; } = p.MinSpeedKmh.ToString("0", Format.Culture);

    public string Brake { get; } = p.BrakePointM is { } b ? $"{b}" : "—";

    public string Throttle { get; } = p.ThrottleM is { } t ? $"{t}" : "—";

    public string Exit { get; } = p.ExitKmh.ToString("0", Format.Culture);

    public string Findings { get; } = !p.Clean
        ? F("coaching.pass.notClean", Phrases.Flaws(p.Flaws))
        : string.Join("; ", p.Issues.Count == p.Findings.Count && p.Issues.Count > 0
            ? p.Issues.Select((i, n) => Phrases.Advice(i.Kind, i.Amount, i.IsRamp, p.Findings[n], i.NoBraking))
            : p.Findings.Select(SessionSummaryText.Capital));

    public bool IsBest { get; } = p.IsBest;

    public bool IsClean { get; } = p.Clean;
}

public sealed class LapItem(LapRow l, int? bestLap)
{
    public string Lap { get; } = l.Number.ToString(CultureInfo.InvariantCulture);

    public string Time { get; } = Phrases.LapTime(l.Time);

    public string Delta { get; } = l.Clean && l.Number == bestLap && l.DeltaToBest is 0
        ? T("coaching.best")
        : l.DeltaToBest is { } d ? $"+{d.ToString("0.000", Format.Culture)}" : "";

    public string Status { get; } = l.Clean
        ? T(l.Compared ? "coaching.lap.clean" : "coaching.lap.cleanBeforeChange")
        : Phrases.Flaws(string.Join(",", l.Flaws));

    public string CornerLoss { get; } = l.CornerLoss is { } c ? $"{c.ToString("0.00", Format.Culture)} s" : "";

    public bool IsBest { get; } = l.Clean && l.Number == bestLap && l.DeltaToBest is 0;

    public bool IsClean { get; } = l.Clean;
}

internal static class Format
{
    public static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
