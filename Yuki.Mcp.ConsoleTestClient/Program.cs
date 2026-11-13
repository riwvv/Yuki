using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Serialization;

var transport = new StdioClientTransport(new StdioClientTransportOptions {
#if DEBUG
    Command = "dotnet",
    Arguments = ["run", "--project", Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\Yuki.Mcp.Browser\Yuki.Mcp.Browser.csproj"))],
#else
    Command = "Yuki.Mcp.Browser.exe",
    Arguments = [],
#endif
    StandardErrorLines = line => Console.Error.WriteLine($"[server] {line}")
});

await using var client = await McpClient.CreateAsync(transport);

var tools = await client.ListToolsAsync();
Console.WriteLine(
    $"Кол-во инструментов: {tools.Count}\n" +
    $"Доступные инструменты:\n{string.Join("\n", tools.Select(x => x.Name))}");

var result = await client.CallToolAsync("open_url", new Dictionary<string, object?> { ["url"] = "www.youtube.com" });
Console.WriteLine($"Результат: {JsonSerializer.Deserialize<DoneResponseDto>(result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text!)?.Message}");

class DoneResponseDto {
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    [JsonPropertyName("fillUrl")]
    public string FillUrl { get; set; } = string.Empty;
}