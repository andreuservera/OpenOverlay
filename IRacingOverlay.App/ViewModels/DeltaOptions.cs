using System.ComponentModel;

namespace IRacingOverlay.App.ViewModels;

/// <summary>The Delta widget's options: which lap it measures against. Never saved for the
/// individual widget — it starts every run at the session best — but a layout keeps its own, and
/// applies it while the layout is open.</summary>
public sealed class DeltaOptions : INotifyPropertyChanged
{
    private DeltaReference _reference = DeltaReference.SessionBest;

    public event PropertyChangedEventHandler? PropertyChanged;

    public DeltaReference Reference
    {
        get => _reference;
        set
        {
            if (_reference == value)
            {
                return;
            }

            _reference = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Reference)));
        }
    }
}
