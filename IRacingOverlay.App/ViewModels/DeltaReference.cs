namespace IRacingOverlay.App.ViewModels;

/// <summary>Which lap the live delta is measured against. Chosen in the settings window, not on
/// the widget itself.</summary>
public enum DeltaReference
{
    SessionBest,
    PersonalBestAllTime,
    OptimalLap,
}
