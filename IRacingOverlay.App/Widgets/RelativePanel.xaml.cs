using System.Collections.ObjectModel;
using System.Windows.Controls;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class RelativePanel : UserControl
{
    public ObservableCollection<RelativeRow> Rows { get; } = [];

    public RelativePanel()
    {
        InitializeComponent();
    }

    public void SetRows(IReadOnlyList<RelativeRow> rows)
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
}
