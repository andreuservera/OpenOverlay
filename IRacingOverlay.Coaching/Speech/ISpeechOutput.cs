namespace IRacingOverlay.Coaching.Speech;

/// <summary>Says a sentence out loud. A new sentence replaces one still being spoken.</summary>
public interface ISpeechOutput : IDisposable
{
    void Say(string text);
}
