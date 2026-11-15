using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json.Serialization;

var transport = new StdioClientTransport(new StdioClientTransportOptions {
#if DEBUG
    Command = "dotnet",
    Arguments = ["run", "--project", Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\Yuki.Mcp.FileSystem\Yuki.Mcp.FileSystem.csproj"))],
#else
    Command = "Yuki.Mcp.FileSystem.exe",
    Arguments = [],
#endif
    StandardErrorLines = line => Console.Error.WriteLine($"[server] {line}")
});

await using var client = await McpClient.CreateAsync(transport);

var resources = await client.ListResourceTemplatesAsync();
Console.WriteLine(
    $"Кол-во ресурсов: {resources.Count}\n" +
    $"Доступные ресурсы:\n{string.Join("\n", resources.Select(x => $"{x.Name} - {x.UriTemplate}"))}");

var tools = await client.ListToolsAsync();
Console.WriteLine(
    $"Кол-во инструментов: {tools.Count}\n" +
    $"Доступные инструменты:\n{string.Join("\n", tools.Select(x => x.Name))}");


var targetUri = "fs://desktop/test_resource.txt";

try {
    var readResult = await client.ReadResourceAsync(targetUri);
    foreach (var content in readResult.Contents) {
        if (content is TextResourceContents textContent)
            Console.WriteLine($"Успех! Содержимое файла:\n{textContent.Text}");
        else
            Console.WriteLine($"Получен ресурс другого типа: {content.GetType().Name}");
    }
}
catch (Exception ex) {
    Console.WriteLine($"Ошибка: {ex.Message}");
}

//var result = await client.CallToolAsync("open_url", new Dictionary<string, object?> { ["url"] = "www.youtube.com" });
//Console.WriteLine($"Результат: {JsonSerializer.Deserialize<DoneResponseDto>(result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text!)?.Message}");

class DoneResponseDto {
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    [JsonPropertyName("fillUrl")]
    public string FillUrl { get; set; } = string.Empty;
}