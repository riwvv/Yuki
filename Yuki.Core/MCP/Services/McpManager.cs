using ModelContextProtocol.Client;
using Yuki.Core.MCP.Contracts;

namespace Yuki.Core.MCP.Services;

public sealed class McpManager : IMcpManager {
    private readonly Dictionary<string, McpClient> _clients = [];
    private readonly List<McpClientTool> _cachedTools = [];

    public async Task InitializeAsync() {
        // инициализация одного сервера
        var browserTransport = new StdioClientTransport(new StdioClientTransportOptions {
#if DEBUG
            Command = "dotnet",
            Arguments = ["run", "--project", Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\Yuki.Mcp.Browser\Yuki.Mcp.Browser.csproj"))],
#else
            Command = Path.Combine(AppContext.BaseDirectory, "Yuki.Mcp.Browser.exe"),
            Arguments = [],
#endif
            StandardErrorLines = line => Console.Error.WriteLine($"[browser-server] {line}")
        });

        var browserClient = await McpClient.CreateAsync(browserTransport);
        _clients.Add("browser", browserClient);
        // так же все остальные


        // кешируем
        foreach (var client in _clients.Values) {
            var tools = await client.ListToolsAsync();
            _cachedTools.AddRange(tools);
        }
    }

    public IEnumerable<McpClientTool> GetAllTools() => _cachedTools;

    public McpClient GetClient(string name) => _clients[name];

    public async ValueTask DisposeAsync() {
        foreach (var client in _clients.Values) {
            await client.DisposeAsync();
        }
        _clients.Clear();
        _cachedTools.Clear();
    }
}
