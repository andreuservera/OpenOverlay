using IRacingOverlay.Coaching.Speech;

namespace IRacingOverlay.Coaching.Ui;

/// <summary>
/// Speaks through Windows and writes each line to the app's log with the voice that said it, so a
/// silent coach can be told apart from one that spoke where the driver couldn't hear it. A voice
/// that fails is logged instead of stopping the coach.
/// </summary>
internal sealed class LoggedVoice(WindowsSpeech voice, Action<string> log) : ISpeechOutput
{
    public void Say(string text)
    {
        try
        {
            voice.Say(text);
            log($"Said ({voice.VoiceName}, default audio output): {text}");
        }
        catch (Exception e) when (e is InvalidOperationException or PlatformNotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            log($"Could not speak ({e.GetType().Name}: {e.Message}): {text}");
        }
    }

    // The voice belongs to LiveCoaching, which disposes it.
    public void Dispose()
    {
    }
}
