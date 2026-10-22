using LLama.Common;
using LLama.Sampling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Yuki.Core.Configurations;
using Yuki.Core.HostAgent.Contracts;
using Yuki.Core.ResourceManagement.Contracts;
using Yuki.Core.ServantAgent.Contracts;
using Yuki.Core.Wrappers.Contracts;
using Yuki.Core.Wrappers.Models;
using Yuki.Core.Wrappers.Utils;

namespace Yuki.Core.HostAgent.Services;

public class HostAgentService : IHostAgentService, IDisposable {
    private const int CONTEXT_SIZE = 4096;
    private readonly ILlamaChatEngineFactory _factory;
    private readonly IServantAgentService _servantAgent;
    private readonly ChatHistory _chatHistory;
    private readonly Grammar _envelopeGrammar;
    private readonly SemaphoreSlim _generationLock;
    private readonly Lock _reloadLock;
    private readonly string _modelPath;
    private readonly ILogger<HostAgentService> _logger;
    private ILlamaChatEngine _engine;
    private int _currentGpuLayerCount;

    public int CurrentGpuLayerCount {
        get { 
            lock (_reloadLock) return _currentGpuLayerCount; 
        }
    }

    public HostAgentService(ILlamaChatEngineFactory factory, IGpuLayerResolver gpuLayerResolver, IServantAgentService servantAgent, IOptions<HostSettings> hostSettings, ILogger<HostAgentService> logger) {
        _logger = logger;
        _reloadLock = new();
        _generationLock = new(1, 1);
        _chatHistory = new();
        _modelPath = Utils.GetLlmModelPath(hostSettings.Value.ModelName);
        _factory = factory;
        _servantAgent = servantAgent;

        var gbnf = Utils.ReadGBNFFromFile("json.gbnf");
        _envelopeGrammar = new Grammar(gbnf, "root");

        var gpuLayers = gpuLayerResolver.ResolveGpuLayers(_modelPath, CONTEXT_SIZE);
        _logger.LogInformation($"Resource manager: выделяю {gpuLayers} слоёв на GPU для Host");

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

    public async IAsyncEnumerable<string> RespondAsync(string userMessage, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        await _generationLock.WaitAsync(cancellationToken);

        try {
            ILlamaChatEngine engine;
            lock (_reloadLock) engine = _engine;

            var fullJson = new StringBuilder();
            var stream = engine.RespondAsync(userMessage, _envelopeGrammar, cancellationToken: cancellationToken);

            await foreach (var token in ExtractAndStreamMessage(stream, fullJson, cancellationToken))
                yield return token;

            ProcessToolCall(fullJson.ToString());
        }
        finally {
            _generationLock.Release();
        }
    }

    public void ReloadEngine(int gpuLayerCount) {
        _generationLock.Wait();
        try {
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
        finally {
            _generationLock.Release();
        }

    }

    private async IAsyncEnumerable<string> ExtractAndStreamMessage(IAsyncEnumerable<string> stream, StringBuilder fullJson, [EnumeratorCancellation] CancellationToken cancellationToken) {
        var buffer = new StringBuilder();
        int state = 0;
        bool isEscaped = false;

        await foreach (var token in stream.WithCancellation(cancellationToken)) {
            fullJson.Append(token);
            buffer.Append(token);

            while (buffer.Length > 0) {
                char c = buffer[0];
                buffer.Remove(0, 1);

                if (state == 0) {
                    if (c == ':')
                        state = 1;
                }
                else if (state == 1) {
                    if (c == '"' && !isEscaped) {
                        state = 2;
                        break;
                    }

                    if (c == '\\' && !isEscaped)
                        isEscaped = true;
                    else
                        isEscaped = false;

                    if (!isEscaped || c != '\\')
                        yield return c.ToString();
                }
            }
        }
    }

    private void ProcessToolCall(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("tool_call", out var toolCall)) {
                var args = toolCall.GetProperty("args");
                var task = args.GetProperty("task").GetString();

                if (!string.IsNullOrWhiteSpace(task)) {
                    _logger.LogInformation($"System: Host делегирует задачу Servant'у: {task}");

                    // TODO реализовать выполнение команд у Servant'а
                    // _ = _servantAgent.ExecuteTaskAsync(task);
                }
            }
        }
        catch (JsonException ex) {
            _logger.LogError(ex, "Ошибка парсинга JSON от Host");
        }
        catch (KeyNotFoundException ex) {
            _logger.LogError(ex, "В JSON отсутствуют обязательные поля");
        }
    }

    public void Dispose() => _engine.Dispose();
}
