using LLama;
using LLama.Common;
using LLama.Sampling;
using Yuki.Core.Wrappers.Contracts;
using Yuki.Core.Wrappers.Models;

namespace Yuki.Core.Wrappers.Services;

public class LlamaChatEngine : ILlamaChatEngine {
    private const float Temperature = 0.6f;
    private const float RepeatPenalty = 1.1f;
    private const float PresencePenalty = 0.3f;
    private const float FrequencyPenalty = 0.3f;
    private const int TopK = 40;
    private const float TopP = 0.9f;
    private const int MaxTokensPerResponse = 256;

    private readonly LLamaWeights _weights;
    private readonly LLamaContext _context;
    private readonly InteractiveExecutor _executor;
    private readonly ChatHistory _chat;
    private readonly ChatSession _session;

    public LlamaChatEngine(LlamaChatEngineOptions settings) {
        _weights = settings.Weights;

        var contextParams = new ModelParams(string.Empty) {
            ContextSize = settings.ContextSize,
        };

        _context = _weights.CreateContext(contextParams);
        _executor = new InteractiveExecutor(_context);
        _chat = settings.ExistingHistory;

        _session = new ChatSession(_executor, _chat)
            .WithHistoryTransform(new ModelChatTemplateTransform(_weights));
    }

    public IAsyncEnumerable<string> RespondAsync(string userMessage, Grammar? grammar = null, CancellationToken cancellationToken = default) {
        var inferenceParams = new InferenceParams {
            MaxTokens = MaxTokensPerResponse,
            AntiPrompts = ["<|im_end|>", "<|im_start|>"],
            SamplingPipeline = new DefaultSamplingPipeline {
                Grammar = grammar ?? default,
                Temperature = Temperature,
                TopK = TopK,
                TopP = TopP,
                RepeatPenalty = RepeatPenalty,
                PresencePenalty = PresencePenalty,
                FrequencyPenalty = FrequencyPenalty,
            }
        };

        return _session.ChatAsync(
            new ChatHistory.Message(AuthorRole.User, userMessage),
            inferenceParams,
            cancellationToken);
    }

    public void Dispose() => _context?.Dispose();
}