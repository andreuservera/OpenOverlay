using System.ComponentModel;
using System.Runtime.CompilerServices;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// One widget's live state, in one place: is it on, does it duck out of view when you're not
/// driving, how big is it, and the window itself once it has been created.
///
/// This is the whole reason the control panel has no per-widget handlers left. Previously each of
/// the twelve widgets needed its own checkbox field, its own Checked/Unchecked handler and its own
/// line in every loop that touched "all widgets" — three places to forget when adding the
/// thirteenth. Now the same twenty lines run for every widget, and the only per-widget code left in
/// the application is the factory that constructs it.
///
/// Windows are created lazily and then hidden rather than closed, matching the previous behaviour:
/// closing would discard the position the user placed it at.
/// </summary>
public sealed class WidgetSlot : INotifyPropertyChanged
{
    private readonly Func<OverlayWindowBase> _factory;
    private OverlayWindowBase? _window;
    private bool _isEnabled;
    private bool _hideOutsideCar;
    private ScaleLevel _scale;
    private double _opacity;
    private bool _isEditMode;
    private bool _isDriving;

    public WidgetSlot(WidgetDescriptor descriptor, Func<OverlayWindowBase> factory)
    {
        Descriptor = descriptor;
        _factory = factory;

        // Restored straight from the stores rather than mirrored out of UI controls, so the saved
        // state is the source of truth and there is no window in which a control's default value
        // could be written back over it.
        _isEnabled = WidgetVisibilityStore.Get(descriptor.Key);
        _hideOutsideCar = HideOutsideCarStore.Get(descriptor.Key);
        _scale = ScaleLevelStore.Get(descriptor.ScaleKey);
        _opacity = WidgetOpacityStore.Get(descriptor.Key);
    }

    public WidgetDescriptor Descriptor { get; }

    public string Key => Descriptor.Key;

    /// <summary>The window, or null while the widget has never been switched on this run.</summary>
    public OverlayWindowBase? Window => _window;

    public T? WindowAs<T>() where T : OverlayWindowBase => _window as T;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            _isEnabled = value;
            WidgetVisibilityStore.Save(Key, value);
            Apply();
            OnPropertyChanged();
        }
    }

    public bool HideOutsideCar
    {
        get => _hideOutsideCar;
        set
        {
            if (_hideOutsideCar == value)
            {
                return;
            }

            _hideOutsideCar = value;
            HideOutsideCarStore.Save(Key, value);
            Apply();
            OnPropertyChanged();
        }
    }

    /// <summary>Whether the player is at the wheel right now, pushed in by the telemetry loop. Held
    /// on the slot rather than read at each Show/Hide site so that <see cref="Apply"/> is the only
    /// place that decides whether a widget belongs on screen — the previous split, where the loop
    /// hid widgets that Apply had just shown, is what made one flash on screen at startup and
    /// whenever it was switched on from the menus.</summary>
    public bool IsDriving
    {
        get => _isDriving;
        set
        {
            if (_isDriving == value)
            {
                return;
            }

            _isDriving = value;
            Apply();
        }
    }

    public ScaleLevel Scale
    {
        get => _scale;
        set
        {
            if (_scale == value)
            {
                return;
            }

            _scale = value;
            // Store first: a widget that hasn't been shown yet has no ScalablePanel to tell, and
            // reads its level back out of the store the moment it loads.
            ScaleLevelStore.Save(Descriptor.ScaleKey, value);
            if (_window is not null)
            {
                _window.ScaleLevel = value;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>How solid the widget is on screen, 0 to 1. Persisted here as well as by the window
    /// itself so the slider shows the right value for a widget that has never been switched on this
    /// run and therefore has no window to ask.</summary>
    public double Opacity
    {
        get => _opacity;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (_opacity.Equals(clamped))
            {
                return;
            }

            _opacity = clamped;
            if (_window is not null)
            {
                // The window persists it on assignment; writing the store here as well would be the
                // same value twice.
                _window.WidgetOpacity = clamped;
            }
            else
            {
                WidgetOpacityStore.Save(Key, clamped);
            }

            OnPropertyChanged();
        }
    }

    /// <summary>Layout-editing mode, pushed down to the window when it exists and remembered so a
    /// widget switched on later starts in the same mode as the ones already on screen.</summary>
    public bool IsEditMode
    {
        get => _isEditMode;
        set
        {
            if (_isEditMode == value)
            {
                return;
            }

            _isEditMode = value;
            if (_window is not null)
            {
                _window.IsEditMode = value;
            }

            // Editing the layout has to be able to reach a widget that auto-hide is currently
            // suppressing — you cannot place something you cannot see.
            Apply();
        }
    }

    /// <summary>What the rail says under the widget's name. Three states rather than two, because
    /// "on, but currently hidden because you aren't driving" is the one a user would otherwise
    /// report as a bug.</summary>
    public string StateLabel => !_isEnabled
        ? "HIDDEN"
        : ShouldBeOnScreen ? "VISIBLE" : "WAITING FOR CAR";

    /// <summary>True when the widget is actually on screen right now, as opposed to merely switched
    /// on. Drives the rail's live dot.</summary>
    public bool IsOnScreen => ShouldBeOnScreen;

    /// <summary>Auto-hide is suspended while the layout is being edited: it is a driving-time
    /// convenience, and nobody laying widgets out from the menus wants them vanishing.</summary>
    private bool ShouldBeOnScreen =>
        _isEnabled && (_isDriving || _isEditMode || !_hideOutsideCar);

    /// <summary>The single place that decides whether this widget is on screen, and the only one
    /// that creates its window. Creation is deferred until it would actually be shown, so switching
    /// a widget on from the menus with auto-hide enabled costs nothing and shows nothing.</summary>
    public void Apply()
    {
        if (ShouldBeOnScreen)
        {
            if (_window is null)
            {
                _window = _factory();
                // Assigned before Show so the widget never appears on screen in the wrong mode.
                _window.IsEditMode = _isEditMode;
            }

            _window.Show();
        }
        else
        {
            _window?.Hide();
        }

        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(IsOnScreen));
    }

    /// <summary>Picks up a size the user changed on the widget itself, via its own +/- control or
    /// Ctrl+wheel, so the control panel's selector never shows a stale level.</summary>
    public void RefreshScaleFromWidget()
    {
        var current = _window?.ScaleLevel ?? ScaleLevelStore.Get(Descriptor.ScaleKey);
        if (current == _scale)
        {
            return;
        }

        _scale = current;
        OnPropertyChanged(nameof(Scale));
    }

    public void Close() => _window?.Close();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
