using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using IRacingOverlay.App.Diagnostics;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// The short notice after a change that touches a lot at once — positions reset, a layout deleted,
/// shortcuts restored, a widget reset, settings copied — with an Undo for a few seconds. Undo is
/// what lets these actions go ahead without an "are you sure?" first: a mistake costs one click,
/// not a re-setup.
/// </summary>
public sealed partial class ControlPanelViewModel
{
    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(6);

    private Toast? _toast;
    private DispatcherTimer? _toastTimer;
    private RelayCommand? _dismissToast;

    /// <summary>The notice on screen, or null.</summary>
    public Toast? Toast
    {
        get => _toast;
        private set
        {
            _toast = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasToast));
        }
    }

    public bool HasToast => _toast is not null;

    public ICommand DismissToastCommand => _dismissToast ??= new RelayCommand(() => Toast = null);

    /// <summary>Says what just happened, and offers to take it back while the notice is up.</summary>
    internal void ShowToast(string message, Action? undo)
    {
        Toast = new Toast(message, undo is null ? null : new RelayCommand(() =>
        {
            Toast = null;
            AppLog.Activity("Control Panel", $"Undone: {message}");
            undo();
        }));

        if (Application.Current is null)
        {
            return;
        }

        _toastTimer ??= new DispatcherTimer();
        _toastTimer.Stop();
        _toastTimer.Interval = ToastDuration;
        _toastTimer.Tick -= OnToastElapsed;
        _toastTimer.Tick += OnToastElapsed;
        _toastTimer.Start();
    }

    private void OnToastElapsed(object? sender, EventArgs e)
    {
        _toastTimer?.Stop();
        Toast = null;
    }
}

/// <summary>A notice: what happened, and how to take it back (null when it can't be).</summary>
public sealed record Toast(string Message, ICommand? UndoCommand)
{
    public bool CanUndo => UndoCommand is not null;
}
