using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class RelativePanel : UserControl
{
    // Mixed RelativeRow (car) and RelativePlaceholderRow (reserved slot) items — the shared
    // per-DataType templates pick the right visual for each.
    public ObservableCollection<object> Rows { get; } = [];

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

    public void SetRows(IReadOnlyList<object> rows)
    {
        // Sync in-place: Replace at each index instead of Clear + Add, which fires a Reset
        // notification that tears down the entire ItemsControl visual tree every tick.
        for (var i = 0; i < rows.Count; i++)
        {
            if (i < Rows.Count)
                Rows[i] = rows[i];
            else
                Rows.Add(rows[i]);
        }

        while (Rows.Count > rows.Count)
            Rows.RemoveAt(Rows.Count - 1);
    }

    public void SetCarName(string carName) =>
        CarNameText.Text = Options.ShowCarName ? carName.ToUpperInvariant() : "";

    public void SetSessionId(int subSessionId) =>
        SessionIdText.Text = Options.ShowSessionId && subSessionId > 0 ? $"#{subSessionId}" : "";
}
