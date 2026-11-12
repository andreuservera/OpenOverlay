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
    private readonly string _language;
    private SpeechSynthesizer? _synth;

    /// <param name="rate">-10 (slow) to 10 (fast); 1 is a little quicker than normal.</param>
    /// <param name="volume">0–100.</param>
    /// <param name="voice">Name of an installed voice; null to pick one for <paramref name="language"/>.</param>
    /// <param name="language">Language of the text ("en"): without a named voice, the first installed
    /// voice for it is used rather than the system default, which may speak another language.</param>
    public WindowsSpeech(int rate = 1, int volume = 100, string? voice = null, string language = "en")
    {
        _language = language;
        _rate = Math.Clamp(rate, -10, 10);
        _volume = Math.Clamp(volume, 0, 100);
        _voice = voice;
    }

    /// <summary>The voices installed on this machine: name and language (e.g. "en-US").</summary>
    public static IReadOnlyList<(string Name, string Culture)> InstalledVoices()
    {
        using var s = new SpeechSynthesizer();
        return s.GetInstalledVoices().Where(v => v.Enabled).Select(v => (v.VoiceInfo.Name, v.VoiceInfo.Culture.Name)).ToList();
    }

    /// <summary>The voice that is (or will be) speaking.</summary>
    public string VoiceName => (_synth ??= Create()).Voice.Name;

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
        else if (!s.Voice.Culture.Name.StartsWith(_language, StringComparison.OrdinalIgnoreCase))
        {
            var match = s.GetInstalledVoices().FirstOrDefault(v =>
                v.Enabled && v.VoiceInfo.Culture.Name.StartsWith(_language, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                s.SelectVoice(match.VoiceInfo.Name);
            }
        }

        s.SetOutputToDefaultAudioDevice();
        return s;
    }
}
