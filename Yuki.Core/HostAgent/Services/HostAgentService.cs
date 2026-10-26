using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text;
using LLama.Sampling;
using LLama.Common;
using Yuki.Core.ServantAgent.Contracts;
using Yuki.Core.HostAgent.Contracts;
using Yuki.Core.Wrappers.Contracts;
using Yuki.Core.Wrappers.Models;
using Yuki.Core.Wrappers.Utils;
using Yuki.Core.Configurations;
using Yuki.Core.Wrappers.Services;

namespace Yuki.Core.HostAgent.Services;

public class HostAgentService : IHostAgentService {
    private const int CONTEXT_SIZE = 2048;
    private readonly ILlamaChatEngineFactory _factory;
    private readonly IServantAgentService _servantAgent;
    private readonly LlamaWeightsProvider _weightsProvider;
    private readonly ChatHistory _chatHistory;
    private readonly Grammar _userInteractionGrammar;
    private readonly Grammar _dialogOnlyGrammar;
    private readonly SemaphoreSlim _generationLock;
    private readonly Lock _reloadLock;
    private readonly ILogger<HostAgentService> _logger;
    private ILlamaChatEngine _engine;
    private int _currentGpuLayerCount;

    public event Action<string>? ResponseGenerated;

    public int CurrentGpuLayerCount {
        get { lock (_reloadLock) return _currentGpuLayerCount; }
    }

    public HostAgentService(ILlamaChatEngineFactory factory, LlamaWeightsProvider weightsProvider, IServantAgentService servantAgent, IOptions<HostSettings> hostSettings, ILogger<HostAgentService> logger) {
        _logger = logger;
        _reloadLock = new();
        _generationLock = new(1, 1);
        _chatHistory = new();
        _factory = factory;
        _weightsProvider = weightsProvider;
        _servantAgent = servantAgent;

        _userInteractionGrammar = new Grammar(Utils.ReadGBNFFromFile("user_interaction.gbnf"), "root");
        _dialogOnlyGrammar = new Grammar(Utils.ReadGBNFFromFile("dialog_only.gbnf"), "root");

        var systemPrompt = Utils.ReadSystemPromptFromFile("HostAgentPrompt.txt");
        if (string.IsNullOrEmpty(systemPrompt)) throw new InvalidOperationException($"Не удалось прочитать промпт '{nameof(systemPrompt)}'");
        _chatHistory.AddMessage(AuthorRole.System, systemPrompt);

        _engine = _factory.Create(new LlamaChatEngineOptions {
            Weights = _weightsProvider.Weights,
            ExistingHistory = _chatHistory,
            ContextSize = (uint)CONTEXT_SIZE
        });

        _currentGpuLayerCount = _weightsProvider.GpuLayerCount;
        _servantAgent.TaskCompleted += OnServantTaskCompleted;
    }

    public IAsyncEnumerable<string> RespondToUserAsync(string userMessage, CancellationToken cancellationToken = default) => GenerateResponseAsync(userMessage, _userInteractionGrammar, true, cancellationToken);

    public IAsyncEnumerable<string> RespondToSystemEventAsync(string eventMessage, CancellationToken cancellationToken = default) => GenerateResponseAsync(eventMessage, _dialogOnlyGrammar, false, cancellationToken);

    public IAsyncEnumerable<string> RespondToServantResultAsync(string result, CancellationToken cancellationToken = default) {
        var prompt = $"[Системное событие] Servant вернул отчёт о задаче. Результат: {result}. Сформулируй ответ пользователю, опираясь ТОЛЬКО на этот точный ответ и игнорируя любые свои предыдущие догадки.";
        return GenerateResponseAsync(prompt, _dialogOnlyGrammar, false, cancellationToken);
    }

    public async Task SaySystemEventAsync(string eventMessage, CancellationToken cancellationToken = default) => await StreamAndInvokeResponseGeneratedAsync(RespondToSystemEventAsync(eventMessage, cancellationToken), cancellationToken);

    public void ReloadEngine(int gpuLayerCount) {
        _generationLock.Wait();
        try {
            ILlamaChatEngine oldEngine;
            lock (_reloadLock) {
                oldEngine = _engine;
            }
            oldEngine.Dispose();

            _weightsProvider.ReloadWeights(gpuLayerCount);

            var newEngine = _factory.Create(new LlamaChatEngineOptions {
                Weights = _weightsProvider.Weights,
                ExistingHistory = _chatHistory,
                ContextSize = (uint)CONTEXT_SIZE
            });

            lock (_reloadLock) {
                _engine = newEngine;
                _currentGpuLayerCount = gpuLayerCount;
            }
        }
        finally {
            _generationLock.Release();
        }
    }

    private async IAsyncEnumerable<string> GenerateResponseAsync(string prompt, Grammar grammar, bool allowToolCall, [EnumeratorCancellation] CancellationToken cancellationToken) {
        await _generationLock.WaitAsync(cancellationToken);
        try {
            ILlamaChatEngine engine;
            lock (_reloadLock) engine = _engine;

            var fullJson = new StringBuilder();
            var stream = engine.RespondAsync(prompt, grammar, cancellationToken: cancellationToken);

            await foreach (var token in ExtractAndStreamMessage(stream, fullJson, cancellationToken))
                yield return token;

            _logger.LogInformation($"Сырой JSON от Gemma: {fullJson}");

            if (allowToolCall) {
                ProcessToolCall(fullJson.ToString());
            }
        }
        finally {
            _generationLock.Release();
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
                    _ = _servantAgent.ExecuteTaskAsync(task);
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

    private async Task StreamAndInvokeResponseGeneratedAsync(IAsyncEnumerable<string> stream, CancellationToken ct = default) {
        var sb = new StringBuilder();
        await foreach (var token in stream.WithCancellation(ct)) {
            sb.Append(token);
        }
        ResponseGenerated?.Invoke(sb.ToString());
    }

    private async void OnServantTaskCompleted(string result) {
        _logger.LogInformation("Host: получил отчёт от Servant'а");
        await StreamAndInvokeResponseGeneratedAsync(RespondToServantResultAsync(result));
    }

    private static async IAsyncEnumerable<string> ExtractAndStreamMessage(IAsyncEnumerable<string> stream, StringBuilder fullJson, [EnumeratorCancellation] CancellationToken cancellationToken) {
        var messageBuffer = new StringBuilder();
        int state = 0;
        bool isEscaped = false;
        bool messageExtracted = false;

        await foreach (var token in stream.WithCancellation(cancellationToken)) {
            fullJson.Append(token);
            if (messageExtracted) continue;

            foreach (char c in token) {
                if (state == 0) {
                    if (c == ':') state = 1;
                }
                else if (state == 1) {
                    if (c == '"' || c == '“' || c == '”') state = 2;
                }
                else if (state == 2) {
                    if ((c == '"' || c == '”' || c == '“') && !isEscaped) {
                        state = 3;
                        break;
                    }

                    isEscaped = c == '\\' && !isEscaped;

                    if (!isEscaped || c != '\\')
                        messageBuffer.Append(c);
                }
            }

            if (state == 3) {
                messageExtracted = true;
                if (messageBuffer.Length > 0)
                    yield return messageBuffer.ToString();
            }
        }
    }

    public void Dispose() => _engine.Dispose();
}