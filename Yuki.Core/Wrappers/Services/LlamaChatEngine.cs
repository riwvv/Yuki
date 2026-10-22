using LLama;
using LLama.Common;
using LLama.Sampling;
using Yuki.Core.Wrappers.Contracts;
using Yuki.Core.Wrappers.Models;

namespace Yuki.Core.Wrappers.Services;

public class LlamaChatEngine : ILlamaChatEngine {
    private readonly LLamaWeights _weights;
    private readonly LLamaContext _context;
    private readonly InteractiveExecutor _executor;
    private readonly ChatHistory _chat;
    private readonly ChatSession _session;

    public LlamaChatEngine(LlamaChatEngineOptions settings) {
        var modelParams = new ModelParams(settings.ModelPath) {
            ContextSize = settings.ContextSize,
            GpuLayerCount = settings.GpuLayerCount,
        };

        _weights = LLamaWeights.LoadFromFile(modelParams);
        _context = _weights.CreateContext(modelParams);
        _executor = new InteractiveExecutor(_context);
        _chat = settings.ExistingHistory;

        _session = new ChatSession(_executor, _chat).WithHistoryTransform(new ModelChatTemplateTransform(_weights));
    }

    public IAsyncEnumerable<string> RespondAsync(string userMessage, Grammar? grammar = null, CancellationToken cancellationToken = default) {
        var inferenceParams = new InferenceParams {
            MaxTokens = 512,
            AntiPrompts = ["<|im_end|>"]
        };

        // добавляем gbnf грамматику если удалось её получить
        if (grammar != null)
            inferenceParams.SamplingPipeline = new DefaultSamplingPipeline {
                Grammar = grammar
            };
        
        return _session.ChatAsync(new ChatHistory.Message(AuthorRole.User, userMessage), inferenceParams, cancellationToken);
    }
    public void Dispose() {
        _context?.Dispose();
        _weights?.Dispose();
    }
}
