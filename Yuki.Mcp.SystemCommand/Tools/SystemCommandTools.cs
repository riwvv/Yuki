using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace Yuki.Mcp.SystemCommand.Tools;

internal class SystemCommandTools {
    [McpServerTool]
    [Description("Выполняет CMD команду. Только для команд которые не закрыты другими функциями: ipconfig, ping, tasklist")]
    public static async Task<string> ExecuteCMD([Description("CMD команда")] string command) {
        if (string.IsNullOrWhiteSpace(command)) {
            return JsonSerializer.Serialize(new {
                status = "error",
                cause = "empty_command",
                description = "Команда не может быть пустой"
            });
        }

        try {
            var psi = new ProcessStartInfo {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            if (process == null) {
                return JsonSerializer.Serialize(new {
                    status = "error",
                    cause = "process_failed",
                    description = "Не удалось выполнить команду"
                });
            }

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (!string.IsNullOrEmpty(error)) {
                return JsonSerializer.Serialize(new {
                    status = "error",
                    cause = "command_error",
                    description = error.Trim()
                });
            }

            var result = string.IsNullOrEmpty(output) ? "Команда выполнена" : output.Trim();

            return JsonSerializer.Serialize(new {
                status = "done",
                message = result,
                output = result,
                originalCommand = command,
                shell = "cmd"
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "error",
                cause = "exception",
                description = $"Ошибка выполнения: {ex.Message}"
            });
        }
    }

    [McpServerTool]
    [Description("Выполняет PowerShell команду. Используй для: Clear-RecycleBin, Get-Date, Get-Process")]
    public static async Task<string> ExecutePowerShell([Description("PowerShell команда")] string command) {
        if (string.IsNullOrWhiteSpace(command)) {
            return JsonSerializer.Serialize(new {
                status = "error",
                cause = "empty_command",
                description = "Команда не может быть пустой"
            });
        }

        try {
            var psi = new ProcessStartInfo {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"{command}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            if (process == null) {
                return JsonSerializer.Serialize(new {
                    status = "error",
                    cause = "process_failed",
                    description = "Не удалось выполнить команду"
                });
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            string output = await outputTask;
            string error = await errorTask;

            if (!string.IsNullOrEmpty(error)) {
                return JsonSerializer.Serialize(new {
                    status = "error",
                    cause = "command_error",
                    description = error.Trim()
                });
            }

            var result = string.IsNullOrEmpty(output) ? "Команда выполнена" : output.Trim();

            return JsonSerializer.Serialize(new {
                status = "done",
                message = result,
                output = result,
                originalCommand = command,
                shell = "powershell"
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "error",
                cause = "exception",
                description = $"Ошибка выполнения: {ex.Message}"
            });
        }
    }

    [McpServerTool]
    [Description("Блокирует компьютер. Вызови эту функцию когда пользователь говорит: заблокируй экран, блокировка, lock screen")]
    public static string LockScreen() {
        try {
            Process.Start("rundll32.exe", "user32.dll,LockWorkStation");
            return JsonSerializer.Serialize(new {
                status = "done",
                message = "Экран заблокирован",
                action = "lock_screen"
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "error",
                cause = "lock_failed",
                description = $"Ошибка блокировки: {ex.Message}"
            });
        }
    }

    [McpServerTool]
    [Description("Сворачивает все окна. Вызови когда пользователь говорит: сверни окна, покажи рабочий стол, minimize all")]
    public static string MinimizeAllWindows() {
        try {
            Process.Start(new ProcessStartInfo {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"(New-Object -ComObject Shell.Application).MinimizeAll()\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            return JsonSerializer.Serialize(new {
                status = "done",
                message = "Все окна свёрнуты",
                action = "minimize_all"
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "error",
                cause = "minimize_failed",
                description = $"Ошибка: {ex.Message}"
            });
        }
    }

    [McpServerTool]
    [Description("Открывает диспетчер задач. Вызови когда пользователь говорит: открой диспетчер задач, task manager")]
    public static string OpenTaskManager() {
        try {
            Process.Start("taskmgr.exe");
            return JsonSerializer.Serialize(new {
                status = "DONE",
                message = "Диспетчер задач открыт",
                action = "open_task_manager"
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "error",
                cause = "open_failed",
                description = $"Ошибка: {ex.Message}"
            });
        }
    }

    [McpServerTool]
    [Description("Выключает компьютер. Вызови когда пользователь говорит: выключи компьютер, shutdown")]
    public static string Shutdown() {
        try {
            Process.Start("shutdown", "/s /t 15 /c \"Юки выключает компьютер\"");
            return JsonSerializer.Serialize(new {
                status = "done",
                message = "Выключаю компьютер через 15 секунд",
                action = "shutdown",
                delay = 15
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "error",
                cause = "shutdown_failed",
                description = $"Ошибка: {ex.Message}"
            });
        }
    }

    [McpServerTool]
    [Description("Перезагружает компьютер. Вызови когда пользователь говорит: перезагрузи, restart, reboot")]
    public static string Restart() {
        try {
            Process.Start("shutdown", "/r /t 15 /c \"Юки перезагружает компьютер\"");
            return JsonSerializer.Serialize(new {
                status = "done",
                message = "Перезагружаю компьютер через 15 секунд",
                action = "restart",
                delay = 15
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "error",
                cause = "restart_failed",
                description = $"Ошибка: {ex.Message}"
            });
        }
    }

    [McpServerTool]
    [Description("Отменяет выключение. Вызови когда пользователь говорит: отмени выключение, cancel shutdown")]
    public static string CancelShutdown() {
        try {
            Process.Start("shutdown", "/a");
            return JsonSerializer.Serialize(new {
                status = "done",
                message = "Выключение отменено",
                action = "cancel_shutdown"
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "error",
                cause = "cancel_failed",
                description = $"Ошибка: {ex.Message}"
            });
        }
    }

    [McpServerTool]
    [Description("Возвращает текущее системное время и дату.")]
    public string GetCurrentTime() {
        var now = DateTime.Now;
        return JsonSerializer.Serialize(new {
            status = "DONE",
            message = now.ToString("HH:mm"),
            data = new {
                fullDate = now.ToString("dd.MM.yyyy"),
                time = now.ToString("HH:mm")
            }
        });
    }
}
