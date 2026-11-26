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
            _logger.LogInformation("MCP: сервер {Server} отдал {Count} инструментов", name, tools.Count());
        }

        _logger.LogInformation("MCP: всего доступно {Count} инструментов", _cachedTools.Count);
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
            StandardErrorLines = line => _logger.LogInformation("MCP [{Server}] {Line}", clientName, line)
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

    public async ValueTask DisposeAsync() {
        foreach (var client in _clients.Values) {
            await client.DisposeAsync();
        }
        _clients.Clear();
        _cachedTools.Clear();
    }
}
