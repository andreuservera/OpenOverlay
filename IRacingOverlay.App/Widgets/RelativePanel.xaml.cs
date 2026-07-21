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
        Rows.Clear();
        foreach (var row in rows)
        {
            Rows.Add(row);
        }
    }
}
