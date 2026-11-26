namespace Yuki.Mcp.MediaPlayer.Tools;

internal readonly record struct GuardedResult<T>(bool Success, T? Value, string? Error);

/// <summary>
/// Общие ограничения для медиа-операций: допустимые значения и таймаут.
/// </summary>
internal static class MediaPolicy {
    internal static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(10);

    internal static bool TryValidateVolume(int volume, out int percent, out string? error) {
        percent = 0;

        if (volume < 1 || volume > 100) {
            error = "Громкость должна быть от 1 до 100. Для выключения звука используй volume_turn_off";
            return false;
        }

        percent = volume;
        error = null;
        return true;
    }

    /// <summary>
    /// Выполняет операцию с жёстким лимитом времени. Возвращает ошибку,
    /// а не бесконечно висящий вызов WinRT/NAudio.
    /// </summary>
    internal static async Task<GuardedResult<T>> GuardAsync<T>(
        Func<Task<T>> operation,
        TimeSpan? timeout = null) {
        var limit = timeout ?? OperationTimeout;

        try {
            using var cancellation = new CancellationTokenSource(limit);
            var task = operation();
            var finished = await Task.WhenAny(task, Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token));

            if (finished != task)
                return new GuardedResult<T>(false, default, "Операция превысила лимит времени");

            return new GuardedResult<T>(true, await task, null);
        }
        catch (OperationCanceledException) {
            return new GuardedResult<T>(false, default, "Операция превысила лимит времени");
        }
        catch (Exception ex) {
            // Не отдаём моделью внутренний текст исключения.
            return new GuardedResult<T>(false, default, $"Ошибка операции: {ex.GetType().Name}");
        }
    }
}
