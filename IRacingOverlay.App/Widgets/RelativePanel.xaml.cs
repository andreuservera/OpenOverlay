using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class RelativePanel : UserControl
{
    // One RowSlot per line, holding a RelativeRow (car) or RelativePlaceholderRow (reserved slot) —
    // the shared per-DataType templates pick the right visual for each. See RowSlot for why.
    public ObservableCollection<RowSlot> Rows { get; } = [];

    // Same reasoning as StandingsPanel: XAML bindings on "Options.ShowX" latch onto whatever object
    // this returns the moment they first evaluate, so swapping in the control panel's persisted
    // instance later has to be a DependencyProperty change to be noticed.
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(DriverTableOptions), typeof(RelativePanel),
        new PropertyMetadata(new DriverTableOptions(DriverTable.Relative), (d, e) => ((RelativePanel)d)._bands?.Follow((DriverTableOptions)e.NewValue)));

    private readonly TableInfoBands _bands;

    public DriverTableOptions Options
    {
        get => (DriverTableOptions)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    public RelativePanel()
    {
        InitializeComponent();
        _bands = new TableInfoBands(
            new Dictionary<TableSlot, Panel>
            {
                [TableSlot.TopLeft] = TopLeftSlot,
                [TableSlot.TopCenter] = TopCenterSlot,
                [TableSlot.TopRight] = TopRightSlot,
                [TableSlot.BottomLeft] = BottomLeftSlot,
                [TableSlot.BottomCenter] = BottomCenterSlot,
                [TableSlot.BottomRight] = BottomRightSlot,
            },
            new Dictionary<TableInfoElement, FrameworkElement>
            {
                [TableInfoElement.SessionType] = SessionTypeText,
                [TableInfoElement.Sof] = SofText,
                [TableInfoElement.SessionLaps] = SessionLapsField,
                [TableInfoElement.SessionTime] = SessionTimeField,
                [TableInfoElement.BrakeBias] = BrakeBiasField,
                [TableInfoElement.AirTemp] = AirTempField,
                [TableInfoElement.TrackTemp] = TrackTempField,
                [TableInfoElement.Humidity] = HumidityField,
            });
        _bands.Follow(Options);
    }

    public void SetRows(IReadOnlyList<object> rows) => RowSlot.Sync(Rows, rows, "Relative");

    public void SetSessionType(string label) =>
        SessionTypeText.Text = label;

    public void SetSof(double sof) => SofText.Text = StandingsPanel.FormatSof(sof);

    public void SetConditions(TableConditions conditions)
    {
        BrakeBiasText.Text = conditions.BrakeBiasDisplay;
        AirTempIcon.Condition = conditions.Condition;
        AirTempText.Text = conditions.AirTempDisplay;
        TrackTempText.Text = conditions.TrackTempDisplay;
        HumidityText.Text = conditions.HumidityDisplay;
    }

    public void SetProgress(SessionProgress progress)
    {
        SessionLapsText.Text = progress.LapDisplay;
        SessionTimeText.Text = progress.TimeDisplay;
    }
}
