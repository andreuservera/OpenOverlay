using System.Collections.ObjectModel;
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
        new PropertyMetadata(new DriverTableOptions(DriverTable.Standings)));

    public DriverTableOptions Options
    {
        get => (DriverTableOptions)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    public StandingsPanel()
    {
        InitializeComponent();
    }

    public void SetRows(IReadOnlyList<object> rows) => RowSlot.Sync(Rows, rows, "Standings");

    public void SetSof(double sof) => SofText.Text = sof > 0 ? $"SOF {Math.Round(sof):N0}" : "";

    public void SetClassName(string className) =>
        ClassNameText.Text = Options.ShowClassName ? className.ToUpperInvariant() : "";

    public void SetSessionId(int subSessionId) =>
        SessionIdText.Text = Options.ShowSessionId && subSessionId > 0 ? $"#{subSessionId}" : "";

    public void SetProgress(SessionProgress progress)
    {
        SessionLapsText.Text = progress.LapDisplay;
        SessionTimeText.Text = progress.TimeDisplay;
    }
}
