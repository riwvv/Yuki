using Microsoft.SemanticKernel;
using System.Text;
using System.Text.Json;
using LLama;
using LLama.Common;
using LLama.Sampling;
using LLama.Abstractions;
using LLamaSharp.SemanticKernel;
using Yuki.Core.Wrappers.Contracts;
using AuthorRole = Microsoft.SemanticKernel.ChatCompletion.AuthorRole;
using ChatHistory = Microsoft.SemanticKernel.ChatCompletion.ChatHistory;

namespace Yuki.Core.Wrappers.Services;

public class YukiChatCompletion(ILLamaExecutor executor, IHistoryTransform? historyTransform = null) : IYukiChatCompletion {
    private const int MAX_TOOL_CALL_ITERATIONS = 7;
    private readonly ILLamaExecutor _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    private readonly IHistoryTransform _historyTransform = historyTransform ?? new LLamaTransforms.DefaultHistoryTransform();

    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default) {
        var workingHistory = new ChatHistory(chatHistory);
        var llamaSettings = executionSettings == null ? new LLamaSharpPromptExecutionSettings() : LLamaSharpPromptExecutionSettings.FromRequestSettings(executionSettings);
        var grammarText = Utils.Utils.ReadGBNFFromFile("tool_calling.gbnf");
        var grammar = new Grammar(grammarText, "root");
        var inferenceParams = new InferenceParams {
            MaxTokens = llamaSettings.MaxTokens ?? 512,
            AntiPrompts = ["<|im_end|>", "<|im_start|>"],
            SamplingPipeline = new DefaultSamplingPipeline {
                Temperature = (float)(llamaSettings.Temperature == default ? 0.0f : llamaSettings.Temperature),
                Grammar = grammar
            }
        };

        for (int iteration = 0; iteration < MAX_TOOL_CALL_ITERATIONS; iteration++) {
            var prompt = BuildPromptFromHistory(workingHistory, kernel);

            System.Diagnostics.Debug.WriteLine($"[YukiChatCompletion] PROMPT:\n{prompt}\n---END PROMPT---");

            var responseBuilder = new StringBuilder();
            await foreach (var token in _executor.InferAsync(prompt, inferenceParams, cancellationToken)) {
                responseBuilder.Append(token);
            }

            var responseText = responseBuilder.ToString().Trim();

            System.Diagnostics.Debug.WriteLine($"[YukiChatCompletion] RESPONSE:\n'{responseText}'\n---END RESPONSE---");

            if (TryParseToolCall(responseText, out var functionCall) && functionCall != null) {
                if (kernel == null)
                    return [new ChatMessageContent(AuthorRole.Assistant, "Модель запросила вызов инструмента, но Kernel не был передан в ChatCompletion.")];

                var assistantMessage = new ChatMessageContent(AuthorRole.Assistant, [functionCall]);
                workingHistory.Add(assistantMessage);

                FunctionResultContent result;
                try {
                    result = await functionCall.InvokeAsync(kernel, cancellationToken);
                }
                catch (Exception ex) {
                    result = new FunctionResultContent(functionCall.FunctionName, functionCall.PluginName, $"Ошибка выполнения функции: {ex.Message}");
                }

                workingHistory.Add(result.ToChatMessage());
                continue;
            }

            var finalText = ExtractMessageFromEnvelope(responseText);
            return [new ChatMessageContent(AuthorRole.Assistant, finalText)];
        }

        return [new ChatMessageContent(AuthorRole.Assistant, "Превышено максимальное число шагов вызова инструментов. Модель не смогла сформулировать финальный ответ.")];
    }

    public IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    private static string BuildSystemPrompt(Kernel kernel) {
        var functions = kernel.Plugins.GetFunctionsMetadata();
        if (functions.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.Append("You have access to the following tools. Use them only when needed. ");
        sb.Append("If a tool is needed, respond ONLY with a JSON object: ");
        sb.Append("{\"tool_call\": {\"name\": \"function_name\", \"arguments\": {...}}}. ");
        sb.Append("If no tool is needed, respond ONLY with a JSON object: ");
        sb.Append("{\"message\": \"your answer\"}.");

        foreach (var func in functions) {
            sb.AppendLine($"- {func.Name}: {func.Description}");
            var parameters = string.Join(", ", func.Parameters.Select(x => $"{x.Name} ({x.ParameterType?.Name})"));
            sb.AppendLine($" Parameters: {parameters}");
        }

        return sb.ToString();
    }

    private string BuildPromptFromHistory(ChatHistory history, Kernel? kernel) {
        var augmentedHistory = new ChatHistory(history);

        if (kernel != null) {
            var toolsPrompt = BuildSystemPrompt(kernel);
            if (!string.IsNullOrEmpty(toolsPrompt))
                augmentedHistory.Insert(0, new ChatMessageContent(AuthorRole.System, toolsPrompt));
        }

        var systemContents = augmentedHistory.Where(m => m.Role == AuthorRole.System).Select(m => m.Content).Where(c => !string.IsNullOrEmpty(c));
        var userAndAssistant = augmentedHistory.Where(m => m.Role != AuthorRole.System);

        var flattened = new ChatHistory();
        var combined = string.Join("\n\n", systemContents);
        if (!string.IsNullOrEmpty(combined))
            flattened.Add(new ChatMessageContent(AuthorRole.System, combined));
        foreach (var m in userAndAssistant) {
            var role = m.Role == AuthorRole.Tool ? AuthorRole.User : m.Role;
            flattened.Add(new ChatMessageContent(role, FlattenMessage(m)));
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

            if (root.ValueKind != JsonValueKind.Object)
                return responseText;

            if (root.TryGetProperty("message", out var messageElement)) {
                return messageElement.ValueKind switch {
                    JsonValueKind.String => messageElement.GetString() ?? string.Empty,
                    JsonValueKind.Null => string.Empty,
                    _ => messageElement.GetRawText()
                };
            }
            
            return responseText;
        }
        catch (JsonException) {
            return responseText;
        }
    }

    private bool TryParseToolCall(string responseText, out FunctionCallContent? functionCall) {
        functionCall = null;
        if (string.IsNullOrWhiteSpace(responseText)) return false;

        try {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("tool_call", out var toolCallElement))
                return false;

            if (!toolCallElement.TryGetProperty("name", out var nameElement))
                return false;

            var functionName = nameElement.GetString();
            if (string.IsNullOrEmpty(functionName)) return false;

            KernelArguments? arguments = null;
            if (toolCallElement.TryGetProperty("arguments", out var argsElement) &&
                argsElement.ValueKind == JsonValueKind.Object) {
                var argsDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                    argsElement.GetRawText());
                if (argsDict != null) {
                    var converted = argsDict.ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value is JsonElement je ? ConvertJsonElement(je) : kvp.Value);
                    arguments = new KernelArguments(converted);
                }
            }

            functionCall = new FunctionCallContent(
                functionName: functionName,
                arguments: arguments);

            return true;
        }
        catch (JsonException) {
            return false;
        }
    }

    private static object? ConvertJsonElement(JsonElement element) {
        return element.ValueKind switch {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }
}
