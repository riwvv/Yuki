using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yuki.Core.Configurations;
using Yuki.Core.HostAgent.Contracts;
using Yuki.Core.ResourceManagement.Contracts;
using Yuki.Core.Wrappers.Contracts;
using Yuki.Core.Wrappers.Models;
using Yuki.Core.Wrappers.Utils;

namespace Yuki.Core.HostAgent.Services;

public class HostAgentService : IHostAgentService, IDisposable {
    private readonly ILlamaChatEngine _engine;
    public HostAgentService(ILlamaChatEngineFactory factory, IOptions<HostSettings> settings, IGpuLayerResolver gpuLayerResolver, ILogger<HostAgentService> logger) {
        const int contextSize = 4096;
        var gpuLayers = gpuLayerResolver.ResolveGpuLayers(settings.Value.ModelPath, contextSize);
        logger.LogInformation($"Resource manager: выделяю {gpuLayers} слоёв на GPU для Host");
        _engine = factory.Create(new LlamaChatEngineOptions {
            ModelPath = settings.Value.ModelPath,
            SystemPrompt = Utils.ReadSystemPromptFromFile("HostAgentPrompt.txt"),
            GpuLayerCount = gpuLayers
        });
    }

    public IAsyncEnumerable<string> RespondAsync(string userMessage, CancellationToken cancellationToken = default) => _engine.RespondAsync(userMessage, cancellationToken);

    public void Dispose() => _engine.Dispose();
}
