using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Yuki.Core.MCP.Contracts;

namespace Yuki.Core.MCP.Services;

public class McpHostedService(IMcpManager _mcpManager, ILogger<McpHostedService> _logger) : IHostedService {
    public async Task StartAsync(CancellationToken cancellationToken) {
        _logger.LogInformation("Запуск MCP-серверов.");
		try {
            await _mcpManager.InitializeAsync();
            _logger.LogInformation("MCP-серверы запущены.");
		}
		catch (Exception ex) {
            _logger.LogError(ex, "Ошибка запуска MCP-серверов. Запуск без инструментов.");
		}
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        _logger.LogInformation("Остановка MCP-серверов.");
        return Task.CompletedTask;
    }
}
