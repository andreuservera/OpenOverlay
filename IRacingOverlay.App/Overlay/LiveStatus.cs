namespace IRacingOverlay.App.Overlay;

/// <summary>Whether the app is reading iRacing right now: the green dot in every widget header.
/// Static, with WPF's static change notification, so the headers bind to it directly instead of
/// each window being told.</summary>
public static class LiveStatus
{
    private static bool _isLive;

    public static event EventHandler<System.ComponentModel.PropertyChangedEventArgs>? StaticPropertyChanged;

    public static bool IsLive
    {
        get => _isLive;
        set
        {
            if (_isLive == value)
            {
                return;
            }

            _isLive = value;
            StaticPropertyChanged?.Invoke(null, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsLive)));
        }
    }
}
