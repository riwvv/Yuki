using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Yuki.Mcp.FileSystem.Tools;

namespace Yuki.Mcp.FileSystem.Resources;

[McpServerResourceType]
internal class FileSystemResources {
    [McpServerResource(UriTemplate = "fs://{folderName}/{fileName}", Name = "Read File Content", MimeType = "text/plain")]
    [Description("Считывает текстовое содержимое файла из специальной папки (downloads, documents, pictures, videos, music, desktop). Только .txt, .csv, .log, .md.")]
    public string ReadTextFile(string folderName, string fileName) {
        if (!SafePaths.TryResolveReadableFile(folderName, fileName, out var fullPath, out var error))
            throw new McpException(error ?? "Недопустимый путь к файлу");

        try {
            return File.ReadAllText(fullPath);
        }
        catch (Exception ex) {
            throw new McpException($"Не удалось прочитать файл: {ex.GetType().Name}");
        }
    }
}
