using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using IRacingOverlay.Coaching.Corners;
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
        CoachingText.LanguageChanged += Reword;
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
            Detail = value is null ? null : new SessionDetail(value.Report);
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
            var items = Sessions.Select(s => new SessionItem(s.Report)).ToList();
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
        Detail = _selected is null ? null : new SessionDetail(_selected.Report, corner);
        OnPropertyChanged(nameof(Selected));
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
            foreach (var r in reports)
            {
                Sessions.Add(keep && r.Id == _selected!.Report.Id ? _selected : new SessionItem(r));
            }
        }
        finally
        {
            _reloading = false;
        }

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
                            var (_, r) = IbtSessionAnalyzer.Analyze(file, _maps);
                            _store.Save(r);
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
            _store.Delete(item.Report.Id);
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
    public SessionItem(SessionReport report)
    {
        Report = report;
        Title = TrackOf(report);
        Subtitle = Format.Join(report.CarName, report.SessionType, CoachingText.T(report.Source == "ibt" ? "coaching.source.ibt" : "coaching.source.live"));
        When = report.StartedUtc == default ? "" : report.StartedUtc.ToLocalTime().ToString("g", CoachingText.Culture);
        BestLap = report.BestLapTime is { } b ? SessionSummaryText.LapTime(b) : CoachingText.T("coaching.tile.bestLap.none");
        Laps = CoachingText.F("coaching.session.cleanLaps", report.CleanLapCount, report.Laps.Count);
    }

    public SessionReport Report { get; }

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
