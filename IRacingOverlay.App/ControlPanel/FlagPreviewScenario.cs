using System.ComponentModel;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>The flag situation the preview is showing, picked from <see cref="PreviewData.FlagScenarios"/>.</summary>
public sealed class FlagPreviewScenario : INotifyPropertyChanged
{
    private int _index;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index
    {
        get => _index;
        set
        {
            var clamped = Math.Clamp(value, 0, PreviewData.FlagScenarios.Count - 1);
            if (_index == clamped)
            {
                return;
            }

            _index = clamped;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Index)));
        }
    }

    public IReadOnlyList<ActiveFlag> Flags => PreviewData.FlagScenarios[_index].Flags;
}
