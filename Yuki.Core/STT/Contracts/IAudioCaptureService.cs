namespace Yuki.Core.STT.Contracts;

public interface IAudioCaptureService : IDisposable{
    event Action<float[]>? AudioAvailable;
    void Start();
    void Stop();
}
