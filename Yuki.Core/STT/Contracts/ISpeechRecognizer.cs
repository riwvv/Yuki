namespace Yuki.Core.STT.Contracts;

public interface ISpeechRecognizer : IDisposable {
    void Reset();
    void AcceptAudio(float[] chunk);
    string GetFinalResult();
}
