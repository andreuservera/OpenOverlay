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
        new PropertyMetadata(new DriverTableOptions(DriverTable.Relative)));

    public DriverTableOptions Options
    {
        get => (DriverTableOptions)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    public RelativePanel()
    {
        InitializeComponent();
    }

    public void SetRows(IReadOnlyList<object> rows) => RowSlot.Sync(Rows, rows, "Relative");

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
