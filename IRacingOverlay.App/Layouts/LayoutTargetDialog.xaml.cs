using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IRacingOverlay.App.Layouts;

/// <summary>What the target dialog was answered with.</summary>
public sealed record LayoutTarget(string Name, MonitorRef Monitor, int Width, int Height);

/// <summary>
/// Picks a layout's monitor and resolution, and its name when creating one. The resolution follows
/// the chosen monitor unless the user picks a preset or types their own; either way the layout stays
/// associated with the monitor picked.
/// </summary>
public partial class LayoutTargetDialog : Window
{
    /// <summary>The presets the spec lists, in order.</summary>
    public static readonly IReadOnlyList<(int Width, int Height)> Presets =
        [(1280, 720), (1920, 1080), (2560, 1440), (3440, 1440), (3840, 2160)];

    private readonly IReadOnlyList<DisplayMonitor> _monitors;
    private readonly MonitorRef? _current;
    private readonly IReadOnlyCollection<string> _takenNames;
    private readonly bool _currentIsDisconnected;
    private LayoutTarget? _result;

    /// <param name="imported">Importing: a layout read from a file, whose name and monitor are
    /// what the dialog starts from. It is otherwise new, so <paramref name="existing"/> is null.</param>
    private LayoutTargetDialog(
        Window? owner,
        IReadOnlyList<DisplayMonitor> monitors,
        Layout? existing,
        IReadOnlyCollection<string>? takenNames = null,
        Layout? imported = null)
    {
        InitializeComponent();
        Owner = owner is { IsVisible: true } ? owner : null;
        if (Owner is null)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        _current = existing?.Monitor;
        _takenNames = takenNames ?? [];

        // A layout whose monitor isn't connected keeps it unless the user picks another: listed
        // first, as it was stored, so applying a new resolution never re-targets it by accident.
        _currentIsDisconnected = existing is not null && MonitorCatalog.Resolve(existing.Monitor, monitors) is not { IsFallback: false };
        _monitors = _currentIsDisconnected
            ? [new DisplayMonitor(existing!.Monitor.DevicePath, existing.Monitor.EdidId, existing.Monitor.FriendlyName, "", 0, 0,
                existing.Monitor.Width, existing.Monitor.Height, IsPrimary: false), .. monitors]
            : monitors;
        monitors = _monitors;

        Heading.Text = imported is not null ? "Import layout" : existing is null ? "New layout" : "Monitor and resolution";
        ConfirmButton.Content = imported is not null ? "Import" : existing is null ? "Create" : "Apply";
        NameRow.Visibility = existing is null ? Visibility.Visible : Visibility.Collapsed;
        NameBox.Text = existing?.Name ?? imported?.Name ?? "";

        // An imported layout starts on the monitor it was made for, if this computer has it.
        MonitorBox.ItemsSource = monitors.Select(Describe).ToList();
        var selected = imported is not null && MonitorCatalog.Resolve(imported.Monitor, monitors) is { IsFallback: false } own
            ? monitors.ToList().IndexOf(own.Monitor)
            : existing is null
            ? monitors.ToList().FindIndex(monitor => monitor.IsPrimary)
            : _currentIsDisconnected ? 0
            : MonitorCatalog.Resolve(existing.Monitor, monitors) is { IsFallback: false } found ? monitors.ToList().IndexOf(found.Monitor) : -1;
        MonitorBox.SelectedIndex = monitors.Count == 0 ? -1 : Math.Max(0, selected);
        if (monitors.Count == 0)
        {
            OnMonitorChanged(MonitorBox, null!);
        }

        // Changing an existing layout starts from its own resolution, not the monitor's.
        if (existing is not null)
        {
            SelectResolution(existing.Width, existing.Height);
        }

        Loaded += (_, _) => (existing is null ? NameBox : (UIElement)MonitorBox).Focus();
    }

    /// <summary>Asks for a new layout's name, monitor and resolution. Null when cancelled.</summary>
    /// <param name="takenNames">Names already in use, so a repeat can be flagged as the user types.</param>
    public static LayoutTarget? ForNew(Window? owner, IReadOnlyList<DisplayMonitor> monitors, IReadOnlyCollection<string> takenNames) =>
        Show(new(owner, monitors, null, takenNames));

    /// <summary>Asks for an imported layout's name and the monitor to aim it at: by default the one
    /// it was made for if connected, otherwise the primary. Null when cancelled.</summary>
    public static LayoutTarget? ForImport(Window? owner, IReadOnlyList<DisplayMonitor> monitors, IReadOnlyCollection<string> takenNames, Layout imported) =>
        Show(new(owner, monitors, null, takenNames, imported));

    /// <summary>Asks where an existing layout should be aimed. Null when cancelled.</summary>
    public static LayoutTarget? ForExisting(Window? owner, IReadOnlyList<DisplayMonitor> monitors, Layout layout) =>
        Show(new(owner, monitors, layout));

    private static LayoutTarget? Show(LayoutTargetDialog dialog)
    {
        dialog.ShowDialog();
        return dialog._result;
    }

    private DisplayMonitor? SelectedMonitor => MonitorBox.SelectedIndex is >= 0 and var i && i < _monitors.Count ? _monitors[i] : null;

    private bool IsCustom => ResolutionBox.SelectedIndex == ResolutionBox.Items.Count - 1;

    private string Describe(DisplayMonitor monitor) =>
        _currentIsDisconnected && ReferenceEquals(monitor, _monitors[0])
            ? string.Create(CultureInfo.InvariantCulture, $"{monitor.FriendlyName} — {monitor.Width}×{monitor.Height} (not connected)")
            : string.Create(CultureInfo.InvariantCulture, $"{monitor.FriendlyName} — {monitor.Width}×{monitor.Height}{(monitor.IsPrimary ? " (primary)" : "")}");

    /// <summary>The spec asks for "Name (2)" to be suggested when a name repeats: say so while typing.</summary>
    private void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var unique = LayoutNaming.Unique(name, _takenNames);
        NameHint.Text = name.Length > 0 && unique != name ? $"Already used — it will be saved as \"{unique}\"." : "";
        NameHint.Visibility = NameHint.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnMonitorChanged(object sender, SelectionChangedEventArgs e)
    {
        var keep = ResolutionBox.SelectedIndex;
        var monitor = SelectedMonitor;
        var options = new List<string>
        {
            monitor is null ? "Monitor's resolution" : string.Create(CultureInfo.InvariantCulture, $"Monitor's resolution ({monitor.Width}×{monitor.Height})"),
        };
        options.AddRange(Presets.Select(preset => string.Create(CultureInfo.InvariantCulture, $"{preset.Width}×{preset.Height}")));
        options.Add("Custom");
        ResolutionBox.ItemsSource = options;
        ResolutionBox.SelectedIndex = keep < 0 ? 0 : keep;
    }

    private void OnResolutionChanged(object sender, SelectionChangedEventArgs e)
    {
        CustomRow.Visibility = IsCustom ? Visibility.Visible : Visibility.Collapsed;
        if (IsCustom && WidthBox.Text.Length == 0 && Resolution() is var (width, height))
        {
            WidthBox.Text = width.ToString(CultureInfo.InvariantCulture);
            HeightBox.Text = height.ToString(CultureInfo.InvariantCulture);
        }
    }

    private void SelectResolution(int width, int height)
    {
        if (SelectedMonitor is { } monitor && monitor.Width == width && monitor.Height == height)
        {
            ResolutionBox.SelectedIndex = 0;
            return;
        }

        var preset = Presets.ToList().IndexOf((width, height));
        if (preset >= 0)
        {
            ResolutionBox.SelectedIndex = preset + 1;
            return;
        }

        WidthBox.Text = width.ToString(CultureInfo.InvariantCulture);
        HeightBox.Text = height.ToString(CultureInfo.InvariantCulture);
        ResolutionBox.SelectedIndex = ResolutionBox.Items.Count - 1;
    }

    /// <summary>The chosen resolution, or the monitor's when "Custom" has nothing typed yet.</summary>
    private (int Width, int Height)? Resolution()
    {
        var index = ResolutionBox.SelectedIndex;
        if (index > 0 && index <= Presets.Count)
        {
            return Presets[index - 1];
        }

        if (IsCustom && int.TryParse(WidthBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var width) &&
            int.TryParse(HeightBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var height))
        {
            return (width, height);
        }

        return SelectedMonitor is { } monitor && !IsCustom ? (monitor.Width, monitor.Height) : null;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (Resolution() is not var (width, height) || !Layout.IsValidResolution(width, height))
        {
            ShowError($"Enter a width and height between {Layout.MinResolution} and {Layout.MaxResolution}.");
            return;
        }

        var monitor = (_currentIsDisconnected && MonitorBox.SelectedIndex == 0 ? _current : SelectedMonitor?.ToRef())
            ?? _current
            ?? new MonitorRef("", null, "Unknown monitor", width, height);
        _result = new LayoutTarget(NameBox.Text.Trim(), monitor, width, height);
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not TextBox)
        {
            DragMove();
        }
    }
}
