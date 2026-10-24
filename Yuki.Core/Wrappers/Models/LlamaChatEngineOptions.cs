using LLama;
using LLama.Common;

namespace Yuki.Core.Wrappers.Models;

public record LlamaChatEngineOptions {
    public required LLamaWeights Weights { get; init; }
    public required ChatHistory ExistingHistory { get; init; }
    public uint ContextSize { get; init; } = 4096;
}
