namespace Yuki.Core.Configurations;

public sealed class HostSettings {
    public required string ModelName { get; set; }
    public uint ContextSize { get; set; } = 4096;
    public int MaxHistoryBeforeTrim { get; set; } = 12;
}
