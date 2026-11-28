namespace Yuki.Core.Configurations;

public sealed class HostSettings {
    public required string ModelName { get; set; }
    public uint ContextSize { get; set; } = 4096;

    // Жёсткий предохранитель на число реплик. Основной триггер — бюджет токенов.
    public int MaxHistoryBeforeTrim { get; set; } = 12;
    public int HistoryTokenBudget { get; set; } = 1536;
    public int KeepRecentMessages { get; set; } = 6;
    public int SummaryMaxTokens { get; set; } = 200;
    public int WorkingMemoryTtlSeconds { get; set; } = 120;
}
