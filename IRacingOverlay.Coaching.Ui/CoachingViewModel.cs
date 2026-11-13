using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.References;
using IRacingOverlay.Coaching.Reports;

namespace IRacingOverlay.Coaching.Ui;

/// <summary>
/// The Coaching page: the saved session reports, newest first, and the one picked. Reports are only
/// read when the page is first shown (and on Refresh), so the page costs nothing until it is opened.
/// </summary>
public sealed class CoachingViewModel : INotifyPropertyChanged
{
    private readonly SessionReportStore _store;
    private readonly CornerMapStore _maps;
    private SessionItem? _selected;
    private SessionDetail? _detail;
    private string _status = "";
    private bool _busy;
    private bool _loaded;
    private bool _reloading;

    public CoachingViewModel()
        : this(new SessionReportStore(SessionReportStore.DefaultDirectory), new CornerMapStore(CornerMapStore.DefaultDirectory))
    {
    }

    public CoachingViewModel(SessionReportStore store, CornerMapStore maps)
    {
        _store = store;
        _maps = maps;
        RefreshCommand = new RelayCommand(Refresh, () => !IsBusy);
        ImportIbtCommand = new RelayCommand(ImportIbt, () => !IsBusy);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        DeleteCommand = new RelayCommand(DeleteSelected, () => Selected is not null && !IsBusy);
        SetReferenceCommand = new RelayCommand(SetReferenceFromIbt, () => !IsBusy);
        CoachingText.LanguageChanged += Reword;
        // A shortcut can switch live coaching while the page is open.
        LiveCoaching.SettingsChanged += () =>
        {
            OnPropertyChanged(nameof(LiveEnabled));
            OnPropertyChanged(nameof(SpeakDebrief));
            OnPropertyChanged(nameof(CompareWithBestEver));
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<SessionItem> Sessions { get; } = [];

    public SessionItem? Selected
    {
        get => _selected;
        set
        {
            // Clearing the list while reloading makes the list box push a null selection: ignore it.
            if (_reloading || ReferenceEquals(_selected, value))
            {
                return;
            }

            _selected = value;
            Detail = value is null ? null : NewDetail(value.Report);
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public SessionDetail? Detail
    {
        get => _detail;
        private set
        {
            _detail = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDetail));
        }
    }

    public bool HasDetail => _detail is not null;

    public bool HasSessions => Sessions.Count > 0;

    public bool IsEmpty => _loaded && Sessions.Count == 0;

    /// <summary>One line about the last thing done (analysing a file, a failure).</summary>
    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasStatus));
        }
    }

    public bool HasStatus => _status.Length > 0;

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            _busy = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string FolderPath => _store.Directory;

    /// <summary>Live coaching on: the coach records each session while iRacing runs. Off, it doesn't
    /// run at all; analysing an .ibt still works.</summary>
    public bool LiveEnabled
    {
        get => LiveCoaching.Settings.LiveEnabled;
        set
        {
            LiveCoaching.Change(LiveCoaching.Settings with { LiveEnabled = value });
            OnPropertyChanged();
        }
    }

    /// <summary>With live coaching on, a short spoken debrief after each clean lap.</summary>
    public bool SpeakDebrief
    {
        get => LiveCoaching.Settings.SpeakDebrief;
        set
        {
            LiveCoaching.Change(LiveCoaching.Settings with { SpeakDebrief = value });
            OnPropertyChanged();
        }
    }

    /// <summary>Live coaching compares with the best lap ever for the track and car (see
    /// <see cref="References"/>) instead of this session's best.</summary>
    public bool CompareWithBestEver
    {
        get => LiveCoaching.Settings.CompareWithBestEver;
        set
        {
            LiveCoaching.Change(LiveCoaching.Settings with { CompareWithBestEver = value });
            OnPropertyChanged();
        }
    }

    /// <summary>The best lap ever stored for each track and car.</summary>
    public ObservableCollection<ReferenceItem> References { get; } = [];

    public bool HasReferences => References.Count > 0;

    public bool HasNoReferences => References.Count == 0;

    /// <summary>Picks an .ibt whose best lap becomes its track and car's reference, even if slower
    /// (a faster driver's lap, or one from another setup).</summary>
    public ICommand SetReferenceCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand ImportIbtCommand { get; }

    public ICommand OpenFolderCommand { get; }

    public ICommand DeleteCommand { get; }

    /// <summary>Reads the reports the first time the page is shown; later visits refresh them, so a
    /// session coached in the meantime shows up.</summary>
    public void OnShown()
    {
        Refresh();
    }

    public void Refresh() => Load(Selected?.Report.Id);

    /// <summary>The language changed: word the list and the open session again, keeping what is picked.</summary>
    private void Reword()
    {
        var id = _selected?.Report.Id;
        var corner = _detail?.SelectedCorner?.Report.Id;
        _reloading = true;
        try
        {
            var items = Sessions.Select(s => new SessionItem(s.Report, s.Copies)).ToList();
            Sessions.Clear();
            foreach (var item in items)
            {
                Sessions.Add(item);
            }
        }
        finally
        {
            _reloading = false;
        }

        _selected = Sessions.FirstOrDefault(s => s.Report.Id == id);
        Detail = _selected is null ? null : NewDetail(_selected.Report, corner);
        OnPropertyChanged(nameof(Selected));
    }

    /// <summary>The session's detail with its track history: the sessions at the same track with
    /// the same car up to this one.</summary>
    private SessionDetail NewDetail(SessionReport r, string? corner = null)
    {
        var history = r.TrackKey is { } track && r.CarName is { } car
            ? History.TrackHistory.Build(Sessions.Select(s => s.Report).Where(o => o.StartedUtc <= r.StartedUtc), track, car)
            : null;
        var reference = r.TrackKey is { } t && r.CarName is { } c ? LiveCoaching.References.Load(t, c) : null;
        return new SessionDetail(r, corner, history, LiveCoaching.Focus, LapTraceStore.For(_store),
            reference is null ? null : () => StoredLap.From(0, reference.LapTime, reference.Trace), reference?.LapTime);
    }

    private void Load(string? select)
    {
        IReadOnlyList<SessionReport> reports;
        try
        {
            reports = _store.LoadAll();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status = CoachingText.F("coaching.status.readFailed", e.Message);
            reports = [];
        }

        // Keep the open session (and the corner picked in it) when nothing about it changed.
        var keep = Selected is { } current && reports.FirstOrDefault(r => r.Id == current.Report.Id) is { } same && same.UpdatedUtc == current.Report.UpdatedUtc;
        _reloading = true;
        try
        {
            Sessions.Clear();
            // A drive saved live and again from its .ibt is one entry, showing the better report.
            foreach (var drive in History.TrackHistory.Drives(reports))
            {
                var r = drive.Kept;
                Sessions.Add(keep && r.Id == _selected!.Report.Id && _selected.Copies.Count == drive.Copies.Count
                    ? _selected
                    : new SessionItem(r, drive.Copies));
            }
        }
        finally
        {
            _reloading = false;
        }

        LoadReferences(reports);
        _loaded = true;
        OnPropertyChanged(nameof(HasSessions));
        OnPropertyChanged(nameof(IsEmpty));
        if (!keep)
        {
            _selected = null;
            Selected = Sessions.FirstOrDefault(s => s.Report.Id == select) ?? Sessions.FirstOrDefault();
        }
        else
        {
            OnPropertyChanged(nameof(Selected));
        }
    }

    private void ImportIbt()
    {
        var telemetry = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "iRacing", "telemetry");
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = CoachingText.T("coaching.ibt.dialogTitle"),
            Filter = CoachingText.T("coaching.ibt.filter") + " (*.ibt)|*.ibt",
            Multiselect = true,
            InitialDirectory = Directory.Exists(telemetry) ? telemetry : null,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _ = AnalyzeAsync(dialog.FileNames);
    }

    /// <summary>Runs the coach over each file on a background thread, saves its report, and opens the last one.</summary>
    public async Task AnalyzeAsync(IReadOnlyList<string> files)
    {
        IsBusy = true;
        string? last = null;
        var failed = new List<string>();
        try
        {
            for (var i = 0; i < files.Count; i++)
            {
                var file = files[i];
                Status = files.Count > 1
                    ? CoachingText.F("coaching.status.analyzingOf", Path.GetFileName(file), i + 1, files.Count)
                    : CoachingText.F("coaching.status.analyzing", Path.GetFileName(file));
                try
                {
                    var report = await Task.Run(() =>
                    {
                        Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                        try
                        {
                            var (session, r) = IbtSessionAnalyzer.Analyze(file, _maps, references: LiveCoaching.References, types: LiveCoaching.Types);
                            _store.Save(r);
                            try
                            {
                                LapTraceStore.For(_store).SaveAll(r.Id, session);
                            }
                            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                            {
                                // Laps can't be overlaid for this session; the report is there.
                            }
                            return r;
                        }
                        finally
                        {
                            Thread.CurrentThread.Priority = ThreadPriority.Normal;
                        }
                    });
                    last = report.Id;
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    failed.Add($"{Path.GetFileName(file)} ({e.Message})");
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        Status = failed.Count == 0 ? "" : CoachingText.F("coaching.status.analyzeFailed", string.Join(", ", failed));
        _selected = null;
        Load(last);
    }

    private void LoadReferences(IReadOnlyList<SessionReport> reports)
    {
        var names = reports.Where(r => r.TrackKey is not null && r.TrackName is not null)
            .GroupBy(r => r.TrackKey!).ToDictionary(g => g.Key, g => g.First().TrackName);
        References.Clear();
        try
        {
            foreach (var info in LiveCoaching.References.List(k => names.GetValueOrDefault(k)))
            {
                References.Add(new ReferenceItem(info, ClearReference));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status = CoachingText.F("coaching.status.readFailed", e.Message);
        }

        OnPropertyChanged(nameof(HasReferences));
        OnPropertyChanged(nameof(HasNoReferences));
    }

    private void ClearReference(ReferenceItem item)
    {
        try
        {
            LiveCoaching.References.Delete(item.Info.TrackKey, item.Info.Car);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status = CoachingText.F("coaching.status.deleteFailed", e.Message);
        }

        Refresh();
    }

    private void SetReferenceFromIbt()
    {
        var telemetry = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "iRacing", "telemetry");
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = CoachingText.T("coaching.reference.dialogTitle"),
            Filter = CoachingText.T("coaching.ibt.filter") + " (*.ibt)|*.ibt",
            InitialDirectory = Directory.Exists(telemetry) ? telemetry : null,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _ = SetReferenceAsync(dialog.FileName);
    }

    /// <summary>Makes the best clean lap of <paramref name="file"/> its track and car's reference.</summary>
    public async Task SetReferenceAsync(string file)
    {
        IsBusy = true;
        Status = CoachingText.F("coaching.status.analyzing", Path.GetFileName(file));
        try
        {
            var message = await Task.Run(() =>
            {
                var (session, report) = IbtSessionAnalyzer.Analyze(file, _maps, saveMap: false);
                if (session.BestLap is not { Trace: not null } best || report.TrackKey is null || report.CarName is null)
                {
                    return CoachingText.T("coaching.reference.noLap");
                }

                LiveCoaching.References.Save(new ReferenceLap
                {
                    TrackKey = report.TrackKey,
                    Car = report.CarName,
                    LapTime = best.LapTime,
                    RecordedUtc = report.StartedUtc,
                    Source = "imported",
                    SourceFile = Path.GetFullPath(file),
                    Trace = best.Trace,
                });
                return "";
            });
            Status = message;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Status = CoachingText.F("coaching.status.analyzeFailed", $"{Path.GetFileName(file)} ({e.Message})");
        }
        finally
        {
            IsBusy = false;
        }

        Refresh();
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(_store.Directory);
            Process.Start(new ProcessStartInfo { FileName = _store.Directory, UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            Status = CoachingText.F("coaching.status.openFailed", _store.Directory, e.Message);
        }
    }

    private void DeleteSelected()
    {
        if (Selected is not { } item)
        {
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            CoachingText.F("coaching.delete.question", item.Title, item.When),
            CoachingText.T("coaching.delete.title"), System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            // The other copies of the same drive go with it, or one would take its place in the list.
            foreach (var report in item.Copies.Prepend(item.Report))
            {
                _store.Delete(report.Id);
                LapTraceStore.For(_store).Delete(report.Id);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status = CoachingText.F("coaching.status.deleteFailed", e.Message);
            return;
        }

        var index = Sessions.IndexOf(item);
        _selected = null;
        Load(Sessions.Count > index + 1 ? Sessions[index + 1].Report.Id : index > 0 ? Sessions[index - 1].Report.Id : null);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A session in the list.</summary>
public sealed class SessionItem
{
    public SessionItem(SessionReport report, IReadOnlyList<SessionReport>? copies = null)
    {
        Report = report;
        Copies = copies ?? [];
        Title = TrackOf(report);
        var source = Copies.Count > 0 ? "coaching.source.both" : report.Source == "ibt" ? "coaching.source.ibt" : "coaching.source.live";
        Subtitle = Format.Join(report.CarName, report.SessionType, CoachingText.T(source));
        When = report.StartedUtc == default ? "" : report.StartedUtc.ToLocalTime().ToString("g", CoachingText.Culture);
        BestLap = report.BestLapTime is { } b ? SessionSummaryText.LapTime(b) : CoachingText.T("coaching.tile.bestLap.none");
        Laps = CoachingText.F("coaching.session.cleanLaps", report.CleanLapCount, report.Laps.Count);
    }

    public SessionReport Report { get; }

    /// <summary>The other reports of the same drive, hidden from the list (saved live and from its .ibt).</summary>
    public IReadOnlyList<SessionReport> Copies { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string When { get; }

    internal static string TrackOf(SessionReport r) => r.TrackName ?? r.TrackKey ?? CoachingText.T("coaching.session.unknownTrack");

    public string BestLap { get; }

    public string Laps { get; }
}

internal sealed class RelayCommand(Action run, Func<bool>? canRun = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canRun?.Invoke() ?? true;

    public void Execute(object? parameter) => run();
}

/// <summary>A stored best lap ever, in the references list.</summary>
public sealed class ReferenceItem
{
    public ReferenceItem(ReferenceLapInfo info, Action<ReferenceItem> clear)
    {
        Info = info;
        Title = $"{info.TrackName ?? info.TrackKey} · {info.Car}";
        var when = info.RecordedUtc.ToLocalTime().ToString("d MMM yyyy", CoachingText.Culture);
        Detail = CoachingText.F(info.Source == "imported" ? "coaching.reference.detailImported" : "coaching.reference.detail",
            SessionSummaryText.LapTime(info.LapTime), when);
        ClearCommand = new RelayCommand(() => clear(this));
    }

    public ReferenceLapInfo Info { get; }

    public string Title { get; }

    public string Detail { get; }

    public ICommand ClearCommand { get; }
}
