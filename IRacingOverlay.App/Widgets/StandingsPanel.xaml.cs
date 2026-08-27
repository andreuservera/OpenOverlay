using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class StandingsPanel : UserControl
{
    // Mixed StandingsRow (car) and StandingsHeaderRow (class title bar) items — WPF's implicit
    // per-DataType templates in the ItemsControl's Resources pick the right visual for each.
    public ObservableCollection<object> Rows { get; } = [];

    // Must be a real DependencyProperty, not a plain CLR property: XAML's ElementName bindings on
    // "ColumnVisibility.ShowX" latch onto whatever object this returns the moment the binding first
    // evaluates (during InitializeComponent). A plain property swap later (MainWindow assigning its
    // own persisted instance via SetColumnVisibility) wouldn't be noticed — the column would stay
    // stuck showing the constructor-time default forever. A DependencyProperty change correctly
    // triggers the binding to rebind to the new object. Defaults to all-visible and is never
    // reassigned on the Dashboard's own StandingsPanel instance, so only the floating overlay
    // widget's panel ever gets a different (control-panel-editable) instance.
    public static readonly DependencyProperty ColumnVisibilityProperty = DependencyProperty.Register(
        nameof(ColumnVisibility), typeof(StandingsColumnVisibility), typeof(StandingsPanel),
        new PropertyMetadata(new StandingsColumnVisibility()));

    public StandingsColumnVisibility ColumnVisibility
    {
        get => (StandingsColumnVisibility)GetValue(ColumnVisibilityProperty);
        set => SetValue(ColumnVisibilityProperty, value);
    }

    public StandingsPanel()
    {
        InitializeComponent();
    }

    public void SetRows(IReadOnlyList<object> rows)
    {
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

    public void SetSof(double sof) => SofText.Text = sof > 0 ? $"SOF {Math.Round(sof):N0}" : "";
}
