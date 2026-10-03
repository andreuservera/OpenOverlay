using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.Layouts;

/// <summary>
/// Every saved layout, plus the one currently open, in one file written through
/// <see cref="SettingsFile"/> (atomic, with a backup). Nothing handed out is the stored object:
/// reads return copies and writes store copies, so a layout being edited only changes what is
/// saved when it is saved on purpose.
/// </summary>
internal sealed class LayoutStore
{
    public const int SchemaVersion = 1;

    public static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "saved-layouts.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // A NaN position must cost that value, not the whole save: plain numbers throw on it.
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly List<Layout> _layouts;
    private OpenLayoutState? _open;

    public LayoutStore(string? filePath = null)
    {
        _filePath = filePath ?? DefaultFilePath;
        var document = SettingsFile.Read(_filePath, json => JsonSerializer.Deserialize<Document>(json, JsonOptions));
        _layouts = document?.Layouts?.Where(layout => layout is not null).ToList() ?? [];
        _open = document?.Open;

        foreach (var layout in _layouts)
        {
            foreach (var problem in layout.Normalize())
            {
                AppLog.Warn("Layouts", $"Layout \"{layout.Name}\": {problem}");
            }
        }

        if (_open is not null && _layouts.All(layout => layout.Id != _open.LayoutId))
        {
            // Kept regardless: the snapshot is what restores the individual widgets, and it is
            // still needed even though the layout itself has gone.
            AppLog.Warn("Layouts", "The open layout no longer exists; its snapshot is kept for restoring");
        }
    }

    /// <summary>Copies of every layout, in creation order.</summary>
    public IReadOnlyList<Layout> List() => _layouts.Select(layout => layout.Clone()).ToList();

    public Layout? Get(Guid id) => Find(id)?.Clone();

    public OpenLayoutState? Open => _open;

    /// <summary>Creates and saves an empty layout. A name already in use gets a " (n)" suffix.</summary>
    public Layout Create(string name, MonitorRef monitor, int width, int height)
    {
        var layout = new Layout(UniqueName(name, except: null), monitor, width, height);
        _layouts.Add(layout);
        Write();
        return layout.Clone();
    }

    /// <summary>Renames a layout, suffixing the name if another layout already has it. Returns the
    /// name actually used, or null if the layout doesn't exist.</summary>
    public string? Rename(Guid id, string name)
    {
        if (Find(id) is not { } layout)
        {
            return null;
        }

        layout.Name = UniqueName(name, except: id);
        layout.ModifiedUtc = DateTime.UtcNow;
        Write();
        return layout.Name;
    }

    /// <summary>Saves a deep copy as a new layout named after the original, e.g. "Race (2)".</summary>
    public Layout? Duplicate(Guid id)
    {
        if (Find(id) is not { } original)
        {
            return null;
        }

        var copy = original.Duplicate(UniqueName(original.Name, except: null));
        _layouts.Add(copy);
        Write();
        return copy.Clone();
    }

    public bool Delete(Guid id)
    {
        if (_layouts.RemoveAll(layout => layout.Id == id) == 0)
        {
            return false;
        }

        Write();
        return true;
    }

    /// <summary>Stores a copy of the layout, replacing the saved one with the same id or adding it
    /// if there is none. The name is made unique against the other layouts. Returns what was saved.</summary>
    public Layout Save(Layout layout)
    {
        var copy = layout.Clone();
        copy.Name = UniqueName(copy.Name, except: copy.Id);
        copy.ModifiedUtc = DateTime.UtcNow;

        var index = _layouts.FindIndex(existing => existing.Id == copy.Id);
        if (index >= 0)
        {
            _layouts[index] = copy;
        }
        else
        {
            _layouts.Add(copy);
        }

        Write();
        return copy.Clone();
    }

    public void SetOpen(OpenLayoutState? open)
    {
        _open = open;
        Write();
    }

    private Layout? Find(Guid id) => _layouts.FirstOrDefault(layout => layout.Id == id);

    private string UniqueName(string name, Guid? except) =>
        LayoutNaming.Unique(name, _layouts.Where(layout => layout.Id != except).Select(layout => layout.Name));

    private void Write() =>
        SettingsFile.Write(_filePath, JsonSerializer.Serialize(new Document(SchemaVersion, _layouts, _open), JsonOptions));

    private sealed record Document(int SchemaVersion, List<Layout>? Layouts, OpenLayoutState? Open);
}
