using System.Text.Json;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.Layouts;

/// <summary>
/// The layout editor's state without any UI: a working copy of one layout, every change the editor
/// can make to it, and the undo/redo history of those changes.
///
/// Every operation records the state before it, so undo is a matter of putting a copy back rather
/// than of each operation knowing how to reverse itself. A layout is a dozen small objects; copying
/// it is cheaper than getting a hand-written inverse wrong. Drags and resizes go through
/// <see cref="BeginGesture"/>/<see cref="EndGesture"/> so a whole drag is one undo step however many
/// mouse moves it took.
/// </summary>
public sealed class LayoutEditorModel
{
    /// <summary>Steps kept for undo. The spec asks for at least 50.</summary>
    public const int HistoryLimit = 100;

    private readonly LinkedList<Layout> _undo = new();
    private readonly Stack<Layout> _redo = new();
    private Layout _saved;
    private Layout? _gestureStart;

    public LayoutEditorModel(Layout layout)
    {
        Layout = layout.Clone();
        _saved = layout.Clone();
    }

    /// <summary>The working copy. Replaced by undo and redo, so hold on to the model, not to this.</summary>
    public Layout Layout { get; private set; }

    public bool IsDirty => ContentOf(Layout) != ContentOf(_saved);

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Raised after every change to <see cref="Layout"/>, undo and redo included.</summary>
    public event Action? Changed;

    /// <summary>Adopts what the store actually saved (it may have adjusted the name) as the new
    /// clean state. History is kept: saving is not an editing step.</summary>
    public void MarkSaved(Layout saved)
    {
        Layout.Name = saved.Name;
        Layout.ModifiedUtc = saved.ModifiedUtc;
        _saved = Layout.Clone();
        Changed?.Invoke();
    }

    /// <summary>Takes on a name given to the layout elsewhere (the Layouts page) while it is open
    /// here, everywhere a copy of it is kept, so neither saving nor undo can bring the old name back.</summary>
    public void AdoptName(string name)
    {
        foreach (var copy in _undo.Concat(_redo).Append(_saved).Append(Layout))
        {
            copy.Name = name;
        }

        Changed?.Invoke();
    }

    /// <summary>Adds a widget on top of the others. Throws <see cref="LayoutRuleException"/> if the
    /// layout can't take it, without recording anything.</summary>
    public void Add(LayoutWidget widget)
    {
        var before = Layout.Clone();
        widget.ZIndex = Layout.Widgets.Count == 0 ? 0 : Layout.Widgets.Max(other => other.ZIndex) + 1;
        Layout.Add(widget);
        Commit(before);
    }

    public void Remove(string type) => Change(() => Layout.Remove(type));

    public void Move(string type, double x, double y) => ChangeUnlocked(type, widget =>
    {
        widget.X = x;
        widget.Y = y;
    });

    /// <summary>Sets the size level, and the position along with it so a resize can keep its
    /// opposite corner where it was.</summary>
    public void Resize(string type, ScaleLevel scale, double x, double y) => ChangeUnlocked(type, widget =>
    {
        widget.Scale = scale;
        widget.X = x;
        widget.Y = y;
    });

    public void SetLocked(string type, bool locked) => ChangeWidget(type, widget => widget.Locked = locked);

    public void SetVisible(string type, bool visible) => ChangeWidget(type, widget => widget.Visible = visible);

    /// <summary>Records the measured size the preview reports. Not an editing step: it changes
    /// whenever the preview re-measures, and is left out of the dirty check for the same reason.</summary>
    public void SetMeasuredSize(string type, double width, double height)
    {
        if (Layout.WidgetOf(type) is { } widget)
        {
            widget.Width = width;
            widget.Height = height;
        }
    }

    public void BringToFront(string type) => Restack(type, toFront: true);

    public void SendToBack(string type) => Restack(type, toFront: false);

    public void SetSnap(bool enabled, int gridSize) => Change(() =>
    {
        Layout.SnapEnabled = enabled;
        Layout.GridSize = Math.Clamp(gridSize, 1, 500);
    });

    /// <summary>Changes the target monitor and resolution. With <paramref name="scalePositions"/>,
    /// every widget keeps its place relative to the screen ("Scale positions"); without, it keeps
    /// its pixel position ("Keep pixels"). Sizes are levels, so they never scale.</summary>
    public void ChangeTarget(MonitorRef monitor, int width, int height, bool scalePositions)
    {
        var before = Layout.Clone();
        var (oldWidth, oldHeight) = (Layout.Width, Layout.Height);
        Layout.SetResolution(width, height);
        Layout.Monitor = monitor;
        if (scalePositions)
        {
            foreach (var widget in Layout.Widgets)
            {
                widget.X = Math.Round(widget.X * width / oldWidth);
                widget.Y = Math.Round(widget.Y * height / oldHeight);
            }
        }

        Commit(before);
    }

    /// <summary>Starts a drag or resize: the state now is what undo goes back to once it ends.</summary>
    public void BeginGesture() => _gestureStart ??= Layout.Clone();

    /// <summary>Moves a widget mid-gesture, without recording a step.</summary>
    public void PreviewMove(string type, double x, double y)
    {
        if (Layout.WidgetOf(type) is { Locked: false } widget)
        {
            widget.X = x;
            widget.Y = y;
            Changed?.Invoke();
        }
    }

    /// <summary>Resizes a widget mid-gesture, without recording a step.</summary>
    public void PreviewResize(string type, ScaleLevel scale, double x, double y)
    {
        if (Layout.WidgetOf(type) is { Locked: false } widget)
        {
            widget.Scale = scale;
            widget.X = x;
            widget.Y = y;
            Changed?.Invoke();
        }
    }

    /// <summary>Ends a gesture as one undo step, or as nothing if it changed nothing.</summary>
    public void EndGesture()
    {
        if (_gestureStart is { } start)
        {
            _gestureStart = null;
            Commit(start);
        }
    }

    public void Undo()
    {
        if (_undo.Last is not { } last)
        {
            return;
        }

        _undo.RemoveLast();
        _redo.Push(Layout);
        Layout = last.Value;
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!_redo.TryPop(out var next))
        {
            return;
        }

        _undo.AddLast(Layout);
        Layout = next;
        Changed?.Invoke();
    }

    private void Restack(string type, bool toFront)
    {
        if (Layout.WidgetOf(type) is null)
        {
            return;
        }

        Change(() =>
        {
            var others = Layout.Widgets.Where(widget => widget.Type != type).OrderBy(widget => widget.ZIndex).ToList();
            var target = Layout.WidgetOf(type)!;
            List<LayoutWidget> order = toFront ? [.. others, target] : [target, .. others];
            for (var i = 0; i < order.Count; i++)
            {
                order[i].ZIndex = i;
            }
        });
    }

    private void ChangeWidget(string type, Action<LayoutWidget> change)
    {
        if (Layout.WidgetOf(type) is { } widget)
        {
            Change(() => change(widget));
        }
    }

    /// <summary>Position and size are what locking protects; a locked widget ignores both.</summary>
    private void ChangeUnlocked(string type, Action<LayoutWidget> change)
    {
        if (Layout.WidgetOf(type) is { Locked: false } widget)
        {
            Change(() => change(widget));
        }
    }

    private void Change(Action change)
    {
        var before = Layout.Clone();
        change();
        Commit(before);
    }

    /// <summary>Records <paramref name="before"/> as an undo step if the layout actually changed.
    /// A new step forgets whatever could have been redone.</summary>
    private void Commit(Layout before)
    {
        if (ContentOf(before) == ContentOf(Layout))
        {
            Changed?.Invoke();
            return;
        }

        _undo.AddLast(before);
        if (_undo.Count > HistoryLimit)
        {
            _undo.RemoveFirst();
        }

        _redo.Clear();
        Changed?.Invoke();
    }

    /// <summary>What counts as a change: everything except the measured sizes, which follow the
    /// preview, and the modification time, which follows saving.</summary>
    private static string ContentOf(Layout layout)
    {
        var copy = layout.Clone();
        copy.ModifiedUtc = default;
        foreach (var widget in copy.Widgets)
        {
            widget.Width = 0;
            widget.Height = 0;
        }

        return JsonSerializer.Serialize(copy);
    }
}
