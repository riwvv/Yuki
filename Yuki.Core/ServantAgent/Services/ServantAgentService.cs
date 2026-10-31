using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using LLama;
using LLama.Abstractions;
using LLama.Common;
using Yuki.Core.ServantAgent.Contracts;
using Yuki.Core.ServantAgent.Plugins;
using Yuki.Core.Wrappers.Services;
using Yuki.Core.Wrappers.Utils;
using ChatHistory = Microsoft.SemanticKernel.ChatCompletion.ChatHistory;

namespace Yuki.Core.ServantAgent.Services;

public class ServantAgentService : IServantAgentService {
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatService;
    private readonly ILLamaExecutor _executor;
    private readonly OpenAIPromptExecutionSettings _settings;
    private readonly ILogger<ServantAgentService> _logger;
    private readonly string _systemPrompt;
    private readonly SemaphoreSlim _taskLock;
    private readonly TimeSpan _taskTimeout = TimeSpan.FromSeconds(60);

    public event Action<string>? TaskCompleted;

    public ServantAgentService(LlamaWeightsProvider weightsProvider, ILogger<ServantAgentService> logger) {
        _logger = logger;
        _taskLock = new(1, 1);

        _systemPrompt = Utils.ReadSystemPromptFromFile("ServantAgentPrompt.txt").TrimStart('\uFEFF');

        _settings = new OpenAIPromptExecutionSettings {
            MaxTokens = 512
        };

        var contextParams = new ModelParams(string.Empty) {
            ContextSize = 6144
        };

        _executor = new StatelessExecutor(weightsProvider.Weights, contextParams);

        var chatService = new YukiChatCompletion(_executor, new QwenChatMLTransform());

        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton<IChatCompletionService>(chatService);

        builder.Plugins.AddFromType<BrowserPlugin>();
        builder.Plugins.AddFromType<FileSystemPlugin>();
        builder.Plugins.AddFromType<MediaPlayerPlugin>();
        builder.Plugins.AddFromType<MiniGamePlugin>();
        builder.Plugins.AddFromType<SystemAudioPlugin>();
        builder.Plugins.AddFromType<SystemCommandPlugin>();
        builder.Plugins.AddFromType<SystemPlugins>();

        _kernel = builder.Build();
        _chatService = _kernel.GetRequiredService<IChatCompletionService>();
    }

    public async Task ExecuteTaskAsync(string taskDescription, CancellationToken ct = default) {
        await _taskLock.WaitAsync(ct);
        try {
            _logger.LogInformation($"Servant: начал работу над задачей - {taskDescription}");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_taskTimeout);

            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(_systemPrompt);
            chatHistory.AddUserMessage(taskDescription);

            var result = await _chatService.GetChatMessageContentAsync(chatHistory, _settings, _kernel, cts.Token);

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

    public void Dispose() {

    }
}