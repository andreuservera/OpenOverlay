using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>What the preview is staged as: the session it shows (<see cref="PreviewScenario"/>) and
/// what it is shown over. Preview-only state: never saved, never seen by a live widget.</summary>
public sealed class PreviewSettings : INotifyPropertyChanged
{
    private PreviewScenario _scenario;
    private PreviewBackdropMode _backdrop;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PreviewScenario Scenario
    {
        get => _scenario;
        set => Set(ref _scenario, value);
    }

    public PreviewBackdropMode Backdrop
    {
        get => _backdrop;
        set => Set(ref _backdrop, value);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>What the preview is shown over. A translucent widget has to hold up over all three: a
/// dark track, a bright sky, and the checkerboard that shows exactly how see-through it is.</summary>
public enum PreviewBackdropMode
{
    Circuit,
    Sky,
    Checkerboard,
}
