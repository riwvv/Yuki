namespace Yuki.Core.Configurations;

public sealed class LlamaChatEngineSettings {
    public float Temperature { get; set; } = 0.6f;
    public float RepeatPenalty { get; set; } = 1.1f;
    public float PresencePenalty { get; set; } = 0.3f;
    public float FrequencyPenalty { get; set; } = 0.3f;
    public int TopK { get; set; } = 40;
    public float TopP { get; set; } = 0.9f;
    public int MaxTokensPerResponse { get; set; } = 384;
    public List<string> AntiPrompts { get; set; } = ["<|im_end|>", "<|im_start|>"];
}
