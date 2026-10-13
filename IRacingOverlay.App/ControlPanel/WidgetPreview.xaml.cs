using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    private TrackInfoOptions? _trackInfoOptions;
    private PedalTraceOptions? _pedalTraceOptions;
    private PreviewSettings? _settings;

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
        WeatherOptions weatherOptions,
        TrackInfoOptions trackInfoOptions,
        PedalTraceOptions pedalTraceOptions,
        PreviewSettings settings)
    {
        _settings = settings;
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PreviewSettings.Backdrop))
            {
                Backdrop.Mode = settings.Backdrop;
            }
            else
            {
                Refresh();
            }
        };
        _standingsOptions = standingsOptions;
        _relativeOptions = relativeOptions;
        _fuelCalculatorOptions = fuelCalculatorOptions;
        _flagOptions = flagOptions;
        _flagPreview = flagPreview;
        _cockpitOptions = cockpitOptions;
        // The weather panel follows its options itself, so no rebuild subscription is needed.
        _weatherOptions = weatherOptions;
        _trackInfoOptions = trackInfoOptions;
        // The pedal panel lays itself out again from its options, like the track info bar.
        _pedalTraceOptions = pedalTraceOptions;
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
            case nameof(WidgetSlot.ShowHeader):
                ApplyHeader();
                break;
        }
    }

    private void OnOptionsChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    public static readonly DependencyProperty FitCaptionProperty = DependencyProperty.Register(
        nameof(FitCaption), typeof(string), typeof(WidgetPreview), new PropertyMetadata(Loc.F("preview.fitScaled", 100)));

    /// <summary>The fit toggle's caption: "FIT", "FIT · 74 %" when the widget is shown smaller
    /// than it will be, or "ACTUAL SIZE".</summary>
    public string FitCaption
    {
        get => (string)GetValue(FitCaptionProperty);
        private set => SetValue(FitCaptionProperty, value);
    }

    private bool ShowsActualSize => ActualSizeToggle.IsChecked == true;

    private void ActualSizeToggle_Changed(object sender, RoutedEventArgs e)
    {
        var actual = ShowsActualSize;
        Fitter.Stretch = actual ? Stretch.None : Stretch.Uniform;
        Viewport.HorizontalScrollBarVisibility = actual ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        Viewport.VerticalScrollBarVisibility = actual ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        UpdateFitCaption();
    }

    private void Fitter_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateFitCaption();

    private void UpdateFitCaption()
    {
        if (ShowsActualSize)
        {
            FitCaption = Loc.T("preview.actualSize");
            return;
        }

        // The Viewbox's size against the stage's own (ladder-scaled) footprint is how far it shrank it.
        var natural = Stage.DesiredSize.Width;
        var shown = Fitter.ActualWidth;
        var factor = natural > 0 && shown > 0 ? shown / natural : 1;
        // Always with the figure, 100 % included, so the caption reads the same whether it shrank or not.
        FitCaption = Loc.F("preview.fitScaled", Math.Min(100, Math.Round(factor * 100)));
    }

    /// <summary>Panels that draw into a canvas (the trace, the map, the wear bars) read their own
    /// ActualWidth when updating, so their first paint has to wait until they have been measured.
    /// Re-pushing on size change covers that without a polling timer.</summary>
    private void Stage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Refresh();
        UpdateFitCaption();
    }

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
        ApplyHeader();
        Refresh();
    }

    private void ApplyScale()
    {
        var factor = (_slot?.Scale ?? WidgetScale.Default).Factor;
        StageScale.ScaleX = factor;
        StageScale.ScaleY = factor;
    }

    /// <summary>The configured value, not the edit-mode floor the window applies: the preview's job
    /// is to answer "what will this look like while I'm racing", and that is the locked state. Applied
    /// the way the window applies it: to the background only.</summary>
    private void ApplyOpacity() => BackgroundOpacity.SetValue(Stage, _slot?.Opacity ?? 1.0);

    private void ApplyHeader() => WidgetFrame.SetShowHeader(Stage, _slot?.ShowHeader ?? WidgetHeaderStore.Default);

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
        _weatherOptions,
        _trackInfoOptions,
        _pedalTraceOptions,
        _settings?.Scenario ?? PreviewScenario.Race);
}
