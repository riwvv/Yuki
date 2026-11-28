using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using Yuki.Core.Configurations;
using Yuki.Core.HostAgent.Contracts;

namespace Yuki.Core.HostAgent.Services;

/// <summary>
/// Рабочая память голосовой сессии: ожидающее подтверждение, активная задача
/// и последний ответ Юки. Ничего не пишется на диск и не живёт дольше TTL.
/// </summary>
public sealed class WorkingMemoryService(
    IOptions<HostSettings> options,
    ILogger<WorkingMemoryService> logger) : IWorkingMemoryService {
    private const int MaxTextLength = 240;

    private readonly Lock _lock = new();
    private PendingConfirmationInfo? _pending;
    private string? _activeTask;
    private string? _lastAssistantText;

    public bool HasPendingConfirmation {
        get {
            lock (_lock)
                return TryGetPending(out _);
        }
    }

    public string? ActiveTask {
        get {
            lock (_lock)
                return string.IsNullOrWhiteSpace(_activeTask) ? null : _activeTask;
        }
    }

    public PendingConfirmationInfo? PendingConfirmation {
        get {
            lock (_lock)
                return TryGetPending(out var pending) ? pending : null;
        }
    }

    public void SetPendingConfirmation(string task, string question) {
        if (string.IsNullOrWhiteSpace(task)) return;

        lock (_lock) {
            _pending = new PendingConfirmationInfo(
                Clip(task),
                Clip(question),
                DateTimeOffset.UtcNow);
            logger.LogInformation("Рабочая память: жду подтверждения — {Task}", _pending.Task);
        }
    }

    public void UpdatePendingQuestion(string question) {
        lock (_lock) {
            if (_pending is null) return;
            _pending = _pending with { Question = Clip(question), AskedAt = DateTimeOffset.UtcNow };
            logger.LogInformation("Рабочая память: обновлён вопрос Юки — {Question}", _pending.Question);
        }
    }

    public void ClearPendingConfirmation() {
        lock (_lock) {
            if (_pending is null) return;
            _pending = null;
            logger.LogInformation("Рабочая память: ожидание подтверждения закрыто");
        }
    }

    public void SetActiveTask(string task) {
        if (string.IsNullOrWhiteSpace(task)) return;
        lock (_lock)
            _activeTask = Clip(task);
    }

    public void ClearActiveTask() {
        lock (_lock)
            _activeTask = null;
    }

    public void SetLastAssistantText(string text) {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (_lock)
            _lastAssistantText = Clip(text);
    }

    public string BuildPromptBlock() {
        lock (_lock) {
            var lines = new List<string>();

            if (TryGetPending(out var pending)) {
                lines.Add($"Ожидающее подтверждение: {pending.Task}");
                if (!string.IsNullOrWhiteSpace(pending.Question))
                    lines.Add($"Юки уже спросила: «{pending.Question}»");
            }

            if (!string.IsNullOrWhiteSpace(_activeTask))
                lines.Add($"Активная задача Servant'а: {_activeTask}");

            // Последний ответ намеренно НЕ подставляется: иначе блок появляется
            // на каждой реплике и модель начинает тащить его за собой по истории.
            if (lines.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("[Рабочая память — служебные данные, не реплика пользователя]");
            foreach (var line in lines)
                sb.AppendLine(line);
            sb.Append("[Конец рабочей памяти]");
            return sb.ToString();
        }
    }

    public void Reset() {
        lock (_lock) {
            _pending = null;
            _activeTask = null;
            _lastAssistantText = null;
        }
    }

    private bool TryGetPending(out PendingConfirmationInfo pending) {
        pending = null!;
        if (_pending is null) return false;

        var ttl = TimeSpan.FromSeconds(Math.Max(1, options.Value.WorkingMemoryTtlSeconds));
        if (DateTimeOffset.UtcNow - _pending.AskedAt <= ttl) {
            pending = _pending;
            return true;
        }

        logger.LogInformation("Рабочая память: подтверждение устарело и забыто");
        _pending = null;
        return false;
    }

    private static string Clip(string text) {
        var trimmed = text.Trim();
        return trimmed.Length <= MaxTextLength ? trimmed : trimmed[..MaxTextLength] + "…";
    }
}
