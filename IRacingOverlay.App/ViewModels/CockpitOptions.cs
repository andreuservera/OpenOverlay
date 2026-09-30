using System.ComponentModel;

namespace IRacingOverlay.App.ViewModels;

/// <summary>Cockpit themes. Each is a different dashboard — its own layout, silhouette and visual
/// language — over exactly the same data. Implementations live in Widgets/Cockpit/.</summary>
public enum CockpitTheme
{
    Default,
    GtSports,
    Casual,
    Hypercar,
    PitWall,
    ClassicCar,
    Invisible,
}

/// <summary>Shared by the widget, the dashboard and the preview, so a theme change reaches all three at once.</summary>
public sealed class CockpitOptions : INotifyPropertyChanged
{
    private CockpitTheme _theme = CockpitTheme.Default;

    public event PropertyChangedEventHandler? PropertyChanged;

    public CockpitTheme Theme
    {
        get => _theme;
        set
        {
            if (_theme == value)
            {
                return;
            }

            _theme = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Theme)));
        }
    }
}
