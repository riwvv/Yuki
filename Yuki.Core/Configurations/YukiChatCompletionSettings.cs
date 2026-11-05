namespace Yuki.Core.Configurations;

public sealed class YukiChatCompletionSettings {
    public int MaxToolCallIterations { get; set; } = 7;
    public int MaxNudges { get; set; } = 2;
    public int DefaultMaxTokens { get; set; } = 512;
    public double DefaultTemperature { get; set; } = 0.6;
    public List<string> AntiPrompts { get; set; } = ["<|im_end|>", "<|im_start|>"];
    public string GrammarFileName { get; set; } = "tool_calling.gbnf";
    public string GrammarRootRule { get; set; } = "root";

    public int? TopK { get; set; }
    public float? TopP { get; set; }
    public float? RepeatPenalty { get; set; }
    public float? PresencePenalty { get; set; }
    public float? FrequencyPenalty { get; set; }
}
