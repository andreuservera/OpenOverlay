using System.Collections.ObjectModel;
using System.Windows.Controls;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class StandingsPanel : UserControl
{
    public ObservableCollection<StandingsRow> Rows { get; } = [];

    public StandingsPanel()
    {
        InitializeComponent();
    }

    public void SetRows(IReadOnlyList<StandingsRow> rows)
    {
        Rows.Clear();
        foreach (var row in rows)
        {
            Rows.Add(row);
        }
    }
}
