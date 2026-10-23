namespace Yuki.Core.TTS.Contracts;

public interface ITextToSpeechPlayer : IDisposable {
    void EnqueueText(string text);
}
