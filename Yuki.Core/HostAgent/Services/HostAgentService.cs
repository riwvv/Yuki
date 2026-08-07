using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LLama.Common;
using Yuki.Core.Configurations;
using Yuki.Core.HostAgent.Contracts;
using Yuki.Core.ResourceManagement.Contracts;
using Yuki.Core.Wrappers.Contracts;
using Yuki.Core.Wrappers.Models;
using Yuki.Core.Wrappers.Utils;

namespace Yuki.Core.HostAgent.Services;

public class HostAgentService : IHostAgentService, IDisposable {
    public int CurrentGpuLayerCount {
        get { 
            lock (_reloadLock) return _currentGpuLayerCount; 
        }
    }

    private const int CONTEXT_SIZE = 4096;
    private readonly ChatHistory _chatHistory;
    private readonly ILlamaChatEngineFactory _factory;
    private readonly Lock _reloadLock = new();
    private readonly string _modelPath;
    private ILlamaChatEngine _engine;
    private int _currentGpuLayerCount;

    public HostAgentService(ILlamaChatEngineFactory factory, IOptions<HostSettings> hostSettings, IGpuLayerResolver gpuLayerResolver, ILogger<HostAgentService> logger) {
        _chatHistory = new ChatHistory();
        _modelPath = hostSettings.Value.ModelPath;
        _factory = factory;

        var gpuLayers = gpuLayerResolver.ResolveGpuLayers(_modelPath, CONTEXT_SIZE);
        logger.LogInformation($"Resource manager: выделяю {gpuLayers} слоёв на GPU для Host");

        var systemPrompt = Utils.ReadSystemPromptFromFile("HostAgentPrompt.txt");
        if (string.IsNullOrEmpty(systemPrompt)) throw new InvalidOperationException($"Не удалось прочитать промпт '{nameof(systemPrompt)}'");
        _chatHistory.AddMessage(AuthorRole.System, systemPrompt);

        _engine = _factory.Create(new LlamaChatEngineOptions {
            ModelPath = _modelPath,
            ExistingHistory = _chatHistory,
            ContextSize = (uint)CONTEXT_SIZE,
            GpuLayerCount = gpuLayers
        });
        _currentGpuLayerCount = gpuLayers;
    }

    public IAsyncEnumerable<string> RespondAsync(string userMessage, CancellationToken cancellationToken = default) {
        ILlamaChatEngine engine;
        lock (_reloadLock) engine = _engine;
        return engine.RespondAsync(userMessage, cancellationToken);
    }

    public void ReloadEngine(int gpuLayerCount) {
        var newEngine = _factory.Create(new LlamaChatEngineOptions {
            ModelPath = _modelPath,
            ExistingHistory = _chatHistory,
            ContextSize = (uint)CONTEXT_SIZE,
            GpuLayerCount = gpuLayerCount
        });

        ILlamaChatEngine oldEngine;
        lock (_reloadLock) {
            oldEngine = _engine;
            _engine = newEngine;
            _currentGpuLayerCount = gpuLayerCount;
        }

        oldEngine.Dispose();
    }

    public void Dispose() => _engine.Dispose();
}
