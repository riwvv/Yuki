using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Yuki.Mcp.FileSystem.Resources;

[McpServerResourceType]
internal class FileSystemResources {
    private readonly List<string> _folderNames = ["downloads", "documents", "pictures", "videos", "music", "desktop"];

    [McpServerResource(UriTemplate = "fs://{folderName}/{fileName}", Name = "Read File Content", MimeType = "text/plain")]
    [Description("Считывает и возвращает текстовое содержимое файла из специальной папки (downloads, documents, pictures, videos, music, desktop).")]
    public string ReadTextFile(string folderName, string fileName) {
        if (!SpecialFolderValidation(folderName))
            throw new McpException($"Invalid folder name: {folderName}");

        var basePath = GetFullFolderPath(folderName);
        var fullPath = Path.GetFullPath(Path.Combine(basePath, fileName));

        if (!fullPath.StartsWith(basePath, StringComparison.Ordinal))
            throw new McpException("Path traversal attempt blocked.");

        if (!File.Exists(fullPath))
            throw new McpException($"File not found: {fileName}");

        return File.ReadAllText(fullPath);
    }

    private bool SpecialFolderValidation(string folder) => !string.IsNullOrWhiteSpace(folder) && _folderNames.Contains(folder.ToLower());

    private static string GetFullFolderPath(string folderName) => folderName.ToLower() switch {
        "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        _ => throw new ArgumentException($"Неизвестный тип папки: {folderName}")
    };
}
