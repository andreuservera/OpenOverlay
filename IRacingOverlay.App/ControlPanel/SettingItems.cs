using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;

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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
    private double _value;

    public SliderSetting(
        string label,
        string? hint,
        double value,
        double minimum,
        double maximum,
        double step,
        Action<double> apply)
        : base(label, hint)
    {
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

    public string Display => (Value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
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
            _apply(value);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>A set of peer toggles shown as a chip grid. The right shape whenever the useful question
/// is "which of these are on" rather than "is this one thing on" — table columns, panel blocks —
/// because the whole set stays visible in the space a handful of switch rows would eat.</summary>
public sealed class ChipGroupSetting : SettingItem
{
    public ChipGroupSetting(string label, string? hint, IReadOnlyList<ChipSetting> chips)
        : base(label, hint)
    {
        Chips = chips;
    }

    public IReadOnlyList<ChipSetting> Chips { get; }
}

/// <summary>A row whose control is a button — something that happens rather than something that is
/// set. The caption is mutable so the button can state the action it will perform next.</summary>
public sealed class ActionSetting : SettingItem
{
    private string _buttonText;

    public ActionSetting(string label, string? hint, string buttonText, Action invoke)
        : base(label, hint)
    {
        _buttonText = buttonText;
        InvokeCommand = new RelayCommand(invoke);
    }

    public ICommand InvokeCommand { get; }

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

/// <summary>A titled block of related settings — the unit the configuration pane is built from.</summary>
public sealed class SettingsGroup
{
    public SettingsGroup(string title, string? subtitle = null)
    {
        Title = title;
        Subtitle = subtitle;
    }

    public string Title { get; }
    public string? Subtitle { get; }
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

    public void Execute(object? parameter) => _execute();
}
