using LLama;
using LLama.Abstractions;
using LLama.Common;
using LLama.Sampling;
using LLamaSharp.SemanticKernel;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Yuki.Core.Configurations;
using Yuki.Core.Wrappers.Contracts;
using AuthorRole = Microsoft.SemanticKernel.ChatCompletion.AuthorRole;
using ChatHistory = Microsoft.SemanticKernel.ChatCompletion.ChatHistory;

namespace Yuki.Core.Wrappers.Services;

public sealed class YukiChatCompletion : IYukiChatCompletion {
    private readonly Func<ILLamaExecutor> _executorFactory;
    private readonly IHistoryTransform _historyTransform;
    private readonly YukiChatCompletionSettings _options;
    private readonly ILogger<YukiChatCompletion>? _logger;
    private readonly Lazy<Grammar> _grammar;

    public YukiChatCompletion(Func<ILLamaExecutor> executorFactory, IHistoryTransform? historyTransform = null, YukiChatCompletionSettings? options = null, ILogger<YukiChatCompletion>? logger = null) {
        _executorFactory = executorFactory ?? throw new ArgumentNullException(nameof(executorFactory));
        _historyTransform = historyTransform ?? new LLamaTransforms.DefaultHistoryTransform();
        _options = options ?? new YukiChatCompletionSettings();
        _logger = logger;

        _grammar = new Lazy<Grammar>(() => {
            var text = Utils.Utils.ReadGBNFFromFile(_options.GrammarFileName).TrimStart('\uFEFF');
            return new Grammar(text, _options.GrammarRootRule);
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(chatHistory);

        var workingHistory = new ChatHistory(chatHistory);
        var llamaSettings = executionSettings == null ? new LLamaSharpPromptExecutionSettings() : LLamaSharpPromptExecutionSettings.FromRequestSettings(executionSettings);

        var inferenceParams = BuildInferenceParams(llamaSettings);

        var executor = _executorFactory();

        var toolsPrompt = kernel != null ? BuildSystemPrompt(kernel) : null;

        var emptyNudgeCount = 0;
        var missingMessageNudgeCount = 0;

        for (int iteration = 0; iteration < _options.MaxToolCallIterations; iteration++) {
            cancellationToken.ThrowIfCancellationRequested();

            var prompt = BuildPromptFromHistory(workingHistory, toolsPrompt);
            var responseText = await InferOnceAsync(executor, prompt, inferenceParams, cancellationToken);

            if (string.IsNullOrWhiteSpace(responseText)) {
                _logger?.LogWarning($"Итерация {iteration}: пустой ответ модели");
                if (emptyNudgeCount < _options.MaxNudges) {
                    workingHistory.Add(new ChatMessageContent(AuthorRole.User,
                        "Твой предыдущий ответ был пустым. Ты ОБЯЗАН вернуть валидный JSON: " +
                        "{\"tool_call\": {\"name\": \"...\", \"arguments\": {...}}} или {\"message\": \"...\"}. " +
                        "Не оставляй ответ пустым."));
                    emptyNudgeCount++;
                    continue;
                }
                return Fail("Не получилось получить ответ. Попробуй переформулировать.");
            }

            if (TryParseToolCall(kernel, responseText, out var functionCall) && functionCall != null) {
                if (kernel == null) {
                    _logger?.LogWarning("Модель запросила tool_call, но kernel не передан");
                    return Fail("Модель запросила вызов инструмента, но Kernel не был передан в ChatCompletion.");
                }

                if (!FunctionExists(kernel, functionCall.FunctionName, functionCall.PluginName)) {
                    var available = string.Join(", ", kernel.Plugins.GetFunctionsMetadata().Select(f => f.Name));
                    _logger?.LogWarning("Модель запросила несуществующую функцию: {Function}", functionCall.FunctionName);
                    workingHistory.Add(new ChatMessageContent(AuthorRole.User,
                        $"Ошибка: функции '{functionCall.FunctionName}' не существует. " +
                        $"Доступные функции: {available}. Выбери правильную или верни {{\"message\": \"...\"}}."));
                    continue;
                }

                var validationError = ValidateArguments(kernel, functionCall.FunctionName, functionCall.Arguments);
                if (validationError != null) {
                    _logger?.LogWarning("Ошибка валидации аргументов {Function}: {Error}", functionCall.FunctionName, validationError);
                    workingHistory.Add(new ChatMessageContent(AuthorRole.User,
                        $"Ошибка аргументов для '{functionCall.FunctionName}': {validationError}. " +
                        "Верни корректный JSON с правильными аргументами."));
                    continue;
                }

                workingHistory.Add(new ChatMessageContent(AuthorRole.Assistant, [functionCall]));

                FunctionResultContent result;
                try {
                    result = await functionCall.InvokeAsync(kernel, cancellationToken);
                }
                catch (Exception ex) {
                    _logger?.LogError(ex, "Ошибка выполнения функции {Function}", functionCall.FunctionName);
                    workingHistory.Add(new ChatMessageContent(AuthorRole.User,
                        $"Ошибка выполнения '{functionCall.FunctionName}': {ex.Message}. " +
                        "Попробуй другие аргументы или другой инструмент, либо верни {\"message\": \"...\"}."));
                    continue;
                }

                workingHistory.Add(result.ToChatMessage());
                continue;
            }

            var finalText = ExtractMessageFromEnvelope(responseText);
            if (string.IsNullOrWhiteSpace(finalText)) {
                _logger?.LogWarning($"Итерация {iteration}: ответ без поля message: {responseText}");
                if (missingMessageNudgeCount < _options.MaxNudges) {
                    workingHistory.Add(new ChatMessageContent(AuthorRole.User, "Твой ответ не содержит поля 'message'. Верни {\"message\": \"...\"} с ответом."));
                    missingMessageNudgeCount++;
                    continue;
                }
                return Fail("Модель не сформировала корректный ответ.");
            }

            _logger?.LogInformation($"Задача выполнена за {iteration + 1} итераций");
            return [new ChatMessageContent(AuthorRole.Assistant, finalText)];
        }

        _logger?.LogWarning("Превышено число итераций tool_call: {Max}", _options.MaxToolCallIterations);
        return Fail("Слишком много шагов для выполнения. Попробуй упростить задачу.");
    }

    /// <summary>
    /// Fallback для SK, если он вызовет streaming-версию.
    /// Настоящего стриминга нет — отдаём результат одним куском,
    /// но не бросаем NotImplementedException.
    /// </summary>
    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        var results = await GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);
        foreach (var msg in results) {
            yield return new StreamingChatMessageContent(msg.Role, msg.Content ?? string.Empty);
        }
    }

    private InferenceParams BuildInferenceParams(LLamaSharpPromptExecutionSettings settings) {
        var antiPrompts = new List<string>(_options.AntiPrompts);
        if (settings.StopSequences is { Count: > 0 })
            antiPrompts.AddRange(settings.StopSequences);

        var temperature = settings.Temperature == default ? (float)_options.DefaultTemperature : (float)settings.Temperature;

        var defaults = new DefaultSamplingPipeline();

        var pipeline = new DefaultSamplingPipeline {
            Grammar = _grammar.Value,
            Temperature = temperature,
            TopK = _options.TopK ?? defaults.TopK,
            TopP = _options.TopP ?? defaults.TopP,
            RepeatPenalty = _options.RepeatPenalty ?? defaults.RepeatPenalty,
            PresencePenalty = _options.PresencePenalty ?? defaults.PresencePenalty,
            FrequencyPenalty = _options.FrequencyPenalty ?? defaults.FrequencyPenalty,
        };

        return new InferenceParams {
            MaxTokens = settings.MaxTokens ?? _options.DefaultMaxTokens,
            AntiPrompts = antiPrompts,
            SamplingPipeline = pipeline
        };
    }

    private static async Task<string> InferOnceAsync(ILLamaExecutor executor, string prompt, InferenceParams inferenceParams, CancellationToken ct) {
        var sb = new StringBuilder();
        await foreach (var token in executor.InferAsync(prompt, inferenceParams, ct)) {
            sb.Append(token);
        }
        return sb.ToString().Trim();
    }

    private static IReadOnlyList<ChatMessageContent> Fail(string message) => [new ChatMessageContent(AuthorRole.Assistant, message)];

    private static bool FunctionExists(Kernel kernel, string functionName, string? pluginName) {
        var functions = kernel.Plugins.GetFunctionsMetadata();
        return string.IsNullOrEmpty(pluginName) ? functions.Any(f => f.Name == functionName) : functions.Any(f => f.Name == functionName && f.PluginName == pluginName);
    }

    private static string? ValidateArguments(Kernel kernel, string functionName, KernelArguments? arguments) {
        var metadata = kernel.Plugins.GetFunctionsMetadata().FirstOrDefault(f => f.Name == functionName);
        if (metadata == null)
            return null;

        var missing = metadata.Parameters
            .Where(p => p.IsRequired && (arguments == null || !arguments.ContainsName(p.Name)))
            .Select(p => p.Name)
            .ToList();

        return missing.Count > 0 ? $"отсутствуют обязательные параметры: {string.Join(", ", missing)}" : null;
    }

    private static string BuildSystemPrompt(Kernel kernel) {
        var functions = kernel.Plugins.GetFunctionsMetadata();
        if (functions.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.Append("Тебе доступны следующие инструменты. Используй их только тогда, когда это необходимо. ");
        sb.Append("Если требуется инструмент, ответь ТОЛЬКО JSON-объектом: ");
        sb.Append("{\"tool_call\": {\"name\": \"function_name\", \"arguments\": {...}}}. ");
        sb.Append("Если инструмент не требуется, ответь ТОЛЬКО JSON-объектом: ");
        sb.Append("{\"message\": \"твой ответ\"}.");

        foreach (var func in functions) {
            sb.AppendLine($"- {func.Name}: {func.Description}");
            var parameters = string.Join(", ", func.Parameters.Select(x => $"{x.Name} ({x.ParameterType?.Name})"));
            sb.AppendLine($" Parameters: {parameters}");
        }
        return sb.ToString();
    }

    private string BuildPromptFromHistory(ChatHistory history, string? precomputedToolsPrompt) {
        var systemContents = history
            .Where(m => m.Role == AuthorRole.System)
            .Select(m => m.Content)
            .Where(c => !string.IsNullOrEmpty(c))
            .ToList();

        if (!string.IsNullOrEmpty(precomputedToolsPrompt))
            systemContents.Insert(0, precomputedToolsPrompt);

        var flattened = new ChatHistory();
        var combined = string.Join("\n\n", systemContents);
        if (!string.IsNullOrEmpty(combined))
            flattened.Add(new ChatMessageContent(AuthorRole.System, combined));

        foreach (var msg in history.Where(m => m.Role != AuthorRole.System)) {
            var role = msg.Role == AuthorRole.Tool ? AuthorRole.User : msg.Role;
            flattened.Add(new ChatMessageContent(role, FlattenMessage(msg)));
        }

        return _historyTransform.HistoryToText(flattened.ToLLamaSharpChatHistory());
    }

    private static string FlattenMessage(ChatMessageContent msg) {
        if (msg.Items.Count == 0)
            return msg.Content ?? string.Empty;

        var sb = new StringBuilder();
        foreach (var item in msg.Items) {
            switch (item) {
                case TextContent text:
                    sb.Append(text.Text);
                    break;
                case FunctionCallContent call:
                    var args = call.Arguments is null
                        ? "{}"
                        : "{" + string.Join(", ", call.Arguments.Select(kv => $"\"{kv.Key}\": {JsonSerializer.Serialize(kv.Value)}")) + "}";
                    sb.Append($"<tool_call>{{\"name\": \"{call.FunctionName}\", \"arguments\": {args}}}</tool_call>");
                    break;
                case FunctionResultContent res:
                    sb.Append($"<tool_result>{res.Result}</tool_result>");
                    break;
                default:
                    sb.Append(item.ToString());
                    break;
            }
        }
        return sb.ToString();
    }

    private static string ExtractMessageFromEnvelope(string responseText) {
        if (string.IsNullOrWhiteSpace(responseText))
            return string.Empty;

        try {
            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return responseText;

            if (!root.TryGetProperty("message", out var messageElement)) return responseText;

            return messageElement.ValueKind switch {
                JsonValueKind.String => messageElement.GetString() ?? string.Empty,
                JsonValueKind.Null => string.Empty,
                _ => messageElement.GetRawText()
            };
        }
        catch (JsonException) {
            return responseText;
        }
    }

    private static bool TryParseToolCall(Kernel? kernel, string responseText, out FunctionCallContent? functionCall) {
        functionCall = null;
        if (string.IsNullOrWhiteSpace(responseText)) 
            return false;

        try {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("tool_call", out var toolCallElement))
                return false;

            if (!toolCallElement.TryGetProperty("name", out var nameElement))
                return false;

            var functionName = nameElement.GetString();
            if (string.IsNullOrEmpty(functionName)) 
                return false;

            string? pluginName = null;
            if (kernel != null) {
                var metadata = kernel.Plugins.GetFunctionsMetadata()
                    .FirstOrDefault(f => f.Name == functionName);
                pluginName = metadata?.PluginName;
            }

            KernelArguments? arguments = null;
            if (toolCallElement.TryGetProperty("arguments", out var argsElement) && argsElement.ValueKind == JsonValueKind.Object) {
                try {
                    var argsDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(argsElement.GetRawText());
                    if (argsDict != null) {
                        var converted = argsDict.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value is JsonElement je ? ConvertJsonElement(je) : kvp.Value);
                        arguments = new KernelArguments(converted);
                    }
                }
                catch (JsonException) {
                    // Битые arguments — вернём tool_call с null, валидация ниже это поймает
                }
            }

            functionCall = new FunctionCallContent(
                pluginName: pluginName,
                functionName: functionName,
                arguments: arguments);
            return true;
        }
        catch (JsonException) {
            return false;
        }
    }

    private static object? ConvertJsonElement(JsonElement element) => element.ValueKind switch {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.GetRawText()
    };
}