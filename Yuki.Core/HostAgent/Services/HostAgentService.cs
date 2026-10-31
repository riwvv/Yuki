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
    private const int CONTEXT_SIZE = 4096;

    private const int MAX_HISTORY_BEFORE_TRIM = 12;

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

        _userInteractionGrammar = new Grammar(Utils.ReadGBNFFromFile("user_interaction.gbnf").TrimStart('\uFEFF'), "root");
        _dialogOnlyGrammar = new Grammar(Utils.ReadGBNFFromFile("dialog_only.gbnf").TrimStart('\uFEFF'), "root");

        var systemPrompt = Utils.ReadSystemPromptFromFile("HostAgentPrompt.txt").TrimStart('\uFEFF');
        if (string.IsNullOrEmpty(systemPrompt))
            throw new InvalidOperationException($"Не удалось прочитать промпт '{nameof(systemPrompt)}'");
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
        var prompt = $"[Системное событие] Servant вернул отчёт о задаче. Результат: {result}. " +
                     "Сформулируй ответ пользователю, опираясь ТОЛЬКО на этот точный ответ и игнорируя любые свои предыдущие догадки.";
        return GenerateResponseAsync(prompt, _dialogOnlyGrammar, false, cancellationToken);
    }

    public async Task SaySystemEventAsync(string eventMessage, CancellationToken cancellationToken = default) => await StreamAndInvokeResponseGeneratedAsync(RespondToSystemEventAsync(eventMessage, cancellationToken), cancellationToken);

    public async Task ReloadEngineAsync(int gpuLayerCount, CancellationToken cancellationToken = default) {
        await _generationLock.WaitAsync(cancellationToken);
        try {
            RecreateEngine(gpuLayerCount);
        }
        finally {
            _generationLock.Release();
        }
    }

    private void RecreateEngine(int? gpuLayerCount = null) {
        ILlamaChatEngine oldEngine;
        lock (_reloadLock) { oldEngine = _engine; }
        oldEngine.Dispose();

        if (gpuLayerCount.HasValue)
            _weightsProvider.ReloadWeights(gpuLayerCount.Value);

        var newEngine = _factory.Create(new LlamaChatEngineOptions {
            Weights = _weightsProvider.Weights,
            ExistingHistory = _chatHistory,
            ContextSize = (uint)CONTEXT_SIZE
        });

        lock (_reloadLock) {
            _engine = newEngine;
            _currentGpuLayerCount = _weightsProvider.GpuLayerCount;
        }
    }

    private async IAsyncEnumerable<string> GenerateResponseAsync(
        string prompt, Grammar grammar, bool allowToolCall,
        [EnumeratorCancellation] CancellationToken cancellationToken) {
        await _generationLock.WaitAsync(cancellationToken);
        try {
            TrimHistoryIfNeeded();

            ILlamaChatEngine engine;
            lock (_reloadLock) engine = _engine;

            var fullJson = new StringBuilder();
            var stream = engine.RespondAsync(prompt, grammar, cancellationToken: cancellationToken);

            await foreach (var token in ExtractAndStreamMessage(stream, fullJson, cancellationToken))
                yield return token;

            _logger.LogInformation($"Сырой JSON от Gemma: {fullJson}");

            if (allowToolCall)
                ProcessToolCall(fullJson.ToString());
        }
        finally {
            _generationLock.Release();
        }
    }

    private void TrimHistoryIfNeeded() {
        if (_chatHistory.Messages.Count <= MAX_HISTORY_BEFORE_TRIM) return;

        _logger.LogInformation("Host: контекст достиг {Count} сообщений — очищаю историю до System", _chatHistory.Messages.Count);

        var systemMsg = _chatHistory.Messages.FirstOrDefault(m => m.AuthorRole == AuthorRole.System);
        _chatHistory.Messages.Clear();
        if (systemMsg != null)
            _chatHistory.AddMessage(systemMsg.AuthorRole, systemMsg.Content);

        RecreateEngine();
    }

    private void ProcessToolCall(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tool_call", out var toolCall)) return;

            if (!toolCall.TryGetProperty("args", out var args) ||
                !args.TryGetProperty("task", out var taskElement)) {
                _logger.LogWarning("Host: tool_call без args.task. Пропущено. JSON: {Json}", json);
                return;
            }

            var task = taskElement.GetString();
            if (string.IsNullOrWhiteSpace(task)) {
                _logger.LogWarning("Host: tool_call с пустой задачей. Пропущено.");
                return;
            }

            _logger.LogInformation($"System: Host делегирует задачу Servant'у: {task}");
            _ = Task.Run(async () => {
                try { await _servantAgent.ExecuteTaskAsync(task); }
                catch (Exception ex) { _logger.LogError(ex, "Ошибка при выполнении задачи Servant'ом"); }
            });
        }
        catch (JsonException ex) {
            _logger.LogError(ex, "Ошибка парсинга JSON от Host");
        }
    }

    private async Task StreamAndInvokeResponseGeneratedAsync(IAsyncEnumerable<string> stream, CancellationToken ct = default) {
        var sb = new StringBuilder();
        await foreach (var token in stream.WithCancellation(ct))
            sb.Append(token);
        ResponseGenerated?.Invoke(sb.ToString());
    }

    private void OnServantTaskCompleted(string result) => _ = HandleServantTaskCompletedAsync(result);

    private async Task HandleServantTaskCompletedAsync(string result) {
        try {
            if (string.IsNullOrWhiteSpace(result) ||
                result.StartsWith("Инструмент не вернул") ||
                result.StartsWith("Произошла ошибка") ||
                result.StartsWith("Задача превысила") ||
                result.StartsWith("Задача отменена")) {
                _logger.LogWarning("Host: отчёт Servant'а пуст или содержит ошибку: '{Result}'", result);
                ResponseGenerated?.Invoke("Не удалось выполнить задачу.");
                return;
            }

            _logger.LogInformation("Host: получил отчёт от Servant'а");
            await StreamAndInvokeResponseGeneratedAsync(RespondToServantResultAsync(result));
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Ошибка при обработке отчёта Servant'а");
        }
    }

    private static async IAsyncEnumerable<string> ExtractAndStreamMessage(IAsyncEnumerable<string> stream, StringBuilder fullJson, [EnumeratorCancellation] CancellationToken cancellationToken) {
        await foreach (var token in stream.WithCancellation(cancellationToken))
            fullJson.Append(token);

        if (fullJson.Length == 0) 
            yield break;

        string? message = null;
        try {
            using var doc = JsonDocument.Parse(fullJson.ToString());
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("message", out var msgElem) &&
                msgElem.ValueKind == JsonValueKind.String) {
                message = msgElem.GetString();
            }
        }
        catch (JsonException) { }

        if (!string.IsNullOrEmpty(message))
            yield return message;
    }

    public void Dispose() {
        _servantAgent.TaskCompleted -= OnServantTaskCompleted;
        _engine?.Dispose();
    }
}