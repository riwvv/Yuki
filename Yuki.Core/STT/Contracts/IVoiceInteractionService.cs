namespace Yuki.Core.STT.Contracts; 

public interface IVoiceInteractionService : IDisposable {
    VoiceInteractionState State { get; }

    event Action<VoiceInteractionState>? StateChanged;
    Func<string, Task>? RequestReady { get; set; }

    void Start();
    void Stop();
    bool TryBeginBusy();
    void SayAsync(string prompt);
    void EndBusy();
}
