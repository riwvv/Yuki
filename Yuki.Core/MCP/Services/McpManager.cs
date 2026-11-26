using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Yuki.Core.MCP.Contracts;

namespace Yuki.Core.MCP.Services;

public sealed class McpManager(ILogger<IMcpManager> _logger) : IMcpManager {
    private readonly Dictionary<string, McpClient> _clients = [];
    private readonly List<McpClientTool> _cachedTools = [];

    public async Task InitializeAsync() {
        await ConnectServerAsync("browser", "Yuki.Mcp.Browser");
        await ConnectServerAsync("media-player", "Yuki.Mcp.MediaPlayer");
        await ConnectServerAsync("system-command", "Yuki.Mcp.SystemCommand");
        await ConnectServerAsync("file-system", "Yuki.Mcp.FileSystem");

        foreach (var (name, client) in _clients) {
            var tools = await client.ListToolsAsync();
            _cachedTools.AddRange(tools);
            _logger.LogInformation("MCP: сервер {Server} отдал {Count} {Noun}",
                name, tools.Count(), Plural(tools.Count(), "инструмент", "инструмента", "инструментов"));
        }

        _logger.LogInformation("MCP: всего доступно {Count} {Noun}",
            _cachedTools.Count, Plural(_cachedTools.Count, "инструмент", "инструмента", "инструментов"));
    }

    public IEnumerable<McpClientTool> GetAllTools() => _cachedTools;

    public McpClient GetClient(string name) => _clients[name];

    private async Task ConnectServerAsync(string clientName, string projectName) {
        _logger.LogInformation("MCP: запускаю сервер {Server}", clientName);

        var transport = new StdioClientTransport(new StdioClientTransportOptions {
#if DEBUG
            Command = "dotnet",
            Arguments = ["run", "--project", Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @$"..\..\..\..\{projectName}\{projectName}.csproj"))],
#else
            Command = Path.Combine(AppContext.BaseDirectory, $"{projectName}.exe"),
            Arguments = [],
#endif
            StandardErrorLines = line => LogServerLine(clientName, line)
        });

        try {
            var client = await McpClient.CreateAsync(transport);
            _clients[clientName] = client;
            _logger.LogInformation("MCP: сервер {Server} подключён", clientName);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "MCP: не удалось запустить сервер {Server}", clientName);
        }
    }

    private void LogServerLine(string clientName, string line) {
        if (string.IsNullOrWhiteSpace(line))
            return;

        var text = line.TrimEnd();
        if (text.Contains("IsError = True", StringComparison.OrdinalIgnoreCase)) {
            _logger.LogWarning("MCP [{Server}] {Line}", clientName, text);
            return;
        }

        if (!text.Contains("IsError = False", StringComparison.OrdinalIgnoreCase) &&
            (text.Contains("error", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("warn", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("exception", StringComparison.OrdinalIgnoreCase))) {
            _logger.LogWarning("MCP [{Server}] {Line}", clientName, text);
            return;
        }

        if (IsRoutineNoise(text)) {
            _logger.LogDebug("MCP [{Server}] {Line}", clientName, text);
            return;
        }

        _logger.LogInformation("MCP [{Server}] {Line}", clientName, text);
    }

    private static bool IsRoutineNoise(string line) {
        var text = line.TrimStart();
        return text.StartsWith("info:", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Microsoft.Hosting.Lifetime", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Application started.", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Hosting environment:", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Content root path:", StringComparison.OrdinalIgnoreCase)
               || text.Contains("transport reading messages", StringComparison.OrdinalIgnoreCase)
               || text.Contains("method 'server/discover'", StringComparison.OrdinalIgnoreCase)
               || text.Contains("method 'tools/list'", StringComparison.OrdinalIgnoreCase);
    }

    private static string Plural(int count, string one, string few, string many) {
        var mod10 = count % 10;
        var mod100 = count % 100;
        if (mod10 == 1 && mod100 != 11) return one;
        if (mod10 is >= 2 and <= 4 && (mod100 < 12 || mod100 > 14)) return few;
        return many;
    }

    public async ValueTask DisposeAsync() {
        foreach (var client in _clients.Values) {
            await client.DisposeAsync();
        }
        _clients.Clear();
        _cachedTools.Clear();
    }
}
