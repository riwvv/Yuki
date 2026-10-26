namespace Yuki.Core.HostAgent.Contracts;

public interface IHostAgentService : IDisposable {
    int CurrentGpuLayerCount { get; }
    IAsyncEnumerable<string> RespondToUserAsync(string userMessage, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> RespondToSystemEventAsync(string eventMessage, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> RespondToServantResultAsync(string result, CancellationToken cancellationToken = default);
    Task SaySystemEventAsync(string eventMessage, CancellationToken cancellationToken = default);
    void ReloadEngine(int gpuLayerCount);
    event Action<string>? ResponseGenerated;
}
