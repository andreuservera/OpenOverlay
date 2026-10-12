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

    /// <summary>Whether the open widget has situations worth previewing it in.</summary>
    public bool HasPreviewChoices => SelectedWidget is { } slot && (PreviewsFlags || ScenarioWidgets.Contains(slot.Key));

    public string PreviewChoiceCaption => PreviewsFlags ? "SIMULATE" : "SCENARIO";

    public IReadOnlyList<string> PreviewChoices => PreviewsFlags
        ? PreviewData.FlagScenarios.Select(scenario => scenario.Label).ToList()
        : PreviewData.ScenarioLabels;

    public int PreviewChoiceIndex
    {
        get => PreviewsFlags ? FlagPreview.Index : (int)PreviewSettings.Scenario;
        set
        {
            if (value < 0)
            {
                return;
            }

            if (PreviewsFlags)
            {
                FlagPreview.Index = value;
            }
            else
            {
                PreviewSettings.Scenario = (PreviewScenario)value;
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
        OnPropertyChanged(nameof(PreviewChoiceCaption));
        OnPropertyChanged(nameof(PreviewChoices));
        OnPropertyChanged(nameof(PreviewChoiceIndex));
    }
}
