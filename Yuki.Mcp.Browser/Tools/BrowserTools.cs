using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace Yuki.Mcp.Browser.Tools;

/// <summary>
/// MCP-инструменты для работы с браузером по умолчанию. Адрес проходит
/// через <see cref="UrlPolicy"/>: никаких file://, javascript://, localhost
/// и внутренних сетей.
/// </summary>
internal class BrowserTools {
    [McpServerTool]
    [Description("Открывает страницу в браузере по умолчанию. Разрешены только http:// и https://; локальные и внутренние адреса запрещены. Для сайта вне списка доверенных требуется confirm = true.")]
    public static string OpenUrl(
        [Description("Полный URL, включая https://")] string url,
        [Description("Подтверждение для сайта вне списка доверенных или со ссылкой, похожей на токен")] bool confirm = false) {
        if (!UrlPolicy.TryValidate(url, confirm, out var target, out var cause, out var error))
            return Error(cause, error!);

        return OpenInBrowser(target);
    }

    private static string OpenInBrowser(UrlTarget target) {
        try {
            // Явно explorer.exe + ArgumentList: адрес не попадает в cmd.exe
            // и не может быть разбит на несколько аргументов.
            var startInfo = new ProcessStartInfo("explorer.exe") {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add(target.Url);

            using var process = Process.Start(startInfo);
            if (process is null)
                return Error("browser_error", "Не удалось открыть браузер");

            return Ok("Страница открыта", new { host = target.Host });
        }
        catch (Exception ex) {
            return Error("browser_error", $"Не удалось открыть страницу: {ex.GetType().Name}");
        }
    }

    private static string Ok(string message, object? data = null)
        => JsonSerializer.Serialize(new { status = "done", message, data });

    private static string Error(string cause, string description)
        => JsonSerializer.Serialize(new { status = "error", cause, description });
}
