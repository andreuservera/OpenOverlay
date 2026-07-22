using System.Windows.Controls;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class TrackInfoPanel : UserControl
{
    public TrackInfoPanel()
    {
        InitializeComponent();
    }

    public void UpdateState(TrackInfoState state)
    {
        TrackNameText.Text = state.TrackNameDisplay;
        SessionLabelText.Text = state.SessionLabelDisplay;
        AirTempText.Text = state.AirTempDisplay;
        TrackTempText.Text = state.TrackTempDisplay;
        WindText.Text = state.WindDisplay;
        HumidityText.Text = state.HumidityDisplay;
        TimeRemainingText.Text = state.TimeRemainingDisplay;
        LapsRemainingText.Text = state.LapsRemainingDisplay;
    }
}
