namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// The preview's own toolbar: which situation it stages and what it is shown over. Every widget
/// whose look depends on the session gets the same scenario picker (race, one class, practice,
/// qualifying, rain, spectating); Flags gets its flag simulator in the same place instead, since
/// for Flags the flag is the situation.
/// </summary>
public sealed partial class ControlPanelViewModel
{
    private static readonly HashSet<string> ScenarioWidgets =
    [
        WidgetCatalog.Relative,
        WidgetCatalog.Standings,
        WidgetCatalog.TrackMap,
        WidgetCatalog.TrackInfo,
        WidgetCatalog.Weather,
    ];

    /// <summary>Scenario and backdrop, shared by every widget's preview.</summary>
    public PreviewSettings PreviewSettings { get; } = new();

    private bool PreviewsFlags => SelectedWidget?.Key == WidgetCatalog.Flag;

    /// <summary>The session picker, for the widgets whose look depends on the session.</summary>
    public bool ShowsScenarioPicker => SelectedWidget is { } slot && ScenarioWidgets.Contains(slot.Key);

    /// <summary>The flag picker, for Flags, where the flag is the situation.</summary>
    public bool ShowsFlagPicker => PreviewsFlags;

    public bool HasPreviewChoices => ShowsScenarioPicker || ShowsFlagPicker;

    // Each picker keeps its own list for good. Swapping one dropdown's list between widgets is
    // what left it blank: the old selection is dropped with the old list.
    public IReadOnlyList<string> ScenarioChoices => PreviewData.ScenarioLabels;

    public IReadOnlyList<string> FlagChoices { get; } = PreviewData.FlagScenarios.Select(scenario => scenario.Label).ToList();

    public int ScenarioIndex
    {
        get => (int)PreviewSettings.Scenario;
        set
        {
            if (value >= 0)
            {
                PreviewSettings.Scenario = (PreviewScenario)value;
            }

            OnPropertyChanged();
        }
    }

    public int FlagChoiceIndex
    {
        get => FlagPreview.Index;
        set
        {
            if (value >= 0)
            {
                FlagPreview.Index = value;
            }

            OnPropertyChanged();
        }
    }

    public IReadOnlyList<string> BackdropChoices { get; } = ["Circuit", "Sky", "Checker"];

    public int BackdropIndex
    {
        get => (int)PreviewSettings.Backdrop;
        set
        {
            if (value < 0)
            {
                return;
            }

            PreviewSettings.Backdrop = (PreviewBackdropMode)value;
            OnPropertyChanged();
        }
    }

    private void OnPreviewTargetChanged()
    {
        OnPropertyChanged(nameof(HasPreviewChoices));
        OnPropertyChanged(nameof(ShowsScenarioPicker));
        OnPropertyChanged(nameof(ShowsFlagPicker));
        OnPropertyChanged(nameof(ScenarioIndex));
        OnPropertyChanged(nameof(FlagChoiceIndex));
    }
}
