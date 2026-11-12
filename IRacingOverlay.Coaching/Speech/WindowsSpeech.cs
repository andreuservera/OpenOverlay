using System.Speech.Synthesis;

namespace IRacingOverlay.Coaching.Speech;

/// <summary>
/// Offline text-to-speech with the voices built into Windows (SAPI through System.Speech): no
/// network, no model to load, and only a few MB of memory, so it can run beside iRacing. The engine
/// is created on first use and speaks on its own thread; a new debrief cuts off the previous one.
/// </summary>
public sealed class WindowsSpeech : ISpeechOutput
{
    private readonly int _rate;
    private readonly int _volume;
    private readonly string? _voice;
    private SpeechSynthesizer? _synth;

    /// <param name="rate">-10 (slow) to 10 (fast); 1 is a little quicker than normal.</param>
    /// <param name="volume">0–100.</param>
    /// <param name="voice">Name of an installed voice; null for the system default.</param>
    public WindowsSpeech(int rate = 1, int volume = 100, string? voice = null)
    {
        _rate = Math.Clamp(rate, -10, 10);
        _volume = Math.Clamp(volume, 0, 100);
        _voice = voice;
    }

    /// <summary>Names of the voices installed on this machine.</summary>
    public static IReadOnlyList<string> InstalledVoices()
    {
        using var s = new SpeechSynthesizer();
        return s.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name).ToList();
    }

    public void Say(string text)
    {
        var synth = _synth ??= Create();
        synth.SpeakAsyncCancelAll();
        synth.SpeakAsync(text);
    }

    /// <summary>Speaks and waits until done (for the command line tool).</summary>
    public void SayAndWait(string text)
    {
        var synth = _synth ??= Create();
        synth.SpeakAsyncCancelAll();
        synth.Speak(text);
    }

    public void Dispose()
    {
        _synth?.SpeakAsyncCancelAll();
        _synth?.Dispose();
        _synth = null;
    }

    private SpeechSynthesizer Create()
    {
        var s = new SpeechSynthesizer { Rate = _rate, Volume = _volume };
        if (_voice is not null)
        {
            s.SelectVoice(_voice);
        }

        s.SetOutputToDefaultAudioDevice();
        return s;
    }
}
