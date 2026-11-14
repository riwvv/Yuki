using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using LLama;
using LLama.Abstractions;
using LLama.Common;
using Yuki.Core.Configurations;
using Yuki.Core.ServantAgent.Contracts;
using Yuki.Core.Wrappers.Services;
using Yuki.Core.Wrappers.Utils;
using Yuki.Core.MCP.Contracts;
using ChatHistory = Microsoft.SemanticKernel.ChatCompletion.ChatHistory;

namespace Yuki.Core.ServantAgent.Services;

public sealed class ServantAgentService : IServantAgentService {
    private readonly LlamaWeightsProvider _weightsProvider;
    private readonly PromptExecutionSettings _settings;
    private readonly YukiChatCompletion _yukiChatCompletion;
    private readonly ModelParams _contextParams;
    private readonly IMcpManager _mcpManager;
    private readonly ILogger<ServantAgentService> _logger;
    private readonly TimeSpan _taskTimeout = TimeSpan.FromSeconds(60);
    private readonly SemaphoreSlim _taskLock;
    private readonly Lock _kernelLock;
    private readonly string _systemPrompt;

    private ILLamaExecutor _executor;
    private IChatCompletionService? _chatService;
    private Kernel? _kernel;

    public bool IsIdle => _taskLock.CurrentCount == 1;

    public event Action<string>? TaskCompleted;

    public ServantAgentService(LlamaWeightsProvider weightsProvider, IMcpManager mcpManager, IOptions<ServantContextParamsSettings> contextSettings, IOptions<YukiChatCompletionSettings> chatCompletionOptions, ILogger<ServantAgentService> logger, ILogger<YukiChatCompletion> chatCompletionLogger) {
        _logger = logger;
        _taskLock = new(1, 1);
        _kernelLock = new();
        _mcpManager = mcpManager;

        _systemPrompt = Utils.ReadSystemPromptFromFile("ServantAgentPrompt.txt").TrimStart('\uFEFF');

        _settings = new PromptExecutionSettings();

        _weightsProvider = weightsProvider;

        _contextParams = new ModelParams(string.Empty) {
            ContextSize = contextSettings.Value.ContextSize
        };

        _executor = new StatelessExecutor(_weightsProvider.Weights, _contextParams);

        _yukiChatCompletion = new YukiChatCompletion(() => _executor, new QwenChatMLTransform(), chatCompletionOptions.Value, chatCompletionLogger);

        _weightsProvider.WeightsReloaded += OnWeightsReloaded;
    }

    public async Task ExecuteTaskAsync(string taskDescription, CancellationToken ct = default) {
        await _taskLock.WaitAsync(ct);
        try {
            var kernel = GetOrCreateKernel();

            _logger.LogInformation($"Servant: начал работу над задачей - {taskDescription}");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_taskTimeout);

            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(_systemPrompt);
            chatHistory.AddUserMessage(taskDescription);

            var result = await _chatService!.GetChatMessageContentAsync(chatHistory, _settings, kernel, cts.Token);

            var resultText = string.IsNullOrWhiteSpace(result.Content) ? "Инструмент не вернул результат." : result.Content;

            _logger.LogInformation($"Servant: завершил задачу. Результат: {resultText}");
            TaskCompleted?.Invoke(resultText);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            _logger.LogWarning("Servant: задача отменена внешним токеном");
            TaskCompleted?.Invoke("Задача отменена.");
        }
        catch (OperationCanceledException) {
            _logger.LogWarning("Servant: задача превысила таймаут {Timeout}", _taskTimeout);
            TaskCompleted?.Invoke("Задача превысила допустимое время выполнения.");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Ошибка при выполнении задачи Servant'ом");
            TaskCompleted?.Invoke("Произошла ошибка при выполнении задачи.");
        }
        finally {
            _taskLock.Release();
        }
    }

    public async Task<bool> TryAcquireReloadLockAsync(TimeSpan timeout, CancellationToken ct = default) => await _taskLock.WaitAsync(timeout, ct);

    public void ReleaseReloadLock() => _taskLock.Release();

    private void OnWeightsReloaded(LLamaWeights newWeights) {
        _logger.LogInformation("Servant: веса перезагружены — пересоздаю executor");
        _executor = new StatelessExecutor(newWeights, _contextParams);
    }

    private Kernel GetOrCreateKernel() {
        if (_kernel != null)
            return _kernel;

        lock (_kernelLock) {
            _logger.LogInformation("Servant: инициализация Kernel с MCP");

            var tools = _mcpManager.GetAllTools();
            _logger.LogInformation($"Servant: получено {tools.Count()} инструментов из MCP");

            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton<IChatCompletionService>(_yukiChatCompletion);

            builder.Plugins.AddFromFunctions("McpTools", tools.Select(x => x.AsKernelFunction()));

            _kernel = builder.Build();
            _chatService = _kernel.GetRequiredService<IChatCompletionService>();

            return _kernel;
        }
    }

    public async ValueTask DisposeAsync() {
        _weightsProvider.WeightsReloaded -= OnWeightsReloaded;
        _taskLock.Dispose();
        await _mcpManager.DisposeAsync();
    }
}