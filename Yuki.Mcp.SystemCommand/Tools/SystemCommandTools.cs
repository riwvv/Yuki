using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace Yuki.Mcp.SystemCommand.Tools;

/// <summary>
/// MCP-инструменты системных действий. Модель не может передать сюда
/// произвольную команду: доступны только операции из WhitelistedCommands.
/// </summary>
internal class SystemCommandTools {
    [McpServerTool]
    [Description("Показывает сетевые настройки (аналог ipconfig). Произвольные аргументы не принимаются.")]
    public static Task<string> GetIpConfig(
        [Description("Показать подробную информацию (/all)")] bool all = false)
        => RunWhitelistedAsync(WhitelistedCommands.IpConfig(all), "Сетевые настройки получены");

    [McpServerTool]
    [Description("Проверяет доступность хоста командой ping. Можно указать только DNS-имя или IP-адрес.")]
    public static async Task<string> PingHost(
        [Description("Хост, например google.com или 8.8.8.8")] string host,
        [Description("Количество пакетов, от 1 до 10")] int count = 3) {
        if (!WhitelistedCommands.TryPing(host, count, out var spec, out var error))
            return NotAllowed(error);

        return await RunWhitelistedAsync(spec!, $"Проверка хоста {host} завершена");
    }

    [McpServerTool]
    [Description("Показывает список запущенных процессов (аналог tasklist).")]
    public static Task<string> GetTaskList()
        => RunWhitelistedAsync(WhitelistedCommands.TaskList(), "Список процессов получен");

    [McpServerTool]
    [Description("Возвращает список процессов с идентификаторами. Можно отфильтровать по имени.")]
    public static string GetProcessList(
        [Description("Имя процесса, например chrome. Пусто — все процессы")] string? name = null) {
        if (!WhitelistedCommands.TryValidateProcessName(name, out var validatedName, out var error))
            return NotAllowed(error);

        try {
            var processes = Process.GetProcesses()
                .Select(process => {
                    using (process) {
                        try {
                            return new { process.Id, process.ProcessName };
                        }
                        catch {
                            return null;
                        }
                    }
                })
                .Where(item => item is not null)
                .Where(item => string.IsNullOrEmpty(validatedName) ||
                               item!.ProcessName.Equals(validatedName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item!.ProcessName)
                .ThenBy(item => item!.Id)
                .Take(200)
                .ToArray();

            return Ok($"Найдено процессов: {processes.Length}", new { processes });
        }
        catch (Exception ex) {
            return Error("process_list_failed", $"Не удалось получить список процессов: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Очищает корзину. Опасная операция: требует подтверждения confirm = true.")]
    public static async Task<string> ClearRecycleBin(
        [Description("Подтверждение. Ставь true только после явной просьбы пользователя")] bool confirm = false) {
        if (!confirm)
            return Error("confirmation_required", "Для очистки корзины требуется confirm = true");

        return await RunWhitelistedAsync(WhitelistedCommands.ClearRecycleBin(), "Корзина очищена");
    }

    [McpServerTool]
    [Description("Блокирует компьютер. Вызови, когда пользователь говорит: заблокируй экран, блокировка, lock screen.")]
    public static string LockScreen()
        => StartDetached(WhitelistedCommands.LockScreen(), "Экран заблокирован");

    [McpServerTool]
    [Description("Сворачивает все окна. Вызови, когда пользователь говорит: сверни окна, покажи рабочий стол, minimize all.")]
    public static Task<string> MinimizeAllWindows()
        => RunWhitelistedAsync(WhitelistedCommands.MinimizeAllWindows(), "Все окна свёрнуты");

    [McpServerTool]
    [Description("Открывает диспетчер задач. Вызови, когда пользователь говорит: открой диспетчер задач, task manager.")]
    public static string OpenTaskManager()
        => StartDetached(WhitelistedCommands.OpenTaskManager(), "Диспетчер задач открыт");

    [McpServerTool]
    [Description("Выключает компьютер через 15 секунд. Опасная операция: требует подтверждения confirm = true.")]
    public static Task<string> Shutdown(
        [Description("Подтверждение. Ставь true только после явной просьбы пользователя выключить ПК")] bool confirm = false)
        => ConfirmOrRun(confirm, WhitelistedCommands.Shutdown(restart: false), "Выключаю компьютер через 15 секунд");

    [McpServerTool]
    [Description("Перезагружает компьютер через 15 секунд. Опасная операция: требует подтверждения confirm = true.")]
    public static Task<string> Restart(
        [Description("Подтверждение. Ставь true только после явной просьбы пользователя перезагрузить ПК")] bool confirm = false)
        => ConfirmOrRun(confirm, WhitelistedCommands.Shutdown(restart: true), "Перезагружаю компьютер через 15 секунд");

    [McpServerTool]
    [Description("Отменяет запланированное выключение или перезагрузку. Вызови, когда пользователь говорит: отмени выключение, cancel shutdown.")]
    public static Task<string> CancelShutdown()
        => RunWhitelistedAsync(WhitelistedCommands.CancelShutdown(), "Запланированное выключение отменено");

    [McpServerTool]
    [Description("Возвращает текущее системное время и дату.")]
    public static string GetCurrentTime() {
        var now = DateTime.Now;
        var time = now.ToString("HH:mm");
        return Ok($"Сейчас {time}", new {
            fullDate = now.ToString("dd.MM.yyyy"),
            time
        });
    }

    private static Task<string> ConfirmOrRun(bool confirm, ProcessSpec spec, string message)
        => confirm
            ? RunWhitelistedAsync(spec, message)
            : Task.FromResult(Error("confirmation_required",
                "Для выключения/перезагрузки требуется confirm = true и явная просьба пользователя"));

    private static async Task<string> RunWhitelistedAsync(ProcessSpec spec, string successMessage) {
        var result = await ProcessRunner.RunAsync(spec);

        if (!result.Started)
            return Error("process_failed", result.StartFailure ?? "Не удалось запустить процесс");

        if (result.TimedOut)
            return Error("timeout", $"Команда «{spec.Display}» превысила лимит {spec.Timeout.TotalSeconds:0} сек");

        if (result.ExitCode != 0) {
            var details = string.IsNullOrWhiteSpace(result.Error)
                ? $"Код выхода {result.ExitCode}"
                : result.Error.Trim();
            return Error("command_error", details);
        }

        var output = string.IsNullOrWhiteSpace(result.Output)
            ? successMessage
            : result.Output.Trim();

        return Ok(successMessage, new {
            command = spec.Display,
            output
        });
    }

    private static string StartDetached(ProcessSpec spec, string message) {
        try {
            using var process = Process.Start(ProcessRunner.CreateStartInfo(spec));
            if (process is null)
                return Error("process_failed", "Не удалось запустить процесс");

            return Ok(message, new { command = spec.Display });
        }
        catch (Exception ex) {
            return Error("process_failed", $"Не удалось запустить процесс: {ex.Message}");
        }
    }

    private static string NotAllowed(string? detail)
        => Error("not_allowed", detail ?? "Операция не разрешена whitelist'ом");

    private static string Ok(string message, object? data = null)
        => JsonSerializer.Serialize(new { status = "done", message, data });

    private static string Error(string cause, string description)
        => JsonSerializer.Serialize(new { status = "error", cause, description });
}
