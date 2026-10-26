using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel;
using LLama;
using LLama.Common;
using LLamaSharp.SemanticKernel.ChatCompletion;
using Yuki.Core.ServantAgent.Contracts;
using Yuki.Core.Wrappers.Services;
using Yuki.Core.ServantAgent.Plugins;
using Yuki.Core.Wrappers.Utils;
using ChatHistory = Microsoft.SemanticKernel.ChatCompletion.ChatHistory;

namespace Yuki.Core.ServantAgent.Services;

public class ServantAgentService : IServantAgentService {
    private readonly Kernel _kernel;
    private readonly LLamaContext _context;
    private readonly IChatCompletionService _chatService;
    private readonly OpenAIPromptExecutionSettings _settings;
    private readonly ILogger<ServantAgentService> _logger;
    private readonly string _systemPrompt;
    private readonly SemaphoreSlim _taskLock;
    public event Action<string>? TaskCompleted;

    public ServantAgentService(LlamaWeightsProvider weightsProvider, ILogger<ServantAgentService> logger) {
        _logger = logger;
        _taskLock = new(1, 1);
        _systemPrompt = Utils.ReadSystemPromptFromFile("ServantAgentPrompt.txt");
        _settings = new OpenAIPromptExecutionSettings {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

        var contextParams = new ModelParams(string.Empty) {
            ContextSize = 4096
        };
        _context = weightsProvider.Weights.CreateContext(contextParams);
        var executor = new InteractiveExecutor(_context);

        var chatService = new LLamaSharpChatCompletion(executor);

        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton<IChatCompletionService>(chatService);

        builder.Plugins.AddFromType<SystemPlugins>();

        _kernel = builder.Build();
        _chatService = _kernel.GetRequiredService<IChatCompletionService>();
    }

    public async Task ExecuteTaskAsync(string taskDescription, CancellationToken ct = default) {
        await _taskLock.WaitAsync(ct);
        try {
            _logger.LogInformation($"Servant: начал работу над задачей - {taskDescription}");

            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(_systemPrompt);
            chatHistory.AddUserMessage(taskDescription);

            var result = await _chatService.GetChatMessageContentAsync(chatHistory, _settings, _kernel, cancellationToken: ct);
            var resultText = result.Content ?? "Инструмент не вернул результат.";

            _logger.LogInformation($"Servant: завершил задачу. Результат: {resultText}");
            TaskCompleted?.Invoke(resultText);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Ошибка при выполнении задачи Servant'ом");
            TaskCompleted?.Invoke("Произошла ошибка при выполнении задачи.");
        }
        finally {
            _taskLock.Release();
        }
    }

    public void Dispose() => _context?.Dispose();
}
