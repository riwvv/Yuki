using LLama;
using LLama.Common;
using Yuki.Core.Wrappers.Contracts;
using Yuki.Core.Wrappers.Models;

namespace Yuki.Core.Wrappers.Services;

public class LlamaChatEngine : ILlamaChatEngine {
    private readonly LLamaWeights _weights;
    private readonly LLamaContext _context;
    private readonly InteractiveExecutor _executor;
    private readonly InferenceParams _params;
    private readonly ChatHistory _chat;
    private readonly ChatSession _session;

    public LlamaChatEngine(LlamaChatEngineOptions options) {
        var modelParams = new ModelParams(options.ModelPath) {
            ContextSize = options.ContextSize,
            GpuLayerCount = options.GpuLayerCount,
        };

        _weights = LLamaWeights.LoadFromFile(modelParams);
        _context = _weights.CreateContext(modelParams);
        _executor = new InteractiveExecutor(_context);
        _chat = new ChatHistory();

        _params = new InferenceParams {
            MaxTokens = 512,
            AntiPrompts = ["User:", "<|im_end|>"]
        };

        if (!string.IsNullOrEmpty(options.SystemPrompt))
            _chat.AddMessage(AuthorRole.System, options.SystemPrompt);
        _session = new ChatSession(_executor, _chat);
    }

    public IAsyncEnumerable<string> RespondAsync(string userMessage, CancellationToken cancellationToken = default) => _session.ChatAsync(new ChatHistory.Message(AuthorRole.User, userMessage), _params, cancellationToken);
    
    public void Dispose() {
        _context?.Dispose();
        _weights?.Dispose();
    }
}
