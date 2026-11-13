using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace Yuki.Mcp.Browser.Tools;

/// <summary>
/// MCP инструменты для работы и взаимодействия с браузером (по умолчанию).
/// </summary>
internal class BrowserTools {
    [McpServerTool]
    [Description("Открывает URL в браузере по умолчанию")]
    public static string OpenUrl([Description("Полный URL адрес, включая https://")] string url) {
        try {
            url = url.Trim();

            if (string.IsNullOrWhiteSpace(url)) {
                return JsonSerializer.Serialize(new {
                    status = "error",
                    cause = "empty_url",
                    description = "URL не может быть пустым"
                });
            }

            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                url = "https://" + url;

            if (!Uri.IsWellFormedUriString(url, UriKind.Absolute)) {
                return JsonSerializer.Serialize(new {
                    status = "error",
                    cause = "invalid_url",
                    description = $"URL '{url}' имеет неверный формат"
                });
            }

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

            return JsonSerializer.Serialize(new {
                status = "done",
                message = $"Страница открыта",
                fillUrl = url
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "error",
                cause = "browser_error",
                description = $"Ошибка открытия: {ex.Message}",
                fillUrl = url
            });
        }
    }
}
