using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// The first-run assistant, in three steps: the monitor iRacing runs on, a preset, then placing the
/// widgets. It replaces twelve switched-off widgets and no guidance with a working overlay in three
/// clicks. The control panel shows it over itself, and it can be run again from Home.
/// </summary>
public sealed class SetupAssistant : INotifyPropertyChanged
{
    public const int MonitorStep = 0;
    public const int PresetStep = 1;
    public const int PlaceStep = 2;

    private readonly Action<SetupChoice, SetupChoice> _apply;
    private readonly Action<bool> _close;
    private int _step;

    /// <param name="monitors">The connected monitors, the primary first.</param>
    /// <param name="apply">Switches the preset on, on the monitor (step 2 → 3).</param>
    /// <param name="close">Closes the assistant; true asks for the widgets to be made movable.</param>
    /// <param name="isFirstRun">Offered on its own on a fresh install, rather than opened by hand:
    /// leaving it is then Loc.T("setup.skipForNow"), not Loc.T("setup.cancel").</param>
    public SetupAssistant(IReadOnlyList<SetupChoice> monitors, Action<SetupChoice, SetupChoice> apply, Action<bool> close, bool isFirstRun = false)
    {
        CancelText = isFirstRun ? Loc.T("setup.skipForNow") : Loc.T("setup.cancel");
        _apply = apply;
        _close = close;
        Monitors = monitors;
        Presets = WidgetPresets.All
            .Select((preset, index) => new SetupChoice(
                preset.Name,
                preset.Description,
                string.Join(" · ", preset.Widgets.Select(key => WidgetCatalog.All.First(d => d.Key == key).Name)),
                index))
            .ToList();
        Select(Monitors, Monitors.FirstOrDefault());
        Select(Presets, Presets.FirstOrDefault());

        NextCommand = new RelayCommand(Next);
        BackCommand = new RelayCommand(() => Step = Math.Max(MonitorStep, _step - 1));
        CancelCommand = new RelayCommand(() => _close(false));
        FinishCommand = new RelayCommand(() => _close(false));
        MoveNowCommand = new RelayCommand(() => _close(true));
        SelectCommand = new ParameterCommand(parameter =>
        {
            if (parameter is SetupChoice choice)
            {
                Select(Monitors.Contains(choice) ? Monitors : Presets, choice);
            }
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<SetupChoice> Monitors { get; }

    public IReadOnlyList<SetupChoice> Presets { get; }

    public int Step
    {
        get => _step;
        private set
        {
            _step = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StepLabel));
            OnPropertyChanged(nameof(CanGoBack));
        }
    }

    public string StepLabel => Loc.F("setup.step", _step + 1);

    public bool CanGoBack => _step == PresetStep;

    public SetupChoice? Monitor => Monitors.FirstOrDefault(choice => choice.IsSelected);

    public SetupChoice? Preset => Presets.FirstOrDefault(choice => choice.IsSelected);

    /// <summary>What the last step says was done.</summary>
    public string Summary => Preset is { } preset && Monitor is { } monitor
        ? Loc.F("setup.summary", preset.Name, preset.Detail, monitor.Name)
        : "";

    /// <summary>What leaving without finishing is called.</summary>
    public string CancelText { get; }

    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand FinishCommand { get; }
    public ICommand MoveNowCommand { get; }
    public ICommand SelectCommand { get; }

    private void Next()
    {
        if (_step == PresetStep && Monitor is { } monitor && Preset is { } preset)
        {
            _apply(monitor, preset);
            OnPropertyChanged(nameof(Summary));
        }

        Step = Math.Min(PlaceStep, _step + 1);
    }

    private static void Select(IReadOnlyList<SetupChoice> choices, SetupChoice? chosen)
    {
        foreach (var choice in choices)
        {
            choice.IsSelected = ReferenceEquals(choice, chosen);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A choice in the assistant: a monitor or a preset, as a selectable card.</summary>
public sealed class SetupChoice(string name, string description, string detail, int index) : INotifyPropertyChanged
{
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; } = name;
    public string Description { get; } = description;
    public string Detail { get; } = detail;

    /// <summary>Which monitor or preset, in the list it came from.</summary>
    public int Index { get; } = index;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}
