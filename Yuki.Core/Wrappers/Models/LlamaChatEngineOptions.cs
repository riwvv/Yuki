using LLama.Common;

namespace Yuki.Core.Wrappers.Models;

public record LlamaChatEngineOptions {
    public required string ModelPath { get; init; }
    public required ChatHistory ExistingHistory { get; init; }
    public uint ContextSize { get; init; } = 4096;
    public int GpuLayerCount { get; init; } = 99;
}
