namespace Yuki.Core.STT.Contracts;

public interface IWakeWordDetector : IDisposable {
    (bool detected, float probability) DetectWithProbability(float[] audioSamples);
    bool Detect(float[] audioSamples);
}
