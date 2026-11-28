using LLama.Common;
using System.Text;

namespace Yuki.Core.HostAgent.Services;

public sealed record CompactionPlan(
    IReadOnlyList<ChatHistory.Message> ToSummarize,
    IReadOnlyList<ChatHistory.Message> Recent,
    int TokenCount);

/// <summary>
/// Чистая логика сжатия истории: работает с ChatHistory как с данными,
/// без обращения к LLM и без побочных эффектов.
/// </summary>
public static class ChatHistoryCompactor {
    public const string SummaryPrefix = "[Краткое резюме диалога] ";

    public static int CountTokens(IEnumerable<ChatHistory.Message> messages, Func<string, int> countTokens)
        => messages.Sum(message => countTokens(message.Content));

    public static CompactionPlan? Plan(
        ChatHistory history,
        int tokenBudget,
        int maxMessages,
        int keepRecent,
        Func<string, int> countTokens) {
        var nonSystem = history.Messages
            .Where(m => m.AuthorRole != AuthorRole.System)
            .ToList();

        // System-промпт живёт вне бюджета диалога: он одинаковый на каждом шаге
        // и не должен сам по себе вызывать сжатие.
        var totalTokens = CountTokens(nonSystem, countTokens);

        if (nonSystem.Count <= maxMessages && totalTokens <= tokenBudget)
            return null;

        // Всегда оставляем хотя бы одно сообщение для резюме, даже если история
        // короткая, но токенов уже слишком много.
        var recentCount = Math.Min(Math.Max(0, keepRecent), Math.Max(0, nonSystem.Count - 1));
        var toSummarize = nonSystem.Take(nonSystem.Count - recentCount).ToList();
        var recent = nonSystem.Skip(nonSystem.Count - recentCount).ToList();

        return new CompactionPlan(toSummarize, recent, totalTokens);
    }

    public static string BuildSummaryPrompt(IEnumerable<ChatHistory.Message> messages) {
        var sb = new StringBuilder();
        sb.AppendLine("Сожми историю диалога в 2–4 коротких предложения по-русски.");
        sb.AppendLine("Сохрани имена, предпочтения и незавершённые решения. Не выдумывай новых фактов.");
        sb.AppendLine("Не отвечай пользователю. Ответ строго JSON: {\"message\": \"...\"}");
        sb.AppendLine("История:");
        foreach (var message in messages)
            sb.AppendLine($"{RoleName(message.AuthorRole)}: {message.Content}");
        return sb.ToString();
    }

    public static IReadOnlyList<(AuthorRole Role, string Content)> BuildCompacted(
        IEnumerable<ChatHistory.Message> system,
        string summary,
        IEnumerable<ChatHistory.Message> recent,
        int tokenBudget,
        Func<string, int> countTokens,
        int minRecent = 1) {
        var systemMessages = system.ToList();
        var recentMessages = recent.ToList();
        var summaryContent = string.IsNullOrWhiteSpace(summary)
            ? null
            : SummaryPrefix + summary.Trim();

        while (true) {
            var result = new List<(AuthorRole Role, string Content)>();
            result.AddRange(systemMessages.Select(m => (m.AuthorRole, m.Content)));
            if (summaryContent is not null)
                result.Add((AuthorRole.Assistant, summaryContent));
            result.AddRange(recentMessages.Select(m => (m.AuthorRole, m.Content)));

            // Бюджет относится только к «живой» части истории. System-промпт
            // не учитывается, иначе сжатие срабатывает на каждой реплике.
            var tokens = summaryContent is null ? 0 : countTokens(summaryContent);
            tokens += recentMessages.Sum(message => countTokens(message.Content));
            if (tokens <= tokenBudget || recentMessages.Count <= minRecent)
                return result;

            recentMessages.RemoveAt(0);
        }
    }

    public static void Replace(ChatHistory history, IEnumerable<(AuthorRole Role, string Content)> messages) {
        history.Messages.Clear();
        foreach (var message in messages)
            history.AddMessage(message.Role, message.Content);
    }

    private static string RoleName(AuthorRole role) => role switch {
        AuthorRole.System => "SYSTEM",
        AuthorRole.Assistant => "ЮКИ",
        _ => "ПОЛЬЗОВАТЕЛЬ"
    };
}
