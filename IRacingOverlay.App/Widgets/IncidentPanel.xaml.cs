using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class IncidentPanel : UserControl
{
    public IncidentPanel()
    {
        InitializeComponent();
    }

    public void UpdateState(IncidentState state)
    {
        var isTeamRace = state.TeamIncidentCount is not null;
        var severity = state.Severity switch
        {
            IncidentSeverity.Critical => StatePalette.Critical,
            IncidentSeverity.Warning => StatePalette.Warning,
            _ => StatePalette.TextPrimary,
        };

        MyCountText.Text = state.MyIncidentCount.ToString(CultureInfo.InvariantCulture);
        MyCaption.Visibility = isTeamRace ? Visibility.Visible : Visibility.Collapsed;
        TeamBlock.Visibility = isTeamRace ? Visibility.Visible : Visibility.Collapsed;
        TeamCountText.Text = state.TeamIncidentCount?.ToString(CultureInfo.InvariantCulture) ?? "";

        // The limit applies to the team total in a team race, so that's the number that changes colour.
        MyCountText.Foreground = isTeamRace ? StatePalette.TextPrimary : severity;
        TeamCountText.Foreground = severity;
        LimitText.Text = state.Limit is { } limit ? $"/ {limit.ToString(CultureInfo.InvariantCulture)}" : "";
    }
}
