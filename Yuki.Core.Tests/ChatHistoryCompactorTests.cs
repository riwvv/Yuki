using LLama.Common;
using Yuki.Core.HostAgent.Services;

namespace Yuki.Core.Tests;

public class ChatHistoryCompactorTests {
    private static readonly Func<string, int> OneTokenPerMessage = _ => 1;

    private static ChatHistory History(int nonSystemMessages) {
        var history = new ChatHistory();
        history.AddMessage(AuthorRole.System, "SYSTEM");
        for (var i = 0; i < nonSystemMessages; i++)
            history.AddMessage(i % 2 == 0 ? AuthorRole.User : AuthorRole.Assistant, $"msg{i}");
        return history;
    }

    [Fact]
    public void Plan_ReturnsNull_WhenHistoryFitsBudgetAndMessageCap() {
        var history = History(4);

        var plan = ChatHistoryCompactor.Plan(history, tokenBudget: 10, maxMessages: 10, keepRecent: 2, OneTokenPerMessage);

        Assert.Null(plan);
    }

    [Fact]
    public void Plan_SplitsOlderMessagesIntoSummary_AndKeepsRecent() {
        var history = History(4);

        var plan = ChatHistoryCompactor.Plan(history, tokenBudget: 2, maxMessages: 2, keepRecent: 2, OneTokenPerMessage);

        Assert.NotNull(plan);
        Assert.Equal(2, plan.ToSummarize.Count);
        Assert.Equal(2, plan.Recent.Count);
        Assert.Equal("msg2", plan.Recent[0].Content);
        Assert.Equal("msg3", plan.Recent[1].Content);
    }

    [Fact]
    public void BuildCompacted_KeepsSystemSummaryAndRecent() {
        var history = History(4);
        var system = history.Messages.Where(m => m.AuthorRole == AuthorRole.System);

        var result = ChatHistoryCompactor.BuildCompacted(
            system,
            "Юки и пользователь поболтали.",
            [.. history.Messages.Where(m => m.AuthorRole != AuthorRole.System).TakeLast(2)],
            tokenBudget: 10,
            OneTokenPerMessage);

        Assert.Equal(4, result.Count);
        Assert.Equal(AuthorRole.System, result[0].Role);
        Assert.Equal(ChatHistoryCompactor.SummaryPrefix + "Юки и пользователь поболтали.", result[1].Content);
        Assert.Equal("msg2", result[2].Content);
        Assert.Equal("msg3", result[3].Content);
    }

    [Fact]
    public void BuildCompacted_DropsOldestRecent_WhenStillOverBudget() {
        var history = History(4);
        var system = history.Messages.Where(m => m.AuthorRole == AuthorRole.System);

        var result = ChatHistoryCompactor.BuildCompacted(
            system,
            "Резюме",
            [.. history.Messages.Where(m => m.AuthorRole != AuthorRole.System).TakeLast(2)],
            tokenBudget: 2,
            OneTokenPerMessage,
            minRecent: 1);

        Assert.Equal(3, result.Count);
        Assert.Equal(ChatHistoryCompactor.SummaryPrefix + "Резюме", result[1].Content);
        Assert.Equal("msg3", result[2].Content);
    }

    [Fact]
    public void Plan_IgnoresSystemPromptTokens() {
        var history = new ChatHistory();
        history.AddMessage(AuthorRole.System, new string('S', 1000));
        history.AddMessage(AuthorRole.User, "short");

        var plan = ChatHistoryCompactor.Plan(
            history,
            tokenBudget: 10,
            maxMessages: 10,
            keepRecent: 2,
            text => text.Length);

        Assert.Null(plan);
    }

    [Fact]
    public void BuildCompacted_IgnoresSystemPromptTokens() {
        var system = new[] { new ChatHistory.Message(AuthorRole.System, new string('S', 1000)) };
        var recent = new[] { new ChatHistory.Message(AuthorRole.User, "msg") };

        var result = ChatHistoryCompactor.BuildCompacted(
            system,
            "Резюме",
            recent,
            tokenBudget: 10,
            text => text.Length);

        Assert.Equal(3, result.Count);
        Assert.Equal("msg", result[2].Content);
    }

    [Fact]
    public void BuildSummaryPrompt_MarksRolesAndAsksForJson() {
        var history = History(2);

        var prompt = ChatHistoryCompactor.BuildSummaryPrompt(
            history.Messages.Where(m => m.AuthorRole != AuthorRole.System));

        Assert.Contains("{\"message\":", prompt);
        Assert.Contains("ПОЛЬЗОВАТЕЛЬ: msg0", prompt);
        Assert.Contains("ЮКИ: msg1", prompt);
    }

    [Fact]
    public void Replace_RewritesHistory() {
        var history = History(4);

        ChatHistoryCompactor.Replace(history, [
            (AuthorRole.System, "SYSTEM"),
            (AuthorRole.Assistant, ChatHistoryCompactor.SummaryPrefix + "Резюме")
        ]);

        Assert.Equal(2, history.Messages.Count);
        Assert.Equal(ChatHistoryCompactor.SummaryPrefix + "Резюме", history.Messages[1].Content);
    }
}
