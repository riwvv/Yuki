namespace Yuki.Core.HostAgent.Contracts;

public interface IHostAgentService {
    IAsyncEnumerable<string> RespondAsync(string userMessage, CancellationToken cancellationToken = default);
    void ReloadEngine(int gpuLayerCount);
}
