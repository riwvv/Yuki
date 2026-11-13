using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var transport = new StdioClientTransport(new StdioClientTransportOptions {
#if DEBUG
    Command = "dotnet",
    Arguments = ["run", "--project", Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\Yuki.Mcp.TestServer\Yuki.Mcp.TestServer.csproj"))],
#else
    Command = "Yuki.Mcp.TestServer.exe",
    Arguments = [],
#endif
    StandardErrorLines = line => Console.Error.WriteLine($"[server] {line}")
});

await using var client = await McpClient.CreateAsync(transport);

var tools = await client.ListToolsAsync();
Console.WriteLine($"Доступно инструментов: {tools.Count}");

foreach (var tool in tools) {
    Console.WriteLine(tool.Name);
}

var result1 = await client.CallToolAsync("get_random_number", new Dictionary<string, object?> { ["min"] = 1, ["max"] = 100 });
var result2 = await client.CallToolAsync("get_date_time");
Console.WriteLine($"Результат 1: {result1.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text}");
Console.WriteLine($"Результат 2: {result2.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text}");