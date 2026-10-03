using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.Layouts;

/// <summary>The monitor a layout was designed for, identified as stably as Windows allows: the
/// monitor's device path survives reboots and driver updates while it stays on the same connector,
/// and the EDID id (manufacturer + product, e.g. "PHLC347") still recognises it after a port swap.
/// The name and resolution are what the user saw when picking it.</summary>
public sealed record MonitorRef(string DevicePath, string? EdidId, string FriendlyName, int Width, int Height);

/// <summary>A layout's entry for one widget type: where that widget goes, how big, and how it is
/// configured while the layout is open. A layout holds at most one per type, because opening it
/// takes over the existing individual widget rather than creating a window of its own.</summary>
public sealed class LayoutWidget
{
    /// <summary>The <see cref="WidgetCatalog"/> key. Fixed once created: changing it would let a
    /// layout end up with two entries of the same type behind the rules' back.</summary>
    public string Type { get; init; } = "";

    /// <summary>Left edge, in pixels of the layout's resolution.</summary>
    public double X { get; set; }

    /// <summary>Top edge, in pixels of the layout's resolution.</summary>
    public double Y { get; set; }

    public ScaleLevel Scale { get; set; } = ScaleLevels.Default;

    /// <summary>As measured in the editor's preview. Informational only: a widget's size follows
    /// its content and <see cref="Scale"/>, and a table's height changes with the data.</summary>
    public double Width { get; set; }

    /// <summary>See <see cref="Width"/>.</summary>
    public double Height { get; set; }

    public int ZIndex { get; set; }

    /// <summary>False keeps the entry in the layout without taking over the individual widget.</summary>
    public bool Visible { get; set; } = true;

    public bool Locked { get; set; }

    public double Opacity { get; set; } = WidgetOpacityStore.Default;

    public bool HideOutsideCar { get; set; }

    /// <summary>The widget's own options, in the shape its <c>IWidgetConfigCodec</c> reads and writes.</summary>
    public JsonObject Config { get; set; } = [];

    /// <summary>Set on import when sensitive fields were left empty and need filling in.</summary>
    public bool RequiresConfiguration { get; set; }

    public LayoutWidget Clone() => new()
    {
        Type = Type,
        X = X,
        Y = Y,
        Scale = Scale,
        Width = Width,
        Height = Height,
        ZIndex = ZIndex,
        Visible = Visible,
        Locked = Locked,
        Opacity = Opacity,
        HideOutsideCar = HideOutsideCar,
        Config = (JsonObject)Config.DeepClone(),
        RequiresConfiguration = RequiresConfiguration,
    };
}

/// <summary>
/// A saved preset of the individual widgets: which of them to show, where, how big and how
/// configured, designed on a canvas the size of one monitor.
///
/// The rules that keep a layout valid live here rather than in whatever screen edits it, so the
/// editor, import and anything else that builds one cannot disagree about them: at most one entry
/// per widget type, only types the catalog knows, and a resolution between
/// <see cref="MinResolution"/> and <see cref="MaxResolution"/>. Breaking one throws
/// <see cref="LayoutRuleException"/> with a message fit to show the user.
/// </summary>
public sealed class Layout
{
    public const int MinResolution = 640;
    public const int MaxResolution = 7680;

    private List<LayoutWidget> _widgets = [];

    /// <summary>For the serializer only; code creates layouts through <see cref="Layout(string, MonitorRef, int, int)"/>.</summary>
    public Layout()
    {
    }

    public Layout(string name, MonitorRef monitor, int width, int height)
    {
        EnsureResolution(width, height);
        Id = Guid.NewGuid();
        Name = name;
        Monitor = monitor;
        Width = width;
        Height = height;
        CreatedUtc = DateTime.UtcNow;
        ModifiedUtc = CreatedUtc;
    }

    /// <summary>How many widgets a layout can hold: one of each type the application has. Grows on
    /// its own as widgets are added to the catalog.</summary>
    public static int MaxWidgets => WidgetCatalog.All.Count;

    [JsonInclude]
    public Guid Id { get; private set; }

    public string Name { get; set; } = "";

    [JsonInclude]
    public DateTime CreatedUtc { get; private set; }

    public DateTime ModifiedUtc { get; set; }

    public MonitorRef Monitor { get; set; } = new("", null, "", MinResolution, MinResolution);

    [JsonInclude]
    public int Width { get; private set; }

    [JsonInclude]
    public int Height { get; private set; }

    public bool SnapEnabled { get; set; } = true;

    public int GridSize { get; set; } = 10;

    [JsonInclude]
    public IReadOnlyList<LayoutWidget> Widgets
    {
        get => _widgets;
        private set => _widgets = [.. value];
    }

    public static bool IsValidResolution(int width, int height) =>
        width is >= MinResolution and <= MaxResolution && height is >= MinResolution and <= MaxResolution;

    public void SetResolution(int width, int height)
    {
        EnsureResolution(width, height);
        Width = width;
        Height = height;
    }

    public bool Contains(string type) => _widgets.Any(widget => widget.Type == type);

    public LayoutWidget? WidgetOf(string type) => _widgets.FirstOrDefault(widget => widget.Type == type);

    /// <summary>Adds an entry, enforcing every rule about what a layout may contain.</summary>
    public void Add(LayoutWidget widget)
    {
        if (!WidgetCatalog.All.Any(descriptor => descriptor.Key == widget.Type))
        {
            throw new LayoutRuleException($"\"{widget.Type}\" is not a widget this version of the app knows.");
        }

        if (Contains(widget.Type))
        {
            throw new LayoutRuleException($"The layout already has the {NameOf(widget.Type)} widget. Each widget can be added once.");
        }

        if (_widgets.Count >= MaxWidgets)
        {
            throw new LayoutRuleException($"A layout can hold at most {MaxWidgets} widgets, one of each.");
        }

        _widgets.Add(widget);
    }

    public bool Remove(string type) => _widgets.RemoveAll(widget => widget.Type == type) > 0;

    /// <summary>A deep copy, Config included, keeping the same <see cref="Id"/>. Store reads and
    /// writes go through copies, so an editor's working copy never changes what is saved until it
    /// is saved on purpose.</summary>
    public Layout Clone() => CloneAs(Id, Name, CreatedUtc);

    /// <summary>A deep copy that is a new layout: new id, new name, created now.</summary>
    public Layout Duplicate(string name)
    {
        var copy = CloneAs(Guid.NewGuid(), name, DateTime.UtcNow);
        copy.ModifiedUtc = copy.CreatedUtc;
        return copy;
    }

    /// <summary>
    /// Brings a layout read from disk back within the rules, dropping what cannot be kept and saying
    /// why: entries of a type this version doesn't know (a file written by a newer version) and any
    /// repeat of a type, of which the first wins. A resolution out of range is clamped. Returns the
    /// problems found, empty for a valid layout.
    /// </summary>
    internal IReadOnlyList<string> Normalize()
    {
        var problems = new List<string>();
        var kept = new List<LayoutWidget>();
        foreach (var widget in _widgets)
        {
            if (!WidgetCatalog.All.Any(descriptor => descriptor.Key == widget.Type))
            {
                problems.Add($"dropped unknown widget type \"{widget.Type}\"");
            }
            else if (kept.Any(other => other.Type == widget.Type))
            {
                problems.Add($"dropped a repeated {widget.Type} widget");
            }
            else
            {
                widget.Config ??= [];
                kept.Add(widget);
            }
        }

        _widgets = kept;

        if (!IsValidResolution(Width, Height))
        {
            problems.Add($"resolution {Width}x{Height} out of range");
            Width = Math.Clamp(Width, MinResolution, MaxResolution);
            Height = Math.Clamp(Height, MinResolution, MaxResolution);
        }

        return problems;
    }

    private Layout CloneAs(Guid id, string name, DateTime createdUtc) => new()
    {
        Id = id,
        Name = name,
        CreatedUtc = createdUtc,
        ModifiedUtc = ModifiedUtc,
        Monitor = Monitor,
        Width = Width,
        Height = Height,
        SnapEnabled = SnapEnabled,
        GridSize = GridSize,
        _widgets = _widgets.Select(widget => widget.Clone()).ToList(),
    };

    private static void EnsureResolution(int width, int height)
    {
        if (!IsValidResolution(width, height))
        {
            throw new LayoutRuleException(
                $"The resolution must be between {MinResolution} and {MaxResolution} pixels on each side; {width}×{height} is not.");
        }
    }

    private static string NameOf(string type) =>
        WidgetCatalog.All.FirstOrDefault(descriptor => descriptor.Key == type)?.Name ?? type;
}

/// <summary>A change that would leave a layout breaking one of its rules. The message is written
/// for the user.</summary>
public sealed class LayoutRuleException(string message) : InvalidOperationException(message);
