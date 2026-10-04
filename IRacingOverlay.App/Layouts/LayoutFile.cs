using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.Layouts;

/// <summary>What reading a layout file gave: a layout to save, with anything left out on the way
/// in <see cref="Warnings"/>, or an <see cref="Error"/> and no layout at all. Messages are written
/// for the user.</summary>
public sealed record LayoutImport(Layout? Layout, IReadOnlyList<string> Warnings, string? Error)
{
    public static LayoutImport Failed(string error) => new(null, [], error);
}

/// <summary>
/// The <c>*.layout.json</c> exchange format, version 1, documented as a JSON Schema in
/// <c>docs/specs/layout.schema.json</c>. Separate from <see cref="LayoutStore"/>'s own file on
/// purpose: that one can change shape with the app, while a shared file has to keep meaning the
/// same thing to other versions.
///
/// Export leaves out every config key a widget declares sensitive. Import checks the file with
/// rules in code rather than a schema library: a widget type this version doesn't know is skipped
/// with a warning instead of failing the file, and the messages say what is wrong in the user's
/// terms. The layout is then built through <see cref="Layout.Add"/>, so the same rules hold as in
/// the editor.
/// </summary>
public static class LayoutFile
{
    public const int FormatVersion = 1;

    public const string Extension = ".layout.json";

    /// <summary>Far more than any real layout needs (one is a few KB); anything bigger isn't one.</summary>
    public const long MaxFileBytes = 1024 * 1024;

    public const string DialogFilter = "Layout files (*.layout.json)|*.layout.json|JSON files (*.json)|*.json|All files (*.*)|*.*";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The file's JSON. <paramref name="codecs"/> say which config keys are sensitive.</summary>
    public static string Export(Layout layout, IReadOnlyDictionary<string, IWidgetConfigCodec> codecs, string appVersion, DateTime exportedUtc)
    {
        var file = new FileV1(
            FormatVersion,
            new MetadataV1(layout.Name, TruncateToSeconds(exportedUtc), appVersion),
            new MonitorV1(layout.Monitor.DevicePath, layout.Monitor.EdidId, layout.Monitor.FriendlyName, layout.Monitor.Width, layout.Monitor.Height),
            new ResolutionV1(layout.Width, layout.Height),
            layout.Widgets.Select(widget => new WidgetV1(
                widget.Type,
                Finite(widget.X),
                Finite(widget.Y),
                widget.Scale,
                Math.Max(0, Finite(widget.Width)),
                Math.Max(0, Finite(widget.Height)),
                widget.ZIndex,
                widget.Visible,
                widget.Locked,
                Math.Clamp(Finite(widget.Opacity, WidgetOpacityStore.Default), 0, 1),
                widget.HideOutsideCar,
                WithoutSensitive(widget.Config, SensitiveFieldsOf(widget.Type, codecs)))).ToList());

        return JsonSerializer.Serialize(file, JsonOptions);
    }

    /// <summary>A file name for the layout's name: what Windows doesn't allow in one becomes "_".</summary>
    public static string FileNameFor(string layoutName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(layoutName.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return (name.Length == 0 ? LayoutNaming.DefaultName : name) + Extension;
    }

    /// <summary>The name to give a layout whose file doesn't carry one: "Race.layout.json" gives "Race".</summary>
    public static string NameFromFileName(string path)
    {
        var name = Path.GetFileName(path);
        name = name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
            ? name[..^Extension.Length]
            : Path.GetFileNameWithoutExtension(name);
        return string.IsNullOrWhiteSpace(name) ? LayoutNaming.DefaultName : name.Trim();
    }

    /// <summary>
    /// Reads a file's JSON into a new layout (new id, created now) aimed at the monitor and
    /// resolution the file names. Sensitive config keys are dropped even if the file has them, and
    /// their widget is flagged <see cref="LayoutWidget.RequiresConfiguration"/>.
    /// </summary>
    /// <param name="fallbackName">Used when the file doesn't name the layout, e.g. the file's name.</param>
    public static LayoutImport Import(string json, IReadOnlyDictionary<string, IWidgetConfigCodec> codecs, string fallbackName)
    {
        try
        {
            return Read(json, codecs, fallbackName);
        }
        catch (InvalidFileException invalid)
        {
            return LayoutImport.Failed(invalid.Message);
        }
    }

    private static LayoutImport Read(string json, IReadOnlyDictionary<string, IWidgetConfigCodec> codecs, string fallbackName)
    {
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return LayoutImport.Failed("The file is damaged or isn't a layout file: it isn't valid JSON.");
        }

        if (parsed is not JsonObject root || !root.ContainsKey("formatVersion"))
        {
            return LayoutImport.Failed("This isn't a layout file.");
        }

        var version = Integer(root, "formatVersion", "the format version");
        if (version != FormatVersion)
        {
            return LayoutImport.Failed(version > FormatVersion
                ? $"This layout was exported by a newer version of the app (format {version}). Update the app to import it."
                : $"Format {version} isn't a layout format this app knows.");
        }

        var resolution = OptionalObject(root, "resolution", "the resolution") ?? throw Invalid("The file doesn't say the layout's resolution.");
        var width = Integer(resolution, "width", "the resolution's width");
        var height = Integer(resolution, "height", "the resolution's height");
        if (!Layout.IsValidResolution(width, height))
        {
            throw Invalid(string.Create(CultureInfo.InvariantCulture,
                $"The layout's resolution, {width}×{height}, is out of range: each side must be between {Layout.MinResolution} and {Layout.MaxResolution} pixels."));
        }

        var name = OptionalObject(root, "metadata", "the metadata") is { } metadata ? OptionalString(metadata, "name", "the layout's name")?.Trim() : null;
        var layout = new Layout(string.IsNullOrEmpty(name) ? fallbackName : name, ReadMonitor(root, width, height), width, height);

        if (root["widgets"] is not JsonArray entries)
        {
            throw Invalid("The file doesn't list the layout's widgets.");
        }

        if (entries.Count > Layout.MaxWidgets)
        {
            return LayoutImport.Failed(
                $"The file lists {entries.Count} widgets, and a layout holds at most {Layout.MaxWidgets}, one of each. It wasn't made by this app or has been changed by hand.");
        }

        var unknown = new List<string>();
        var repeated = new List<string>();
        for (var i = 0; i < entries.Count; i++)
        {
            var where = $"widget {i + 1}";
            if (entries[i] is not JsonObject entry)
            {
                throw Invalid($"The file's {where} isn't valid.");
            }

            var type = RequiredString(entry, "type", $"the type of {where}");
            if (WidgetCatalog.All.FirstOrDefault(descriptor => descriptor.Key == type) is not { } descriptor)
            {
                // Not checked any further: a newer version may describe its widgets differently.
                unknown.Add(type);
                continue;
            }

            if (layout.Contains(type))
            {
                repeated.Add(descriptor.Name);
                continue;
            }

            layout.Add(ReadWidget(entry, i, type, $"the {descriptor.Name} widget", SensitiveFieldsOf(type, codecs)));
        }

        if (entries.Count > 0 && layout.Widgets.Count == 0)
        {
            return LayoutImport.Failed("None of the file's widgets exists in this version of the app.");
        }

        List<string> warnings = [];
        if (unknown.Count > 0)
        {
            warnings.Add($"Left out {Count(unknown.Count, "widget")} this version of the app doesn't have: {string.Join(", ", unknown.Distinct())}.");
        }

        if (repeated.Count > 0)
        {
            warnings.Add($"The file had the {string.Join(", ", repeated.Distinct())} widget more than once; kept the first.");
        }

        return new LayoutImport(layout, warnings, null);
    }

    /// <summary>The monitor the file was made for. A file without one is aimed at no monitor in
    /// particular, which resolves to the primary.</summary>
    private static MonitorRef ReadMonitor(JsonObject root, int width, int height)
    {
        if (OptionalObject(root, "monitor", "the monitor") is not { } monitor)
        {
            return new MonitorRef("", null, "Unknown monitor", width, height);
        }

        var devicePath = OptionalString(monitor, "id", "the monitor's id") ?? "";
        return new MonitorRef(
            devicePath,
            OptionalString(monitor, "edidId", "the monitor's model id") is { Length: > 0 } edid ? edid : MonitorCatalog.EdidIdFromDevicePath(devicePath),
            OptionalString(monitor, "name", "the monitor's name") ?? "Unknown monitor",
            monitor.ContainsKey("width") ? Integer(monitor, "width", "the monitor's width") : width,
            monitor.ContainsKey("height") ? Integer(monitor, "height", "the monitor's height") : height);
    }

    private static LayoutWidget ReadWidget(JsonObject entry, int index, string type, string what, IReadOnlySet<string> sensitive)
    {
        var scaleText = RequiredString(entry, "scale", $"the size of {what}");
        // By name only: Enum.TryParse would also take "3" or "M, L".
        if (!Enum.GetNames<ScaleLevel>().Contains(scaleText))
        {
            throw Invalid($"The size of {what}, \"{scaleText}\", isn't one of {string.Join(", ", Enum.GetNames<ScaleLevel>())}.");
        }

        var opacity = OptionalNumber(entry, "opacity", $"the opacity of {what}") ?? WidgetOpacityStore.Default;
        if (opacity is < 0 or > 1)
        {
            throw Invalid($"The opacity of {what} must be between 0 and 1.");
        }

        var config = entry["config"] switch
        {
            null when !entry.ContainsKey("config") => [],
            JsonObject value => (JsonObject)value.DeepClone(),
            _ => throw Invalid($"The settings of {what} aren't valid."),
        };

        return new LayoutWidget
        {
            Type = type,
            X = Number(entry, "x", $"the position of {what}"),
            Y = Number(entry, "y", $"the position of {what}"),
            Scale = Enum.Parse<ScaleLevel>(scaleText),
            Width = NonNegative(OptionalNumber(entry, "width", $"the width of {what}") ?? 0, $"the width of {what}"),
            Height = NonNegative(OptionalNumber(entry, "height", $"the height of {what}") ?? 0, $"the height of {what}"),
            ZIndex = entry.ContainsKey("zIndex") ? Integer(entry, "zIndex", $"the layer of {what}") : index,
            Visible = OptionalBool(entry, "visible", $"whether {what} is shown") ?? true,
            Locked = OptionalBool(entry, "locked", $"whether {what} is locked") ?? false,
            Opacity = opacity,
            HideOutsideCar = OptionalBool(entry, "hideOutsideCar", $"whether {what} hides outside the car") ?? false,
            Config = WithoutSensitive(config, sensitive),
            // Its sensitive fields are empty whatever the file had: they need filling in again.
            RequiresConfiguration = sensitive.Count > 0,
        };
    }

    private static IReadOnlySet<string> SensitiveFieldsOf(string type, IReadOnlyDictionary<string, IWidgetConfigCodec> codecs) =>
        codecs.TryGetValue(type, out var codec) ? codec.SensitiveFields : new HashSet<string>();

    private static JsonObject WithoutSensitive(JsonObject config, IReadOnlySet<string> sensitive)
    {
        var copy = (JsonObject)config.DeepClone();
        foreach (var key in copy.Select(pair => pair.Key).Where(sensitive.Contains).ToList())
        {
            copy.Remove(key);
        }

        return copy;
    }

    // ===== Typed reads, each failing with a message that names what is wrong =====

    private static JsonObject? OptionalObject(JsonObject parent, string key, string what) => parent[key] switch
    {
        null when !parent.ContainsKey(key) => null,
        JsonObject value => value,
        _ => throw Invalid($"The file is damaged: {what} isn't valid."),
    };

    private static string RequiredString(JsonObject parent, string key, string what) =>
        OptionalString(parent, key, what) is { Length: > 0 } value ? value : throw Invalid($"The file is damaged: {what} is missing.");

    private static string? OptionalString(JsonObject parent, string key, string what) => parent[key] switch
    {
        null => null,
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        _ => throw Invalid($"The file is damaged: {what} isn't valid."),
    };

    private static int Integer(JsonObject parent, string key, string what) =>
        parent[key] is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<int>(out var number)
            ? number
            : throw Invalid($"The file is damaged: {what} is missing or isn't a whole number.");

    private static double Number(JsonObject parent, string key, string what) =>
        OptionalNumber(parent, key, what) ?? throw Invalid($"The file is damaged: {what} is missing.");

    private static double? OptionalNumber(JsonObject parent, string key, string what) => parent[key] switch
    {
        null when !parent.ContainsKey(key) => null,
        JsonValue value when value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<double>(out var number) && double.IsFinite(number) => number,
        _ => throw Invalid($"The file is damaged: {what} isn't a number."),
    };

    private static bool? OptionalBool(JsonObject parent, string key, string what) => parent[key] switch
    {
        null when !parent.ContainsKey(key) => null,
        JsonValue value when value.TryGetValue<bool>(out var flag) => flag,
        _ => throw Invalid($"The file is damaged: {what} isn't true or false."),
    };

    private static double NonNegative(double value, string what) =>
        value >= 0 ? value : throw Invalid($"The file is damaged: {what} can't be negative.");

    private static InvalidFileException Invalid(string message) => new(message);

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private static double Finite(double value, double fallback = 0) => double.IsFinite(value) ? value : fallback;

    private static DateTime TruncateToSeconds(DateTime utc) =>
        new(utc.Ticks - utc.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

    private sealed class InvalidFileException(string message) : Exception(message);

    // ===== Format v1, in the order the fields are written =====

    private sealed record FileV1(int FormatVersion, MetadataV1 Metadata, MonitorV1 Monitor, ResolutionV1 Resolution, List<WidgetV1> Widgets);

    private sealed record MetadataV1(string Name, DateTime ExportedAt, string AppVersion);

    private sealed record MonitorV1(string Id, string? EdidId, string Name, int Width, int Height);

    private sealed record ResolutionV1(int Width, int Height);

    private sealed record WidgetV1(
        string Type,
        double X,
        double Y,
        ScaleLevel Scale,
        double Width,
        double Height,
        int ZIndex,
        bool Visible,
        bool Locked,
        double Opacity,
        bool HideOutsideCar,
        JsonObject Config);
}
