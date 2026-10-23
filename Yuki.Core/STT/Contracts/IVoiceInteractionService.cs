namespace Yuki.Core.STT.Contracts; 

public interface IVoiceInteractionService : IDisposable {
    VoiceInteractionState State { get; }

    event Action<VoiceInteractionState>? StateChanged;
    event Action<string>? RequestReady;

    void Start();
    void Stop();
    bool TryBeginBusy();
    void SayAsync(string prompt);
    void EndBusy();
}
