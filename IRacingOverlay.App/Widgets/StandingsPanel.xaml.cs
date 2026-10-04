using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class StandingsPanel : UserControl
{
    // One RowSlot per line, holding a StandingsRow (car), StandingsSeparatorRow (podium/dynamic-block
    // break) or StandingsHeaderRow (class title bar) — WPF's implicit per-DataType templates in the
    // ItemsControl's Resources pick the right visual for each. See RowSlot for why slots.
    public ObservableCollection<RowSlot> Rows { get; } = [];

    // Must be a real DependencyProperty, not a plain CLR property: XAML's ElementName bindings on
    // "Options.ShowX" latch onto whatever object this returns the moment the binding first
    // evaluates (during InitializeComponent). A plain property swap later (MainWindow assigning its
    // own persisted instance via SetOptions) wouldn't be noticed — the column would stay
    // stuck showing the constructor-time default forever. A DependencyProperty change correctly
    // triggers the binding to rebind to the new object. Defaults to all-visible and is never
    // reassigned on the Dashboard's own StandingsPanel instance, so only the floating overlay
    // widget's panel ever gets a different (control-panel-editable) instance.
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(DriverTableOptions), typeof(StandingsPanel),
        new PropertyMetadata(new DriverTableOptions(DriverTable.Standings), (d, e) => ((StandingsPanel)d)._bands?.Follow((DriverTableOptions)e.NewValue)));

    private readonly TableInfoBands _bands;

    public DriverTableOptions Options
    {
        get => (DriverTableOptions)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    public StandingsPanel()
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
            });
        _bands.Follow(Options);
    }

    public void SetRows(IReadOnlyList<object> rows) => RowSlot.Sync(Rows, rows, "Standings");

    public void SetSof(double sof) => SofText.Text = FormatSof(sof);

    /// <summary>"SOF 2.9k": thousands to one decimal, truncated rather than rounded, so 2,987 never
    /// claims to be a 3k lobby. Under 1,000 the actual SOF, as a whole number; nothing without a
    /// strength.</summary>
    internal static string FormatSof(double sof)
    {
        if (double.IsNaN(sof) || sof <= 0)
        {
            return "";
        }

        var whole = Math.Round(sof);
        return whole < 1000
            ? $"SOF {whole.ToString("0", CultureInfo.InvariantCulture)}"
            : $"SOF {(Math.Floor(whole / 100) / 10).ToString("0.0", CultureInfo.InvariantCulture)}k";
    }

    /// <summary>The session type; whether and where it shows is the options' call (TableInfoBands).</summary>
    public void SetSessionType(string label) => SessionTypeText.Text = label;

    public void SetProgress(SessionProgress progress)
    {
        SessionLapsText.Text = progress.LapDisplay;
        SessionTimeText.Text = progress.TimeDisplay;
    }
}
