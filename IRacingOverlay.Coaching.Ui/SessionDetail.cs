using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using IRacingOverlay.Coaching.Reports;

namespace IRacingOverlay.Coaching.Ui;

/// <summary>One session's post-session analysis, as the page shows it: tiles, summary, recurring
/// mistakes, the corners (one of them open in detail) and the laps.</summary>
public sealed class SessionDetail : INotifyPropertyChanged
{
    private CornerItem? _selectedCorner;
    private CornerDetail? _corner;

    public SessionDetail(SessionReport r)
    {
        Report = r;
        Title = r.TrackName ?? r.TrackKey ?? "Unknown track";
        Subtitle = Format.Join(
            r.CarName,
            r.SessionType,
            r.StartedUtc == default ? null : r.StartedUtc.ToLocalTime().ToString("dddd d MMMM yyyy, HH:mm", Format.Culture),
            r.Source == "ibt" ? $"analysed from {Path.GetFileName(r.SourceFile)}" : "live coach");

        var clean = r.CleanLapCount;
        Tiles =
        [
            new StatTile("BEST LAP", r.BestLapTime is { } b ? SessionSummaryText.LapTime(b) : "—", r.BestLapNumber is { } n ? $"lap {n}" : "no clean lap"),
            new StatTile("THEORETICAL BEST", r.TheoreticalBestLapTime is { } tb ? SessionSummaryText.LapTime(tb) : "—",
                r.TheoreticalBestLapTime is { } t && r.BestLapTime is { } bl ? $"{Format.Seconds(bl - t)} in your best corners" : "needs a clean pass of every corner"),
            new StatTile("CLEAN LAPS", $"{clean} / {r.Laps.Count}", clean == r.Laps.Count ? "every lap counted" : $"{r.Laps.Count - clean} didn't count"),
            new StatTile("CONSISTENCY", r.CleanLapSpread is { } s ? $"± {Format.Seconds(s)}" : "—",
                r.AverageGapToBest is { } g ? $"{Format.Seconds(g)} off best on average" : "needs two clean laps"),
        ];

        Headline = SessionSummaryText.Headline(r);
        Notes = r.Notes;
        Mistakes = r.Mistakes.Select((m, i) => new MistakeItem(i + 1, m)).ToList();

        var maxLoss = r.Corners.Select(c => c.MedianLoss ?? 0).DefaultIfEmpty(0).Max();
        Corners = r.Corners.Select(c => new CornerItem(c, maxLoss)).ToList();
        Laps = r.Laps.Select(l => new LapItem(l, r.BestLapNumber)).ToList();

        // Open the corner a normal lap loses most in: where the summary points first.
        SelectedCorner = Corners.Where(c => c.Report.MedianLoss is not null).MaxBy(c => c.Report.MedianLoss) ?? Corners.FirstOrDefault();
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

    public string NoMistakesText => Report.CleanLapCount < 3
        ? "Drive at least three clean laps for the mistakes you repeat to show up here."
        : "No mistake came back often enough in the same corner to call it a habit. Check the corners below for the biggest losses.";

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

    public string Problem { get; } = m.Problem;

    public string Advice { get; } = m.Advice + ".";

    public string Cost { get; } = $"{Format.Seconds(m.TypicalLoss)} a lap · {m.Laps} of {m.OutOf} laps";
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
        Kind = c.Name != c.Id ? Format.Join(c.Id, c.Kind) : c.Kind ?? "";
        BestTime = c.BestTime is { } b ? $"{b.ToString("0.000", Format.Culture)}" : "—";
        BestLap = c.BestLap is { } l ? $"lap {l}" : "";
        TypicalLoss = c.MedianLoss is { } m ? $"+{m.ToString("0.00", Format.Culture)}" : "—";
        LossBar = c.MedianLoss is { } ml && maxLoss > 0 ? Math.Max(2, BarWidth * ml / maxLoss) : 0;
        BestLapLoss = c.BestLapLoss is { } bll ? $"+{bll.ToString("0.00", Format.Culture)}" : "—";
        var main = c.Findings.FirstOrDefault();
        MainFinding = main is null ? "" : $"{SessionSummaryText.Capital(main.Advice)} ({main.Laps}/{main.OutOf})";
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
        Subtitle = Format.Join(c.Name != c.Id ? c.Id : null, c.Kind, $"{c.StartM}–{c.EndM} m", $"{c.CleanPasses} clean pass{(c.CleanPasses == 1 ? "" : "es")}");
        BestTrace = c.BestTrace;
        TypicalTrace = c.TypicalTrace;
        HasTrace = c.BestTrace is not null;
        BestLegend = c.Best is { } b ? $"Best pass, lap {b.Lap}" : "Best pass";
        TypicalLegend = c.Typical is { } t ? $"Typical pass, lap {t.Lap} (+{(t.Loss ?? 0).ToString("0.00", Format.Culture)} s)" : "";
        HasTypical = c.TypicalTrace is not null;

        var best = c.Best;
        var typ = c.Typical;
        Compare =
        [
            Row("Time (corner + straight)", best?.Time, typ?.Time, v => $"{v:0.000} s"),
            Row("Entry speed", best?.EntryKmh, typ?.EntryKmh, v => $"{v:0} km/h"),
            Row("Brake point", best?.BrakePointM, typ?.BrakePointM, v => $"{v:0} m"),
            Row("Peak brake", best?.PeakBrakePct, typ?.PeakBrakePct, v => $"{v:0}%"),
            Row("Trail brake", best?.TrailBrakeM, typ?.TrailBrakeM, v => $"{v:0} m"),
            Row("Minimum speed", best?.MinSpeedKmh, typ?.MinSpeedKmh, v => $"{v:0} km/h"),
            Row("Minimum speed at", best?.MinSpeedAtM, typ?.MinSpeedAtM, v => $"{v:0} m"),
            Row("Throttle at", best?.ThrottleM, typ?.ThrottleM, v => $"{v:0} m"),
            Row("Speed 100 m after", best?.ExitKmh, typ?.ExitKmh, v => $"{v:0} km/h"),
            Row("Gear at apex", best?.Gear, typ?.Gear, v => $"{v:0}"),
        ];

        Findings = c.Findings.Select(f => new FindingItem(
            SessionSummaryText.Capital(f.Advice),
            $"main issue on {f.Laps} of {f.OutOf} laps, about {Format.Seconds(f.TypicalLoss)} each")).ToList();
        Passes = c.Passes.Select(p => new PassItem(p)).ToList();
    }

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

    private static CompareRow Row(string label, double? best, double? typical, Func<double, FormattableString> fmt)
    {
        string Show(double? v) => v is { } x ? fmt(x).ToString(Format.Culture) : "—";
        var diff = "";
        if (best is { } b && typical is { } t && Math.Abs(t - b) > 1e-9)
        {
            var d = t - b;
            var text = fmt(Math.Abs(d)).ToString(Format.Culture);
            diff = (d > 0 ? "+" : "−") + text;
        }

        return new CompareRow(label, Show(best), Show(typical), diff);
    }

    private static CompareRow Row(string label, int? best, int? typical, Func<double, FormattableString> fmt) =>
        Row(label, (double?)best, (double?)typical, fmt);
}

public sealed record CompareRow(string Label, string Best, string Typical, string Difference);

public sealed record FindingItem(string Advice, string Detail);

public sealed class PassItem(PassRow p)
{
    public string Lap { get; } = p.Lap.ToString(CultureInfo.InvariantCulture);

    public string Time { get; } = p.Time.ToString("0.000", Format.Culture);

    public string Loss { get; } = p.IsBest ? "best" : p.Loss is { } l ? $"+{l.ToString("0.00", Format.Culture)}" : p.Flaws ?? "";

    public string MinSpeed { get; } = $"{p.MinSpeedKmh.ToString("0", Format.Culture)}";

    public string Brake { get; } = p.BrakePointM is { } b ? $"{b}" : "—";

    public string Throttle { get; } = p.ThrottleM is { } t ? $"{t}" : "—";

    public string Exit { get; } = $"{p.ExitKmh.ToString("0", Format.Culture)}";

    public string Findings { get; } = !p.Clean ? $"Not clean: {p.Flaws}" : string.Join("; ", p.Findings);

    public bool IsBest { get; } = p.IsBest;

    public bool IsClean { get; } = p.Clean;
}

public sealed class LapItem(LapRow l, int? bestLap)
{
    public string Lap { get; } = l.Number.ToString(CultureInfo.InvariantCulture);

    public string Time { get; } = SessionSummaryText.LapTime(l.Time);

    public string Delta { get; } = l.Clean && l.Number == bestLap && l.DeltaToBest is 0 ? "best" : l.DeltaToBest is { } d ? $"+{d.ToString("0.000", Format.Culture)}" : "";

    public string Status { get; } = l.Clean
        ? (l.Compared ? "clean" : "clean, before the car changed")
        : string.Join(", ", l.Flaws.Select(Format.Flaw)) + (l.UncleanPieces.Count > 0 ? $" — {string.Join("; ", l.UncleanPieces.Take(2))}" : "");

    public string CornerLoss { get; } = l.CornerLoss is { } c ? $"{c.ToString("0.00", Format.Culture)} s" : "";

    public bool IsBest { get; } = l.Clean && l.Number == bestLap && l.DeltaToBest is 0;

    public bool IsClean { get; } = l.Clean;
}

internal static class Format
{
    public static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    public static string Seconds(double s) => $"{s.ToString("0.00", Culture)} s";

    public static string Flaw(string flaw) => flaw switch
    {
        "OffTrack" => "off track",
        "Incident" => "incident",
        "PitRoad" => "pit lane",
        "Partial" => "out lap / partial",
        "StartLap" => "start or out lap",
        "NotRacing" => "not racing",
        "NotOnTrack" => "left the car",
        "TelemetryGap" => "telemetry gap",
        "Discontinuity" => "reset",
        _ => flaw,
    };
}
