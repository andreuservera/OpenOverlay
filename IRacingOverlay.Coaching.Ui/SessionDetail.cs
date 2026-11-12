using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
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

    public SessionDetail(SessionReport r, string? selectCornerId = null)
    {
        Report = r;
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
            Corner = value is null ? null : new CornerDetail(value.Report);
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

    public IReadOnlyList<LapItem> Laps { get; }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record StatTile(string Label, string Value, string Hint);

public sealed class MistakeItem(int rank, RecurringMistake m)
{
    public string Rank { get; } = rank.ToString(CultureInfo.InvariantCulture);

    public string Corner { get; } = m.CornerName;

    public string Problem { get; } = Phrases.Problem(m.Kind, m.TypicalAmount, m.IsRamp, m.Laps, m.OutOf, m.TypicalLoss, m.Problem, m.NoBraking);

    public string Advice { get; } = Phrases.Advice(m.Kind, m.TypicalAmount, m.IsRamp, m.Advice, m.NoBraking) + ".";

    public string Cost { get; } = F("coaching.mistakes.cost", Phrases.Seconds(m.TypicalLoss), m.Laps, m.OutOf);
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
        var main = c.Findings.FirstOrDefault();
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
}

/// <summary>One corner open in detail: the traces, best against typical, and every pass.</summary>
public sealed class CornerDetail
{
    public CornerDetail(CornerReport c)
    {
        Report = c;
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

        Findings = c.Findings.Select(f => new FindingItem(
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

public sealed record PhaseItem(string Phase, string Seconds, double BarWidth, bool IsGain);

public sealed record CompareRow(string Label, string Best, string Typical, string Difference);

public sealed record FindingItem(string Advice, string Detail);

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
