using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IRacingOverlay.App.ViewModels;

public enum FlagDisplayMode
{
    IconAndText,
    IconOnly,
}

public enum FlagLayout
{
    Vertical,
    Horizontal,
}

public enum FlagIconPlacement
{
    Left,
    Top,
}

/// <summary>
/// How the flag widget behaves and looks. One instance is shared by the widget, the dashboard and
/// the control-panel preview, so a change reaches all three through their bindings.
/// </summary>
public sealed class FlagOptions : INotifyPropertyChanged
{
    public const int MaxFlagsLimit = 6;

    /// <summary>Raised as the property name when any flag type is switched on or off.</summary>
    public const string EnabledKindsChanged = "EnabledKinds";

    /// <summary>Hold times offered for informational flags, in seconds; 0 keeps them up while active.</summary>
    public static IReadOnlyList<int> HoldChoices { get; } = [0, 3, 5, 10, 30];

    // Only explicit choices are stored, so a flag added in a later version starts at its own default.
    private readonly Dictionary<FlagKind, bool> _enabledOverrides = [];

    private FlagDisplayMode _displayMode = FlagDisplayMode.IconAndText;
    private bool _showName = true;
    private bool _showDescription = true;
    private FlagLayout _layout = FlagLayout.Vertical;
    private FlagIconPlacement _iconPlacement = FlagIconPlacement.Left;
    private int _maxFlags = 3;
    private int _infoFlagSeconds = 5;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyDictionary<FlagKind, bool> EnabledOverrides => _enabledOverrides;

    public bool IsEnabled(FlagKind kind) =>
        _enabledOverrides.TryGetValue(kind, out var enabled) ? enabled : FlagCatalog.Get(kind).EnabledByDefault;

    public void SetEnabled(FlagKind kind, bool enabled)
    {
        if (IsEnabled(kind) == enabled)
        {
            return;
        }

        _enabledOverrides[kind] = enabled;
        OnPropertyChanged(EnabledKindsChanged);
    }

    public FlagDisplayMode DisplayMode
    {
        get => _displayMode;
        set
        {
            if (Set(ref _displayMode, value))
            {
                OnTextVisibilityChanged();
            }
        }
    }

    public bool ShowName
    {
        get => _showName;
        set
        {
            if (Set(ref _showName, value))
            {
                OnTextVisibilityChanged();
            }
        }
    }

    public bool ShowDescription
    {
        get => _showDescription;
        set
        {
            if (Set(ref _showDescription, value))
            {
                OnTextVisibilityChanged();
            }
        }
    }

    public FlagLayout Layout
    {
        get => _layout;
        set
        {
            if (Set(ref _layout, value))
            {
                OnPropertyChanged(nameof(IsHorizontal));
            }
        }
    }

    public FlagIconPlacement IconPlacement
    {
        get => _iconPlacement;
        set
        {
            if (Set(ref _iconPlacement, value))
            {
                OnPropertyChanged(nameof(IconOnTop));
            }
        }
    }

    public int MaxFlags
    {
        get => _maxFlags;
        set => Set(ref _maxFlags, Math.Clamp(value, 1, MaxFlagsLimit));
    }

    public int InfoFlagSeconds
    {
        get => _infoFlagSeconds;
        set => Set(ref _infoFlagSeconds, Math.Max(0, value));
    }

    public TimeSpan InfoFlagHold => TimeSpan.FromSeconds(_infoFlagSeconds);

    // Derived flags the panel's bindings read directly.
    public bool ShowText => _displayMode == FlagDisplayMode.IconAndText && (_showName || _showDescription);
    public bool ShowNameText => ShowText && _showName;
    public bool ShowDescriptionText => ShowText && _showDescription;
    public bool IsHorizontal => _layout == FlagLayout.Horizontal;
    public bool IconOnTop => _iconPlacement == FlagIconPlacement.Top;

    private void OnTextVisibilityChanged()
    {
        OnPropertyChanged(nameof(ShowText));
        OnPropertyChanged(nameof(ShowNameText));
        OnPropertyChanged(nameof(ShowDescriptionText));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
