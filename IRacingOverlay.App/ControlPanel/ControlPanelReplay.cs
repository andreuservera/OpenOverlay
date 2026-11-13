using System.IO;
using System.Windows;
using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.App.Layouts;
using IRacingOverlay.App.Localization;
using IRacingOverlay.Sdk.Replay;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// Diagnostics › Replay: plays an .ibt recording into the overlays as if iRacing were running, to
/// try a layout or look at a widget without driving. The recording goes into the same shared memory
/// iRacing uses, so the overlays read it exactly as they read the sim; that also means it only works
/// while iRacing is closed.
/// </summary>
public sealed partial class ControlPanelViewModel
{
    // One replay for the whole app, kept across page rebuilds.
    private static IbtReplayer? _replay;
    private static string? _replayFile;
    private ActionSetting? _replayButton;

    private SettingsGroup ReplayGroup()
    {
        var button = new ActionSetting(
            Loc.T("diagnostics.replay"),
            Loc.T("diagnostics.replay.hint"),
            ReplayButtonText(),
            ToggleReplay);
        _replayButton = button;
        return new SettingsGroup(Loc.T("diagnostics.replay.title")).With(button);
    }

    private static string ReplayButtonText() =>
        _replay is { IsPlaying: true } && _replayFile is { } file
            ? Loc.F("diagnostics.replay.stop", Path.GetFileNameWithoutExtension(file))
            : Loc.T("diagnostics.replay.choose");

    private void ToggleReplay()
    {
        if (_replay is { } playing)
        {
            AppLog.Activity("Replay", "Stopped");
            _replay = null;
            playing.Dispose();
            UpdateReplayButton();
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("diagnostics.replay.choose"),
            Filter = Loc.T("diagnostics.replay.filter") + " (*.ibt)|*.ibt",
            CheckFileExists = true,
        };
        if (!ShowFileDialog(dialog))
        {
            return;
        }

        IbtReplayer replayer;
        try
        {
            replayer = new IbtReplayer(IbtRecording.Open(dialog.FileName));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            AppLog.Warn("Replay", "Could not read the telemetry file", error);
            LayoutDialog.Inform(DialogOwner, Loc.T("diagnostics.replay.unreadable"), error.Message);
            return;
        }

        try
        {
            replayer.Start(loop: true);
        }
        catch (InvalidOperationException error)
        {
            replayer.Dispose();
            AppLog.Info("Replay", "Not started: iRacing owns the shared memory", new Dictionary<string, string> { ["reason"] = error.Message });
            LayoutDialog.Inform(DialogOwner, Loc.T("diagnostics.replay.busy"), Loc.T("diagnostics.replay.busy.hint"));
            return;
        }

        replayer.Finished += error =>
        {
            if (error is not null)
            {
                AppLog.Warn("Replay", "Replay stopped by an error", error);
            }

            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (ReferenceEquals(_replay, replayer))
                {
                    _replay = null;
                    replayer.Dispose();
                    UpdateReplayButton();
                }
            });
        };
        _replay = replayer;
        _replayFile = dialog.FileName;
        AppLog.Activity("Replay", "Started", new Dictionary<string, string> { ["file"] = Path.GetFileName(dialog.FileName) });
        UpdateReplayButton();
    }

    private void UpdateReplayButton()
    {
        if (_replayButton is { } button)
        {
            button.ButtonText = ReplayButtonText();
        }
    }

    /// <summary>On exit: a replay left playing would hold the shared memory until the process ends anyway.</summary>
    internal static void StopReplay()
    {
        var replay = _replay;
        _replay = null;
        replay?.Dispose();
    }
}
