namespace Yuki.Core.TTS.Contracts;

public interface ITextToSpeechPlayer : IDisposable {
    void Speak(string text);
}
