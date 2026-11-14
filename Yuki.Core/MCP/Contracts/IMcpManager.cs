using ModelContextProtocol.Client;

namespace Yuki.Core.MCP.Contracts;

public interface IMcpManager : IAsyncDisposable {
    /// <summary>
    /// Инициализирует и кеширует MCP клиенты.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Возвращает список инструментов со всех MCP серверов.
    /// </summary>
    /// <returns>Список инструментов, кешированных при инициализации менеджера.</returns>
    IEnumerable<McpClientTool> GetAllTools();

    /// <summary>
    /// Возвращает MCP клиента по названию <paramref name="name"/>.
    /// </summary>
    /// <param name="name"></param>
    McpClient GetClient(string name);
}
