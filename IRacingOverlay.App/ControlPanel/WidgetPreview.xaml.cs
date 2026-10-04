using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// The live preview pane.
///
/// It renders the widget by instantiating the *production* panel control and feeding it mock state
/// through the same UpdateState/SetRows calls the telemetry loop uses. Nothing about the widget is
/// re-implemented here, which is the only way a preview stays honest: a hand-drawn mock-up is
/// another copy of the layout to keep in step, and it starts lying the first time someone changes a
/// column width and forgets it exists.
///
/// It also binds the panel to the very same options objects the live widget uses, so a column
/// switched off in the configuration pane disappears here through the panel's own bindings, with no
/// refresh path in between. Only settings that change *how much data there is* — row counts, the
/// multiclass split — need a rebuild, and those come in over PropertyChanged.
/// </summary>
public partial class WidgetPreview : UserControl
{
    private DriverTableOptions? _standingsOptions;
    private DriverTableOptions? _relativeOptions;
    private FuelCalculatorOptions? _fuelCalculatorOptions;
    private FlagOptions? _flagOptions;
    private FlagPreviewScenario? _flagPreview;
    private CockpitOptions? _cockpitOptions;
    private WeatherOptions? _weatherOptions;

    private WidgetSlot? _slot;
    private UIElement? _panel;
    private bool _refreshing;

    public WidgetPreview()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty SlotProperty = DependencyProperty.Register(
        nameof(Slot), typeof(WidgetSlot), typeof(WidgetPreview),
        new PropertyMetadata(null, OnSlotChanged));

    /// <summary>Which widget to preview. Null (an application-level page is selected) empties the
    /// stage rather than leaving the previous widget stranded on it.</summary>
    public WidgetSlot? Slot
    {
        get => (WidgetSlot?)GetValue(SlotProperty);
        set => SetValue(SlotProperty, value);
    }

    /// <summary>Hands the preview the same options instances the live widgets were given. Called
    /// once at startup; the preview then follows them for as long as it exists.</summary>
    public void Bind(
        DriverTableOptions standingsOptions,
        DriverTableOptions relativeOptions,
        FuelCalculatorOptions fuelCalculatorOptions,
        FlagOptions flagOptions,
        FlagPreviewScenario flagPreview,
        CockpitOptions cockpitOptions,
        WeatherOptions weatherOptions)
    {
        _standingsOptions = standingsOptions;
        _relativeOptions = relativeOptions;
        _fuelCalculatorOptions = fuelCalculatorOptions;
        _flagOptions = flagOptions;
        _flagPreview = flagPreview;
        _cockpitOptions = cockpitOptions;
        // The weather panel follows its options itself, so no rebuild subscription is needed.
        _weatherOptions = weatherOptions;
        // Previews show sample data in whatever units iRacing last reported.
        Units.CurrentChanged += Refresh;

        standingsOptions.PropertyChanged += OnOptionsChanged;
        relativeOptions.PropertyChanged += OnOptionsChanged;
        fuelCalculatorOptions.PropertyChanged += OnOptionsChanged;
        flagOptions.PropertyChanged += OnOptionsChanged;
        flagPreview.PropertyChanged += OnOptionsChanged;
    }

    private static void OnSlotChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var preview = (WidgetPreview)d;

        if (e.OldValue is WidgetSlot previous)
        {
            previous.PropertyChanged -= preview.OnSlotPropertyChanged;
        }

        preview._slot = e.NewValue as WidgetSlot;
        if (preview._slot is not null)
        {
            preview._slot.PropertyChanged += preview.OnSlotPropertyChanged;
        }

        preview.Rebuild();
    }

    private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(WidgetSlot.Scale):
                ApplyScale();
                break;
            case nameof(WidgetSlot.Opacity):
                ApplyOpacity();
                break;
        }
    }

    private void OnOptionsChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    /// <summary>Panels that draw into a canvas (the trace, the map, the wear bars) read their own
    /// ActualWidth when updating, so their first paint has to wait until they have been measured.
    /// Re-pushing on size change covers that without a polling timer.</summary>
    private void Stage_SizeChanged(object sender, SizeChangedEventArgs e) => Refresh();

    private void Rebuild()
    {
        _panel = _slot is null ? null : PanelFactory.Create(_slot.Key, Options);

        if (_panel is FrameworkElement element && _slot is not null)
        {
            // Mirrors the DesignWidth/DesignHeight the widget's own ScalablePanel declares, for the
            // panels that stretch to fill and would otherwise collapse to their longest label.
            element.MinWidth = _slot.Descriptor.PreviewWidth;
            element.MinHeight = _slot.Descriptor.PreviewHeight;
        }

        Stage.Content = _panel;
        Backdrop.Visibility = _panel is null ? Visibility.Collapsed : Visibility.Visible;
        ApplyScale();
        ApplyOpacity();
        Refresh();
    }

    private void ApplyScale()
    {
        var factor = ScaleLevels.FactorOf(_slot?.Scale ?? ScaleLevels.Default);
        StageScale.ScaleX = factor;
        StageScale.ScaleY = factor;
    }

    /// <summary>The configured value, not the edit-mode floor the window applies: the preview's job
    /// is to answer "what will this look like while I'm racing", and that is the locked state. Applied
    /// the way the window applies it: to the background only, unless the widget has none of its own.</summary>
    private void ApplyOpacity()
    {
        var opacity = _slot?.Opacity ?? 1.0;
        var whole = _slot is { } slot && WidgetCatalog.FadesWholeWidget(slot.Key);
        Stage.Opacity = whole ? opacity : 1.0;
        BackgroundOpacity.SetValue(Stage, whole ? 1.0 : opacity);
    }

    /// <summary>Pushes the mock state into whatever panel is on the stage. Cheap enough to call on
    /// every option change: it is one pass over at most a couple of dozen rows, against a visual
    /// tree that is already built.</summary>
    private void Refresh()
    {
        // Pushing rows changes the panel's size, which raises SizeChanged, which lands back here.
        // One pass is all that's needed; the guard stops the second one from bouncing.
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            PushMockState();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void PushMockState() => PanelFactory.PushMockState(_panel, Options);

    private PreviewOptions Options => new(
        _standingsOptions,
        _relativeOptions,
        _fuelCalculatorOptions,
        _flagOptions,
        _flagPreview,
        _cockpitOptions,
        _weatherOptions);
}
