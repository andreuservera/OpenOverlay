using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Windows;
using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Layouts;

/// <summary>
/// One widget on the editor's canvas: the real panel, rendered with the layout's own config for that
/// widget, plus everything the canvas draws around it. Bound to the <see cref="LayoutWidget"/> of the
/// editor's current working copy, which undo and redo replace, hence <see cref="Bind"/>.
/// </summary>
public sealed class EditorWidgetItem : INotifyPropertyChanged
{
    private readonly WidgetConfigTargets _targets;
    private readonly IWidgetConfigCodec _codec;
    private LayoutWidget _widget;
    private JsonObject _appliedConfig;
    private bool _isSelected;
    private bool _isOffCanvas;
    private double _canvasScale = 1;
    private bool _refreshing;

    public EditorWidgetItem(LayoutWidget widget)
    {
        _widget = widget;
        Descriptor = WidgetCatalog.All.First(descriptor => descriptor.Key == widget.Type);
        _targets = NewTargets();
        _codec = WidgetConfigCodecs.Create(_targets)[widget.Type];
        _codec.Apply(widget.Config);
        _appliedConfig = (JsonObject)widget.Config.DeepClone();
        Options = new PreviewOptions(
            _targets.Standings,
            _targets.Relative,
            _targets.FuelCalculator,
            _targets.Flag,
            FlagPreview: null,
            _targets.Cockpit,
            _targets.Weather);
        Panel = PanelFactory.Create(widget.Type, Options);
        if (Panel is FrameworkElement element)
        {
            // As the control panel preview does: stretch-to-fill panels need their design size.
            element.MinWidth = Descriptor.PreviewWidth;
            element.MinHeight = Descriptor.PreviewHeight;
            element.Loaded += (_, _) => PushMockState();
            element.SizeChanged += (_, _) => PushMockState();
        }
    }

    public WidgetDescriptor Descriptor { get; }

    public string Type => _widget.Type;

    public UIElement? Panel { get; }

    public PreviewOptions Options { get; }

    public LayoutWidget Widget => _widget;

    public double X => _widget.X;

    public double Y => _widget.Y;

    public int ZIndex => _widget.ZIndex;

    public double Factor => ScaleLevels.FactorOf(_widget.Scale);

    public double PanelOpacity => _widget.Opacity;

    public bool IsLocked => _widget.Locked;

    public bool IsHidden => !_widget.Visible;

    /// <summary>Hidden widgets stay on the canvas, faded, so they can be found and shown again.</summary>
    public double ItemOpacity => _widget.Visible ? 1 : 0.35;

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public bool IsOffCanvas
    {
        get => _isOffCanvas;
        set => Set(ref _isOffCanvas, value);
    }

    public bool ShowHandles => _isSelected && !_widget.Locked;

    /// <summary>Handles and outlines are sized in screen pixels, so they stay grabbable and thin at
    /// any zoom: these are those sizes in canvas units.</summary>
    public double HandleSize => 10 / _canvasScale;

    /// <summary>Pulls each corner handle half outside the widget, whichever corner it sits in.</summary>
    public Thickness HandleMargin => new(-5 / _canvasScale);

    public double OutlineThickness => (_isSelected ? 2 : 1) / _canvasScale;

    public double BadgeScale => 1 / _canvasScale;

    public double CanvasScale
    {
        get => _canvasScale;
        set
        {
            if (Set(ref _canvasScale, value))
            {
                OnPropertyChanged(nameof(HandleSize));
                OnPropertyChanged(nameof(HandleMargin));
                OnPropertyChanged(nameof(OutlineThickness));
                OnPropertyChanged(nameof(BadgeScale));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Points the item at the same widget in a new working copy (after undo or redo) and
    /// refreshes everything drawn from it.</summary>
    public void Bind(LayoutWidget widget)
    {
        _widget = widget;
        if (!JsonNode.DeepEquals(widget.Config, _appliedConfig))
        {
            // Undo or redo brought back a different config: the preview follows it.
            _codec.Apply(widget.Config);
            _appliedConfig = (JsonObject)widget.Config.DeepClone();
            PushMockState();
        }

        OnPropertyChanged(string.Empty);
    }

    /// <summary>Settings for this widget's own options, acting on the very objects its preview is
    /// drawn from, so a change shows at once. Nothing is saved to the widget's stores:
    /// <paramref name="changed"/> is where the editor records it in the layout.</summary>
    public WidgetSettingsContext SettingsContext(Action changed) => new(
        _targets.Standings,
        _targets.Relative,
        _targets.FuelCalculator,
        _targets.Flag,
        _targets.Cockpit,
        _targets.Weather,
        _targets.Delta,
        SaveToStores: false,
        TableHeaderChanged: null,
        Changed: changed);

    /// <summary>The options as the settings have left them, in the layout's config shape. Also
    /// refreshes the preview's sample data, which some options (row counts) change the amount of.</summary>
    public JsonObject ReadConfig()
    {
        var config = _codec.Read();
        _appliedConfig = (JsonObject)config.DeepClone();
        PushMockState();
        return config;
    }

    private void PushMockState()
    {
        // Pushing data resizes the panel, which lands back here; one pass is enough.
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            PanelFactory.PushMockState(Panel, Options);
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>Fresh options objects for this widget's preview and settings — never the live
    /// ones — with a persistence that saves nowhere.</summary>
    private static WidgetConfigTargets NewTargets() => new(
        new DriverTableOptions(DriverTable.Standings),
        new DriverTableOptions(DriverTable.Relative),
        new FlagOptions(),
        new CockpitOptions(),
        new WeatherOptions(),
        new FuelCalculatorOptions(),
        new DeltaOptions(),
        new WidgetConfigPersistence(_ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }),
        _ => { });

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName == nameof(IsSelected))
        {
            OnPropertyChanged(nameof(ShowHandles));
            OnPropertyChanged(nameof(OutlineThickness));
        }

        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>A widget in the editor's catalog, and whether it can still be added.</summary>
public sealed record CatalogEntry(WidgetDescriptor Descriptor, bool IsAvailable)
{
    public string Status => IsAvailable ? "" : "IN LAYOUT";

    public double EntryOpacity => IsAvailable ? 1 : 0.45;

    public string Hint => IsAvailable
        ? "Drag onto the screen, or double-click to add."
        : "Already in this layout. Use the bin to remove it.";
}
