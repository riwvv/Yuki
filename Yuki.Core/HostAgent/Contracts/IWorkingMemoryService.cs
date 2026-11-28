namespace Yuki.Core.HostAgent.Contracts;

/// <summary>
/// Короткоживущее состояние незавершённого действия. Живёт только в памяти процесса.
/// </summary>
public sealed record PendingConfirmationInfo(string Task, string Question, DateTimeOffset AskedAt);

public interface IWorkingMemoryService {
    bool HasPendingConfirmation { get; }
    string? ActiveTask { get; }
    PendingConfirmationInfo? PendingConfirmation { get; }

    void SetPendingConfirmation(string task, string question);
    void UpdatePendingQuestion(string question);
    void ClearPendingConfirmation();

    void SetActiveTask(string task);
    void ClearActiveTask();

    void SetLastAssistantText(string text);

    /// <summary>
    /// Компактный блок для подстановки в реплику пользователя. Пустая строка, если нечего добавить.
    /// </summary>
    string BuildPromptBlock();

    void Reset();
}
