using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Yuki.Core.MCP.Contracts;

namespace Yuki.Core.MCP.Services;

public class McpHostedService(IMcpManager _mcpManager, ILogger<McpHostedService> _logger) : IHostedService {
    public async Task StartAsync(CancellationToken cancellationToken) {
        _logger.LogInformation("MCP: запуск серверов");
		try {
            await _mcpManager.InitializeAsync();
            _logger.LogInformation("MCP: серверы запущены");
		}
		catch (Exception ex) {
            _logger.LogError(ex, "MCP: ошибка запуска — приложение работает без инструментов");
		}
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        _logger.LogInformation("MCP: остановка серверов");
        return Task.CompletedTask;
    }
}
