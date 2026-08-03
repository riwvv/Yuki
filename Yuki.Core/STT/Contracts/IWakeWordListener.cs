namespace Yuki.Core.STT.Contracts;

public interface IWakeWordListener : IDisposable {
    event Action<float> WakeWordDetected;
    void Start();
    void Stop();
}
