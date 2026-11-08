using LLama;
using LLama.Common;
using LLama.Sampling;
using Yuki.Core.Configurations;
using Yuki.Core.Wrappers.Contracts;
using Yuki.Core.Wrappers.Models;

namespace Yuki.Core.Wrappers.Services;

public class LlamaChatEngine : ILlamaChatEngine {
    private readonly LLamaWeights _weights;
    private readonly LLamaContext _context;
    private readonly InteractiveExecutor _executor;
    private readonly ChatHistory _chat;
    private readonly ChatSession _session;
    private readonly LlamaChatEngineSettings _settings;

    public LlamaChatEngine(LlamaChatEngineOptions options, LlamaChatEngineSettings? settings = null) {
        _weights = options.Weights;
        _settings = settings ?? new LlamaChatEngineSettings();

        var contextParams = new ModelParams(string.Empty) {
            ContextSize = options.ContextSize,
        };

        _context = _weights.CreateContext(contextParams);
        _executor = new InteractiveExecutor(_context);
        _chat = options.ExistingHistory;

        _session = new ChatSession(_executor, _chat)
            .WithHistoryTransform(new ModelChatTemplateTransform(_weights));
    }

    public IAsyncEnumerable<string> RespondAsync(string userMessage, Grammar? grammar = null, CancellationToken cancellationToken = default) {
        var inferenceParams = new InferenceParams {
            MaxTokens = _settings.MaxTokensPerResponse,
            AntiPrompts = _settings.AntiPrompts,
            SamplingPipeline = new DefaultSamplingPipeline {
                Grammar = grammar ?? default,
                Temperature = _settings.Temperature,
                TopK = _settings.TopK,
                TopP = _settings.TopP,
                RepeatPenalty = _settings.RepeatPenalty,
                PresencePenalty = _settings.PresencePenalty,
                FrequencyPenalty = _settings.FrequencyPenalty,
            }
        };

        return _session.ChatAsync(
            new ChatHistory.Message(AuthorRole.User, userMessage),
            inferenceParams,
            cancellationToken);
    }

    public void Dispose() => _context?.Dispose();
}