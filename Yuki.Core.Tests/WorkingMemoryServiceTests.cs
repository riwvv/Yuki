using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Yuki.Core.Configurations;
using Yuki.Core.HostAgent.Services;

namespace Yuki.Core.Tests;

public class WorkingMemoryServiceTests {
    private static WorkingMemoryService Create(int ttlSeconds = 120) => new(
        Options.Create(new HostSettings {
            ModelName = "test",
            WorkingMemoryTtlSeconds = ttlSeconds
        }),
        NullLogger<WorkingMemoryService>.Instance);

    [Fact]
    public void BuildPromptBlock_IsEmpty_WhenNothingHappened() {
        var memory = Create();

        Assert.Equal(string.Empty, memory.BuildPromptBlock());
        Assert.False(memory.HasPendingConfirmation);
    }

    [Fact]
    public void BuildPromptBlock_ContainsPendingConfirmation_ButNotLastAnswer() {
        var memory = Create();
        memory.SetPendingConfirmation("Установить громкость 50%", "Эй, правда на пятьдесят ставим?");
        memory.SetLastAssistantText("Эй, правда на пятьдесят ставим?");

        var block = memory.BuildPromptBlock();

        Assert.Contains("[Рабочая память — служебные данные, не реплика пользователя]", block);
        Assert.Contains("Ожидающее подтверждение: Установить громкость 50%", block);
        Assert.Contains("Юки уже спросила: «Эй, правда на пятьдесят ставим?»", block);
        Assert.DoesNotContain("Последний ответ Юки", block);
        Assert.Contains("[Конец рабочей памяти]", block);
    }

    [Fact]
    public void BuildPromptBlock_ContainsActiveTask() {
        var memory = Create();
        memory.SetActiveTask("Открыть YouTube");

        var block = memory.BuildPromptBlock();

        Assert.Contains("Активная задача Servant'а: Открыть YouTube", block);
        Assert.DoesNotContain("Ожидающее подтверждение", block);
    }

    [Fact]
    public void UpdatePendingQuestion_ChangesQuestion_ButKeepsTask() {
        var memory = Create();
        memory.SetPendingConfirmation("Выключить ПК", "Правда выключаем?");

        memory.UpdatePendingQuestion("Точно выключаем компьютер?");

        var pending = memory.PendingConfirmation;
        Assert.NotNull(pending);
        Assert.Equal("Выключить ПК", pending.Task);
        Assert.Equal("Точно выключаем компьютер?", pending.Question);
    }

    [Fact]
    public async Task PendingConfirmation_ExpiresAfterTtl() {
        var memory = Create(ttlSeconds: 1);
        memory.SetPendingConfirmation("Выключить ПК", "Правда выключаем?");

        Assert.True(memory.HasPendingConfirmation);

        await Task.Delay(1200);

        Assert.False(memory.HasPendingConfirmation);
        Assert.Equal(string.Empty, memory.BuildPromptBlock());
    }

    [Fact]
    public void ClearAndReset_RemoveAllState() {
        var memory = Create();
        memory.SetPendingConfirmation("Выключить ПК", "Правда выключаем?");
        memory.SetActiveTask("Выключить ПК");
        memory.SetLastAssistantText("Правда выключаем?");

        memory.ClearPendingConfirmation();
        Assert.False(memory.HasPendingConfirmation);

        memory.Reset();
        Assert.Null(memory.ActiveTask);
        Assert.Equal(string.Empty, memory.BuildPromptBlock());
    }

    [Fact]
    public void SetLastAssistantText_DoesNotEnterPromptBlock() {
        var memory = Create();

        memory.SetLastAssistantText(new string('а', 600));

        Assert.Equal(string.Empty, memory.BuildPromptBlock());
    }
}
