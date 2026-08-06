namespace Yuki.Core.STT.Contracts;

public interface IVoiceActivityDetector : IDisposable {
    void Reset();
    float GetSpeechProbability(float[] chunk);
}
