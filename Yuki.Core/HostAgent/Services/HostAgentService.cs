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
    private readonly ILlamaChatEngineFactory _factory;
    private readonly IServantAgentService _servantAgent;
    private readonly IWorkingMemoryService _workingMemory;
    private readonly LlamaWeightsProvider _weightsProvider;
    private readonly ChatHistory _chatHistory;
    private readonly Grammar _userInteractionGrammar;
    private readonly Grammar _dialogOnlyGrammar;
    private readonly SemaphoreSlim _generationLock;
    private readonly Lock _reloadLock;
    private readonly ILogger<HostAgentService> _logger;
    private readonly IOptions<HostSettings> _hostSettings;
    private readonly IOptions<LlamaChatEngineSettings> _engineSettings;
    private readonly string _systemPrompt;
    private ILlamaChatEngine _engine;
    private int _currentGpuLayerCount;

    public event Action<string>? ResponseGenerated;

    public int CurrentGpuLayerCount {
        get {
            lock (_reloadLock)
                return _currentGpuLayerCount;
        }
    }

    public HostAgentService(
        ILlamaChatEngineFactory factory,
        LlamaWeightsProvider weightsProvider,
        IServantAgentService servantAgent,
        IWorkingMemoryService workingMemory,
        IOptions<HostSettings> hostSettings,
        IOptions<LlamaChatEngineSettings> engineSettings,
        ILogger<HostAgentService> logger) {
        _logger = logger;
        _hostSettings = hostSettings;
        _engineSettings = engineSettings;
        _reloadLock = new();
        _generationLock = new(1, 1);
        _chatHistory = new();
        _factory = factory;
        _weightsProvider = weightsProvider;
        _servantAgent = servantAgent;
        _workingMemory = workingMemory;

        _userInteractionGrammar = new Grammar(Utils.ReadGBNFFromFile("user_interaction.gbnf").TrimStart('\uFEFF'), "root");
        _dialogOnlyGrammar = new Grammar(Utils.ReadGBNFFromFile("dialog_only.gbnf").TrimStart('\uFEFF'), "root");

        _systemPrompt = Utils.ReadSystemPromptFromFile("HostAgentPrompt.txt").TrimStart('\uFEFF');
        if (string.IsNullOrEmpty(_systemPrompt))
            throw new InvalidOperationException($"Не удалось прочитать промпт '{nameof(_systemPrompt)}'");
        _chatHistory.AddMessage(AuthorRole.System, _systemPrompt);

        _engine = _factory.Create(new LlamaChatEngineOptions {
            Weights = _weightsProvider.Weights,
            ExistingHistory = _chatHistory,
            ContextSize = _hostSettings.Value.ContextSize
        });

        _currentGpuLayerCount = _weightsProvider.GpuLayerCount;
        _servantAgent.TaskCompleted += OnServantTaskCompleted;
    }

    public async IAsyncEnumerable<string> RespondToUserAsync(
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        var hadPendingConfirmation = _workingMemory.HasPendingConfirmation;
        var block = _workingMemory.BuildPromptBlock();
        var prompt = string.IsNullOrEmpty(block)
            ? userMessage
            : block + Environment.NewLine + Environment.NewLine + userMessage;
        if (!string.IsNullOrEmpty(block))
            _logger.LogInformation("Host: в реплику добавлен блок рабочей памяти");

        try {
            await foreach (var token in GenerateWithCompactionAsync(prompt, _userInteractionGrammar, true, cancellationToken))
                yield return token;
        }
        finally {
            // Ответ на вопрос про подтверждение — даже «нет» — закрывает его.
            // Если подтверждение прошло и задача делегирована, ProcessToolCall уже очистил ожидание.
            if (hadPendingConfirmation)
                _workingMemory.ClearPendingConfirmation();
        }
    }

    public IAsyncEnumerable<string> RespondToSystemEventAsync(
        string eventMessage,
        CancellationToken cancellationToken = default)
        => GenerateWithCompactionAsync(eventMessage, _dialogOnlyGrammar, false, cancellationToken);

    public IAsyncEnumerable<string> RespondToServantResultAsync(
        string result,
        CancellationToken cancellationToken = default) {
        var prompt = $"[Системное событие] Servant вернул отчёт о задаче. Результат: {result}. " +
                     "Сформулируй ответ пользователю, опираясь ТОЛЬКО на этот точный ответ и игнорируя любые свои предыдущие догадки.";
        return GenerateWithCompactionAsync(prompt, _dialogOnlyGrammar, false, cancellationToken);
    }

    public async Task SaySystemEventAsync(string eventMessage, CancellationToken cancellationToken = default) {
        await StreamAndInvokeResponseGeneratedAsync(RespondToSystemEventAsync(eventMessage, cancellationToken), cancellationToken);
    }

    public async Task ReloadEngineAsync(int gpuLayerCount, CancellationToken cancellationToken = default) {
        await _generationLock.WaitAsync(cancellationToken);
        try {
            RecreateEngine(gpuLayerCount);
        }
        finally {
            _generationLock.Release();
        }
    }

    private async IAsyncEnumerable<string> GenerateWithCompactionAsync(
        string prompt,
        Grammar grammar,
        bool allowToolCall,
        [EnumeratorCancellation] CancellationToken cancellationToken) {
        await _generationLock.WaitAsync(cancellationToken);
        try {
            await CompactHistoryIfNeededAsync(cancellationToken);
            await foreach (var token in GenerateResponseCoreAsync(prompt, grammar, allowToolCall, cancellationToken))
                yield return token;
        }
        finally {
            _generationLock.Release();
        }
    }

    private async IAsyncEnumerable<string> GenerateResponseCoreAsync(
        string prompt,
        Grammar grammar,
        bool allowToolCall,
        [EnumeratorCancellation] CancellationToken cancellationToken) {
        ILlamaChatEngine engine;
        lock (_reloadLock) engine = _engine;

        var fullJson = new StringBuilder();
        var stream = engine.RespondAsync(prompt, grammar, cancellationToken: cancellationToken);

        await foreach (var token in ExtractAndStreamMessage(stream, fullJson, cancellationToken))
            yield return token;

        var json = fullJson.ToString();
        _logger.LogDebug("Host: сырой JSON ответа: {Json}", json);

        var message = ParseMessage(json);
        if (!string.IsNullOrEmpty(message))
            _workingMemory.SetLastAssistantText(message);

        if (allowToolCall)
            ProcessToolCall(json);
    }

    private async Task CompactHistoryIfNeededAsync(CancellationToken cancellationToken) {
        var budget = ResolveHistoryTokenBudget();
        var plan = ChatHistoryCompactor.Plan(
            _chatHistory,
            budget,
            _hostSettings.Value.MaxHistoryBeforeTrim,
            _hostSettings.Value.KeepRecentMessages,
            CountTokens);

        if (plan is null)
            return;

        _logger.LogInformation(
            "Host: контекст {Tokens} токенов / {Count} сообщений — сжимаю историю",
            plan.TokenCount,
            _chatHistory.Messages.Count);

        var system = _chatHistory.Messages.Where(m => m.AuthorRole == AuthorRole.System).ToList();
        var summary = await GenerateSummaryAsync(plan.ToSummarize, cancellationToken);
        var compacted = ChatHistoryCompactor.BuildCompacted(system, summary, plan.Recent, budget, CountTokens);
        ChatHistoryCompactor.Replace(_chatHistory, compacted);

        RecreateEngine();
    }

    private async Task<string> GenerateSummaryAsync(
        IReadOnlyList<ChatHistory.Message> messages,
        CancellationToken cancellationToken) {
        var summaryHistory = new ChatHistory();
        summaryHistory.AddMessage(AuthorRole.System,
            "Ты сжимаешь историю диалога Юки. Отвечай только коротким JSON с полем message.");

        using var summaryEngine = _factory.Create(new LlamaChatEngineOptions {
            Weights = _weightsProvider.Weights,
            ExistingHistory = summaryHistory,
            ContextSize = _hostSettings.Value.ContextSize
        });

        var sb = new StringBuilder();
        await foreach (var token in summaryEngine.RespondAsync(
                           ChatHistoryCompactor.BuildSummaryPrompt(messages),
                           _dialogOnlyGrammar,
                           cancellationToken))
            sb.Append(token);

        var summary = ParseMessage(sb.ToString());
        if (string.IsNullOrWhiteSpace(summary)) {
            _logger.LogWarning("Host: не удалось получить резюме — использую короткий fallback");
            return "Предыдущая часть диалога была длинной; подробности потеряны.";
        }

        return TrimByTokenBudget(summary, _hostSettings.Value.SummaryMaxTokens);
    }

    private string TrimByTokenBudget(string text, int maxTokens) {
        if (maxTokens <= 0) return text;
        var tokens = CountTokens(text);
        if (tokens <= maxTokens) return text;

        var keepChars = Math.Max(1, text.Length * maxTokens / tokens);
        return text[..keepChars].TrimEnd() + "…";
    }

    private int ResolveHistoryTokenBudget() {
        var systemTokens = ChatHistoryCompactor.CountTokens(
            _chatHistory.Messages.Where(m => m.AuthorRole == AuthorRole.System),
            CountTokens);
        var reserve = _engineSettings.Value.MaxTokensPerResponse + 256;
        var dynamicBudget = (int)_hostSettings.Value.ContextSize - systemTokens - reserve;
        return Math.Clamp(
            Math.Min(_hostSettings.Value.HistoryTokenBudget, dynamicBudget),
            512,
            (int)_hostSettings.Value.ContextSize);
    }

    private int CountTokens(string text) => _weightsProvider.Weights.Tokenize(text, false, false, Encoding.UTF8).Length;

    private void RecreateEngine(int? gpuLayerCount = null) {
        ILlamaChatEngine oldEngine;
        lock (_reloadLock) oldEngine = _engine;
        oldEngine.Dispose();

        if (gpuLayerCount.HasValue)
            _weightsProvider.ReloadWeights(gpuLayerCount.Value);

        var newEngine = _factory.Create(new LlamaChatEngineOptions {
            Weights = _weightsProvider.Weights,
            ExistingHistory = _chatHistory,
            ContextSize = _hostSettings.Value.ContextSize
        });

        lock (_reloadLock) {
            _engine = newEngine;
            _currentGpuLayerCount = _weightsProvider.GpuLayerCount;
        }
    }

    private void ProcessToolCall(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tool_call", out var toolCall))
                return;

            if (!toolCall.TryGetProperty("args", out var args) ||
                !args.TryGetProperty("task", out var taskElement)) {
                _logger.LogWarning("Host: tool_call без args.task. Пропущено. JSON: {Json}",
                    json.Length > 400 ? json[..400] + "…" : json);
                return;
            }

            var task = taskElement.GetString();
            if (string.IsNullOrWhiteSpace(task)) {
                _logger.LogWarning("Host: tool_call с пустой задачей. Пропущено.");
                return;
            }

            _workingMemory.SetActiveTask(task);
            if (task.Contains("Пользователь подтвердил", StringComparison.OrdinalIgnoreCase))
                _workingMemory.ClearPendingConfirmation();

            _logger.LogInformation("Host: делегирую задачу Servant'у: {Task}", task);

            _ = Task.Run(async () => {
                try {
                    await _servantAgent.ExecuteTaskAsync(task);
                }
                catch (Exception ex) {
                    _logger.LogError(ex, "Host: задача Servant'а завершилась ошибкой");
                }
            });
        }
        catch (JsonException ex) {
            _logger.LogError(ex, "Ошибка парсинга JSON от Host");
        }
    }

    private async Task<string> StreamAndInvokeResponseGeneratedAsync(IAsyncEnumerable<string> stream, CancellationToken ct = default) {
        var sb = new StringBuilder();
        await foreach (var token in stream.WithCancellation(ct))
            sb.Append(token);

        var response = sb.ToString();
        ResponseGenerated?.Invoke(response);
        return response;
    }

    private void OnServantTaskCompleted(string result) => _ = HandleServantTaskCompletedAsync(result);

    private async Task HandleServantTaskCompletedAsync(string result) {
        try {
            if (LooksLikeConfirmationRequired(result)) {
                var task = _workingMemory.ActiveTask ?? ExtractConfirmationTask(result);
                _logger.LogInformation("Host: Servant запросил подтверждение — передаю в рабочую память");
                _workingMemory.SetPendingConfirmation(task, result);
            }
            _workingMemory.ClearActiveTask();

            if (string.IsNullOrWhiteSpace(result) ||
                result.StartsWith("Инструмент не вернул") ||
                result.StartsWith("Произошла ошибка") ||
                result.StartsWith("Задача превысила") ||
                result.StartsWith("Задача отменена")) {
                _logger.LogWarning("Host: отчёт Servant'а пуст или содержит ошибку: '{Result}'", result);
                ResponseGenerated?.Invoke("Не удалось выполнить задачу.");
                return;
            }

            _logger.LogInformation("Host: отчёт Servant'а — {Result}", result);
            var response = await StreamAndInvokeResponseGeneratedAsync(RespondToServantResultAsync(result));
            if (_workingMemory.HasPendingConfirmation)
                _workingMemory.UpdatePendingQuestion(response);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Host: ошибка при обработке отчёта Servant'а");
        }
    }

    private static bool LooksLikeConfirmationRequired(string result) =>
        result.Contains("confirmation_required", StringComparison.OrdinalIgnoreCase) ||
        result.Contains("нужно подтверждение", StringComparison.OrdinalIgnoreCase) ||
        result.Contains("требуется подтверждение", StringComparison.OrdinalIgnoreCase) ||
        result.Contains("нужно разрешение", StringComparison.OrdinalIgnoreCase);

    private static string ExtractConfirmationTask(string result) {
        var text = result.Trim();
        const string marker = "подтверждение:";
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
            text = text[(index + marker.Length)..].Trim();

        foreach (var tail in new[] { "Без него не делаю", "Без подтверждения не делаю" }) {
            var tailIndex = text.IndexOf(tail, StringComparison.OrdinalIgnoreCase);
            if (tailIndex >= 0)
                text = text[..tailIndex];
        }

        text = text.Trim().TrimEnd('.', ' ');
        return string.IsNullOrWhiteSpace(text) ? "подтвердить действие" : text;
    }

    private static string? ParseMessage(string json) {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("message", out var msgElem) &&
                msgElem.ValueKind == JsonValueKind.String)
                return msgElem.GetString();
        }
        catch (JsonException) { }

        return null;
    }

    private static async IAsyncEnumerable<string> ExtractAndStreamMessage(
        IAsyncEnumerable<string> stream,
        StringBuilder fullJson,
        [EnumeratorCancellation] CancellationToken cancellationToken) {
        await foreach (var token in stream.WithCancellation(cancellationToken))
            fullJson.Append(token);

        var message = ParseMessage(fullJson.ToString());
        if (!string.IsNullOrEmpty(message))
            yield return message;
    }

    public void Dispose() {
        _servantAgent.TaskCompleted -= OnServantTaskCompleted;
        _engine?.Dispose();
    }
}
