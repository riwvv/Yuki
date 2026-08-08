namespace Yuki.Core.STT.Contracts; 

public interface IVoiceInteractionService : IDisposable {
    VoiceInteractionState State { get; }

    event Action<VoiceInteractionState>? StateChanged;
    event Action<string>? ResponseReady;

    void Start();
    void Stop();
    bool TryBeginBusy();
    Task SayAsync(string prompt);
    void EndBusy();
}
