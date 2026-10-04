using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using IRacingOverlay.App.About;
using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// A single configurable option, independent of how it is drawn. Each concrete kind pairs a value
/// with the callback that writes it through to the live options object and its store — the control
/// panel never reaches into a widget directly, and the widget never learns that a control panel
/// exists.
///
/// The rendering side is one implicit DataTemplate per concrete type in ControlPanelTheme.xaml, so
/// adding a new kind of control to the entire application is exactly two things: a class here and a
/// template there. Adding a new *option* to an existing widget is one line in ControlPanelSchema.
/// </summary>
public abstract class SettingItem : INotifyPropertyChanged
{
    protected SettingItem(string label, string? hint)
    {
        Label = label;
        Hint = hint;
    }

    public string Label { get; }

    /// <summary>One sentence explaining what the option changes, shown under the label. Optional:
    /// self-evident settings are better off without a line of text repeating their own name.</summary>
    public string? Hint { get; }

    /// <summary>Where the setting lives ("Standings › Columns › "), stamped when its page is built.</summary>
    internal string TracePath { get; set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Records a change in the activity trail, before it is applied, so a change that fails is still on record.</summary>
    protected void Trace(string value) => AppLog.Activity("Settings", $"{TracePath}{Label}: {value}");
}

/// <summary>An on/off option, drawn as a switch.</summary>
public sealed class ToggleSetting : SettingItem
{
    private readonly Action<bool> _apply;
    private bool _value;

    public ToggleSetting(string label, string? hint, bool value, Action<bool> apply)
        : base(label, hint)
    {
        _value = value;
        _apply = apply;
    }

    public bool Value
    {
        get => _value;
        set
        {
            if (_value == value)
            {
                return;
            }

            _value = value;
            OnPropertyChanged();
            Trace(value ? "on" : "off");
            _apply(value);
        }
    }
}

/// <summary>One of a short list of named alternatives, drawn as a dropdown.</summary>
public sealed class ChoiceSetting : SettingItem
{
    private readonly Action<int> _apply;
    private int _selectedIndex;

    public ChoiceSetting(string label, string? hint, IReadOnlyList<string> options, int selectedIndex, Action<int> apply)
        : base(label, hint)
    {
        Options = options;
        _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, options.Count - 1));
        _apply = apply;
    }

    public IReadOnlyList<string> Options { get; }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            // A ComboBox reports -1 while its ItemsSource is being swapped; treating that as a real
            // choice would write a garbage value straight into the store.
            if (value < 0 || _selectedIndex == value)
            {
                return;
            }

            _selectedIndex = value;
            OnPropertyChanged();
            Trace(value < Options.Count ? Options[value] : value.ToString(CultureInfo.InvariantCulture));
            _apply(value);
        }
    }
}

/// <summary>Same data as <see cref="ChoiceSetting"/>, drawn as a segmented control instead. Used
/// where seeing the whole range at once is part of the information — the XS…XL size ladder.</summary>
public sealed class SegmentedSetting : SettingItem
{
    private readonly Action<int> _apply;
    private int _selectedIndex;

    public SegmentedSetting(string label, string? hint, IReadOnlyList<string> options, int selectedIndex, Action<int> apply)
        : base(label, hint)
    {
        Options = options;
        _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, options.Count - 1));
        _apply = apply;
    }

    public IReadOnlyList<string> Options { get; }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < 0 || _selectedIndex == value)
            {
                return;
            }

            _selectedIndex = value;
            OnPropertyChanged();
            Trace(value < Options.Count ? Options[value] : value.ToString(CultureInfo.InvariantCulture));
            _apply(value);
        }
    }

    /// <summary>Reflects a change made somewhere else (the widget's own +/- control) back into the
    /// selector without re-firing the callback that would write it straight back out again.</summary>
    public void SyncTo(int index)
    {
        if (index < 0 || _selectedIndex == index)
        {
            return;
        }

        _selectedIndex = index;
        OnPropertyChanged(nameof(SelectedIndex));
    }
}

/// <summary>A number with a stepper. Typed input is deliberately lenient: an entry is only applied
/// once it parses inside the allowed range, so an empty box or a half-typed "1." on the way to
/// "1.5" leaves the previous value alone rather than being rewritten under the user.</summary>
public sealed class NumberSetting : SettingItem
{
    private readonly Action<double> _apply;
    private double _value;
    private string _text;

    public NumberSetting(
        string label,
        string? hint,
        double value,
        double minimum,
        double maximum,
        double step,
        string format,
        string? unit,
        Action<double> apply)
        : base(label, hint)
    {
        Minimum = minimum;
        Maximum = maximum;
        Step = step;
        Format = format;
        Unit = unit;
        _value = Math.Clamp(value, minimum, maximum);
        _text = _value.ToString(format, CultureInfo.InvariantCulture);
        _apply = apply;

        IncrementCommand = new RelayCommand(() => Nudge(+1));
        DecrementCommand = new RelayCommand(() => Nudge(-1));
    }

    public double Minimum { get; }
    public double Maximum { get; }
    public double Step { get; }
    public string Format { get; }
    public string? Unit { get; }

    public ICommand IncrementCommand { get; }
    public ICommand DecrementCommand { get; }

    public double Value
    {
        get => _value;
        private set
        {
            var clamped = Math.Clamp(value, Minimum, Maximum);
            if (_value.Equals(clamped))
            {
                return;
            }

            _value = clamped;
            OnPropertyChanged();
            Trace(clamped.ToString(Format, CultureInfo.InvariantCulture) + (Unit is null ? "" : " " + Unit));
            _apply(clamped);
        }
    }

    /// <summary>What the box actually contains. Kept separate from <see cref="Value"/> so mid-typing
    /// states survive: the text is whatever was typed, the value only tracks it once it's valid.</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (_text == value)
            {
                return;
            }

            _text = value;
            OnPropertyChanged();

            // Both separators accepted: the box shows invariant formatting, but a user on a
            // comma-decimal locale will reasonably type a comma.
            var normalized = value.Replace(',', '.');
            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
                parsed >= Minimum && parsed <= Maximum)
            {
                Value = parsed;
            }
        }
    }

    private void Nudge(int direction)
    {
        var next = Math.Clamp(Value + (direction * Step), Minimum, Maximum);
        Value = next;
        SetText(next.ToString(Format, CultureInfo.InvariantCulture));
    }

    private void SetText(string text)
    {
        _text = text;
        OnPropertyChanged(nameof(Text));
    }
}

/// <summary>A continuous value with a slider and a live percentage readout. Right whenever the
/// question is "how much" rather than "which one" — the answer is judged by looking at the result,
/// not by picking a number, so the control has to be draggable while the preview updates.</summary>
public sealed class SliderSetting : SettingItem
{
    private readonly Action<double> _apply;
    private readonly Func<double, string>? _format;
    private double _value;

    public SliderSetting(
        string label,
        string? hint,
        double value,
        double minimum,
        double maximum,
        double step,
        Action<double> apply,
        Func<double, string>? format = null)
        : base(label, hint)
    {
        _format = format;
        Minimum = minimum;
        Maximum = maximum;
        Step = step;
        _value = Math.Clamp(value, minimum, maximum);
        _apply = apply;
    }

    public double Minimum { get; }
    public double Maximum { get; }
    public double Step { get; }

    public double Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, Minimum, Maximum);
            if (_value.Equals(clamped))
            {
                return;
            }

            _value = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
            _apply(clamped);
        }
    }

    /// <summary>The value beside the slider; a percentage unless the setting formats it itself.</summary>
    public string Display => _format?.Invoke(Value) ?? (Value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
}

/// <summary>One toggle inside a <see cref="ChipGroupSetting"/>.</summary>
public sealed class ChipSetting : INotifyPropertyChanged
{
    private readonly Action<bool> _apply;
    private bool _value;

    public ChipSetting(string label, string? hint, bool value, Action<bool> apply)
    {
        Label = label;
        Hint = hint;
        _value = value;
        _apply = apply;
    }

    public string Label { get; }
    public string? Hint { get; }

    /// <summary>See <see cref="SettingItem.TracePath"/>; includes the chip group's own label.</summary>
    internal string TracePath { get; set; } = "";

    public bool Value
    {
        get => _value;
        set
        {
            if (_value == value)
            {
                return;
            }

            _value = value;
            OnPropertyChanged();
            AppLog.Activity("Settings", $"{TracePath}{Label}: {(value ? "on" : "off")}");
            _apply(value);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>A set of peer toggles — "which of these are on" — shown as one row and switch each, like
/// a reorder list without the grip: for elements whose order is the widget's own (flag types,
/// weather readouts, the fuel headline).</summary>
public sealed class ChipGroupSetting : SettingItem
{
    public ChipGroupSetting(string label, string? hint, IReadOnlyList<ChipSetting> chips)
        : base(label, hint)
    {
        Chips = chips;
    }

    public IReadOnlyList<ChipSetting> Chips { get; }
}

/// <summary>One entry in a <see cref="ReorderListSetting"/>: its show/hide switch and, for an entry
/// that carries a second part (a table's iRating and its delta), that part's switch too.</summary>
public sealed class ReorderListItem : INotifyPropertyChanged
{
    private bool _isDragging;

    public ReorderListItem(Enum key, ChipSetting visible, ChipSetting? companion = null)
    {
        Key = key;
        Visible = visible;
        Companion = companion;
    }

    /// <summary>What the entry stands for: a table column, a bar field.</summary>
    public Enum Key { get; }

    public ChipSetting Visible { get; }

    public ChipSetting? Companion { get; }

    public string Label => Visible.Label;

    /// <summary>True while the row is being dragged, so the list can lift it.</summary>
    public bool IsDragging
    {
        get => _isDragging;
        set
        {
            if (_isDragging != value)
            {
                _isDragging = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDragging)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Things shown side by side (a table's columns, a bar's fields) as a list to reorder by dragging,
/// each row with its show/hide switch.
/// While a row is dragged only the list moves; the new order is applied once, on release, so a
/// drag is one change (one save, one undo step) however far it travels.
/// </summary>
public sealed class ReorderListSetting : SettingItem
{
    private readonly Action<IReadOnlyList<Enum>> _apply;
    private IReadOnlyList<Enum> _applied;

    public ReorderListSetting(string label, string? hint, IEnumerable<ReorderListItem> items, Action<IReadOnlyList<Enum>> apply)
        : base(label, hint)
    {
        Items = new ObservableCollection<ReorderListItem>(items);
        _applied = Order;
        _apply = apply;
    }

    public ObservableCollection<ReorderListItem> Items { get; }

    private IReadOnlyList<Enum> Order => Items.Select(item => item.Key).ToList();

    /// <summary>Moves a row in the list only, as a drag passes over the others.</summary>
    public void MoveLive(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= Items.Count || to >= Items.Count)
        {
            return;
        }

        Items.Move(from, to);
    }

    /// <summary>Applies the list's order if it differs from the last one applied.</summary>
    public void CommitOrder()
    {
        var order = Order;
        if (order.SequenceEqual(_applied))
        {
            return;
        }

        _applied = order;
        Trace(string.Join(", ", Items.Select(item => item.Label)));
        _apply(order);
    }

    /// <summary>A move and its commit in one step.</summary>
    public void Move(int from, int to)
    {
        MoveLive(from, to);
        CommitOrder();
    }
}

/// <summary>A row whose control is a button — something that happens rather than something that is
/// set. The caption is mutable so the button can state the action it will perform next.</summary>
public sealed class ActionSetting : SettingItem
{
    private string _buttonText;

    /// <param name="isEnabled">False for an action that is planned but not available yet: the row
    /// still says what it will do, and the disabled button says so.</param>
    public ActionSetting(string label, string? hint, string buttonText, Action invoke, bool isEnabled = true)
        : base(label, hint)
    {
        _buttonText = buttonText;
        IsEnabled = isEnabled;
        InvokeCommand = new RelayCommand(() =>
        {
            Trace("run");
            invoke();
        });
    }

    public ICommand InvokeCommand { get; }

    public bool IsEnabled { get; }

    public string ButtonText
    {
        get => _buttonText;
        set
        {
            if (_buttonText == value)
            {
                return;
            }

            _buttonText = value;
            OnPropertyChanged();
        }
    }
}

/// <summary>
/// A global shortcut: the current combination as a click-to-record field, an enable switch and a
/// clear button. Validation and duplicate checks happen in the callback, which returns the reason a
/// combination was refused; the reason is shown on the row instead of silently ignoring the press.
/// </summary>
public sealed class HotkeySetting : SettingItem
{
    private readonly Func<Hotkey?, string?> _assign;
    private readonly Action<bool> _setEnabled;
    private readonly Action<bool> _recordingChanged;
    private Hotkey? _hotkey;
    private bool _enabled;
    private bool _isRecording;
    private ModifierKeys _heldModifiers;
    private string? _error;
    private string? _status;

    public HotkeySetting(
        string label,
        string? hint,
        Hotkey? hotkey,
        bool enabled,
        Func<Hotkey?, string?> assign,
        Action<bool> setEnabled,
        Action<bool> recordingChanged)
        : base(label, hint)
    {
        _hotkey = hotkey;
        _enabled = enabled;
        _assign = assign;
        _setEnabled = setEnabled;
        _recordingChanged = recordingChanged;
        ClearCommand = new RelayCommand(Clear);
    }

    public ICommand ClearCommand { get; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            OnPropertyChanged();
            Trace(value ? "enabled" : "disabled");
            _setEnabled(value);
        }
    }

    public bool IsRecording
    {
        get => _isRecording;
        private set
        {
            if (_isRecording == value)
            {
                return;
            }

            _isRecording = value;
            _heldModifiers = ModifierKeys.None;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
            _recordingChanged(value);
        }
    }

    public bool HasHotkey => _hotkey is not null;

    /// <summary>What the field shows: the live combination while recording, the saved one otherwise.</summary>
    public string Display => _isRecording
        ? _heldModifiers == ModifierKeys.None ? "Press a shortcut…" : Hotkey.Describe(_heldModifiers, "…")
        : _hotkey?.Display ?? "Not set";

    /// <summary>Why the last recorded combination was refused, or why Windows wouldn't register it.</summary>
    public string? Message => _error ?? _status;

    public bool HasError => Message is not null;

    public void BeginRecording()
    {
        SetError(null);
        IsRecording = true;
    }

    public void CancelRecording() => IsRecording = false;

    public void PreviewModifiers(ModifierKeys modifiers)
    {
        _heldModifiers = modifiers;
        OnPropertyChanged(nameof(Display));
    }

    public void Record(ModifierKeys modifiers, Key key)
    {
        var candidate = new Hotkey(modifiers, key);
        var problem = candidate.Problem ?? _assign(candidate);
        IsRecording = false;
        if (problem is not null)
        {
            Trace($"{candidate.Display} refused ({problem})");
            SetError(problem);
            return;
        }

        Trace(candidate.Display);
        SetHotkey(candidate);
    }

    /// <summary>Set by the owner after registering with Windows: null when it went through.</summary>
    public void SetStatus(string? status)
    {
        _status = status;
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(HasError));
    }

    private void Clear()
    {
        Trace("cleared");
        _assign(null);
        SetError(null);
        SetHotkey(null);
    }

    private void SetHotkey(Hotkey? hotkey)
    {
        _hotkey = hotkey;
        OnPropertyChanged(nameof(Display));
        OnPropertyChanged(nameof(HasHotkey));
    }

    private void SetError(string? error)
    {
        _error = error;
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(HasError));
    }
}

/// <summary>A read-only fact — a version, a date — where a control would be. The value can change
/// while the page is open, as the update status does.</summary>
public sealed class InfoSetting : SettingItem
{
    private string _value;

    public InfoSetting(string label, string? hint, string value)
        : base(label, hint)
    {
        _value = value;
    }

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
            {
                return;
            }

            _value = value;
            OnPropertyChanged();
        }
    }
}

/// <summary>One line of release notes, with the lines nested under it.</summary>
public sealed class BulletSetting : SettingItem
{
    public BulletSetting(ChangelogItem item)
        : base(item.Text, null)
    {
        Item = item;
    }

    public ChangelogItem Item { get; }
}

/// <summary>A heading and its lines inside a <see cref="ReleaseSetting"/>.</summary>
public sealed record NoteSection(string Heading, IReadOnlyList<ChangelogItem> Items, bool IsWarning = false);

/// <summary>One release in the changelog: a header row that expands to its changes, so a long
/// history reads as a list of versions until one is opened.</summary>
public sealed class ReleaseSetting : SettingItem
{
    private bool _isExpanded;

    public ReleaseSetting(string title, string? date, string? badge, string? summary, IReadOnlyList<NoteSection> sections, bool isExpanded)
        : base(title, null)
    {
        Date = date;
        Badge = badge;
        Summary = summary;
        Sections = sections;
        _isExpanded = isExpanded;
    }

    public string? Date { get; }

    /// <summary>"YOUR VERSION", "LATEST" and the like, or null.</summary>
    public string? Badge { get; }

    public string? Summary { get; }

    public IReadOnlyList<NoteSection> Sections { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();
            Trace(value ? "expanded" : "collapsed");
        }
    }
}

/// <summary>A titled block of related settings — the unit the configuration pane is built from.
/// It can be folded shut down to its title; whether it is outlives the group, which pages rebuild
/// freely, because it is kept per <see cref="Scope"/> and title in <see cref="CollapseStore"/>.</summary>
public sealed class SettingsGroup : INotifyPropertyChanged
{
    private bool _keepOpen;

    public SettingsGroup(string title, string? subtitle = null)
    {
        Title = title;
        Subtitle = subtitle;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }
    public string? Subtitle { get; }

    /// <summary>The page the group belongs to — a widget's type, so the control panel and the layout
    /// editor fold that widget's groups together.</summary>
    public string? Scope { get; set; }

    /// <summary>Shows the group open whatever was saved: set on search results, so a match is never
    /// hidden inside a folded group.</summary>
    public bool KeepOpen
    {
        get => _keepOpen;
        set
        {
            _keepOpen = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCollapsed)));
        }
    }

    public string CollapseKey => $"{Scope}/{Title}";

    /// <summary>A group with nothing in it (a notice) has nothing to fold.</summary>
    public bool IsCollapsible => Items.Count > 0;

    public bool IsCollapsed
    {
        get => !KeepOpen && IsCollapsible && CollapseStore.IsCollapsed(CollapseKey);
        set
        {
            if (value == IsCollapsed || !IsCollapsible)
            {
                return;
            }

            KeepOpen = false;
            CollapseStore.SetCollapsed(CollapseKey, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCollapsed)));
        }
    }

    /// <summary>Draws the title in the danger colour, for a block that asks for attention.</summary>
    public bool IsWarning { get; init; }

    public ObservableCollection<SettingItem> Items { get; } = [];

    public SettingsGroup With(params SettingItem[] items)
    {
        foreach (var item in items)
        {
            Items.Add(item);
        }

        return this;
    }
}

/// <summary>Minimal parameterless command, so setting items can carry their own actions and every
/// template in the theme stays binding-only — no code-behind reaching into a data template.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;

    public RelayCommand(Action execute) => _execute = execute;

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    /// <summary>Contained, so a failing button (Explorer not starting, the clipboard held by another
    /// app) is a log line rather than an exception loose on the UI thread.</summary>
    public void Execute(object? parameter)
    {
        try
        {
            _execute();
        }
        catch (Exception e) when (!ExceptionPolicy.IsFatal(e))
        {
            AppLog.Error("Control Panel", "Action failed", e);
        }
    }
}
