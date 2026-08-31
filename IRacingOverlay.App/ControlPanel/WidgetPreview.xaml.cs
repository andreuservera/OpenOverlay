using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;
using IRacingOverlay.App.Widgets;

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
        FuelCalculatorOptions fuelCalculatorOptions)
    {
        _standingsOptions = standingsOptions;
        _relativeOptions = relativeOptions;
        _fuelCalculatorOptions = fuelCalculatorOptions;

        standingsOptions.PropertyChanged += OnOptionsChanged;
        relativeOptions.PropertyChanged += OnOptionsChanged;
        fuelCalculatorOptions.PropertyChanged += OnOptionsChanged;
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
        _panel = _slot is null ? null : CreatePanel(_slot.Key);

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
    /// is to answer "what will this look like while I'm racing", and that is the locked state.</summary>
    private void ApplyOpacity() => Stage.Opacity = _slot?.Opacity ?? 1.0;

    private UIElement? CreatePanel(string key) => key switch
    {
        WidgetCatalog.Relative => new RelativePanel { Options = _relativeOptions ?? new DriverTableOptions(DriverTable.Relative) },
        WidgetCatalog.Standings => new StandingsPanel { Options = _standingsOptions ?? new DriverTableOptions(DriverTable.Standings) },
        WidgetCatalog.Cockpit => new CockpitPanel(),
        WidgetCatalog.Flag => new FlagPanel(),
        WidgetCatalog.TireInfo => new TireInfoPanel(),
        WidgetCatalog.Delta => new DeltaPanel(),
        WidgetCatalog.Fuel => new FuelPanel(),
        WidgetCatalog.PedalTrace => new PedalTracePanel(),
        WidgetCatalog.Incident => new IncidentPanel(),
        WidgetCatalog.TrackInfo => new TrackInfoPanel(),
        WidgetCatalog.TrackMap => new TrackMapPanel(),
        WidgetCatalog.FuelCalculator => new FuelCalculatorPanel { Options = _fuelCalculatorOptions ?? new FuelCalculatorOptions() },
        _ => null,
    };

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

    private void PushMockState()
    {
        switch (_panel)
        {
            case StandingsPanel standings:
                {
                    var options = _standingsOptions;
                    var field = PreviewData.StandingsField(options?.ShowMulticlass == true);
                    var focusSize = options?.FocusSize ?? DriverTableOptions.DefaultStandingsFocusSize;
                    standings.SetRows(options?.ShowMulticlass == true
                        ? StandingsBuilder.BuildMulticlassView(field, focusSize)
                        : StandingsBuilder.BuildFocusedView(field, focusSize));
                    standings.SetSof(PreviewData.StrengthOfField());
                    standings.SetCarName(PreviewData.CarName);
                    standings.SetSessionId(PreviewData.SubSessionId);
                    break;
                }

            case RelativePanel relative:
                {
                    var focusSize = _relativeOptions?.FocusSize ?? DriverTableOptions.DefaultRelativeFocusSize;
                    relative.SetRows(PreviewData.RelativeRows(focusSize));
                    relative.SetCarName(PreviewData.CarName);
                    relative.SetSessionId(PreviewData.SubSessionId);
                    break;
                }

            case CockpitPanel cockpit:
                cockpit.UpdateState(PreviewData.Cockpit());
                break;
            case FlagPanel flag:
                flag.UpdateState(PreviewData.Flags());
                break;
            case TireInfoPanel tires:
                tires.UpdateState(PreviewData.Tires());
                break;
            case DeltaPanel delta:
                delta.UpdateState(PreviewData.Delta());
                break;
            case FuelPanel fuel:
                fuel.UpdateState(PreviewData.Fuel());
                break;
            case PedalTracePanel pedals:
                pedals.UpdateState(PreviewData.PedalTrace());
                break;
            case IncidentPanel incidents:
                incidents.UpdateState(PreviewData.Incidents());
                break;
            case TrackInfoPanel trackInfo:
                trackInfo.UpdateState(PreviewData.TrackInfo());
                break;
            case TrackMapPanel trackMap:
                trackMap.UpdateState(PreviewData.TrackMap());
                break;
            case FuelCalculatorPanel fuelCalculator:
                fuelCalculator.UpdateState(PreviewData.FuelCalculator());
                break;
        }
    }
}
