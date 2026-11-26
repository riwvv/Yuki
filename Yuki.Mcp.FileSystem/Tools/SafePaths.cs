namespace Yuki.Mcp.FileSystem.Tools;

/// <summary>
/// Единая политика для специальных папок, имён файлов и расширений.
/// Всё, что касается путей, проходит через этот класс.
/// </summary>
internal static class SafePaths {
    internal static readonly string[] FolderNames =
        ["downloads", "documents", "pictures", "videos", "music", "desktop"];

    internal static readonly Dictionary<string, string[]> FileTypes = new(StringComparer.OrdinalIgnoreCase) {
        ["текстовик"] = [".txt"],
        ["текстовый документ"] = [".txt"],
        ["фото"] = [".png", ".jpg", ".jpeg"],
        ["фотка"] = [".png", ".jpg", ".jpeg"],
        ["видео"] = [".mp4", ".mkv"],
        ["видос"] = [".mp4", ".mkv"],
        ["презентация"] = [".pptx", ".ppt", ".pdf"],
        ["ворд"] = [".docx"],
        ["документ"] = [".docx"],
        ["таблица"] = [".xlsx", ".ods", ".csv"]
    };

    private static readonly HashSet<string> AllowedExtensions =
        [.. FileTypes.Values.SelectMany(extensions => extensions).Distinct(StringComparer.OrdinalIgnoreCase)];

    // Расширения, которые разрешено читать как текст через fs:// и ReadFileContent.
    private static readonly HashSet<string> ReadableExtensions = [".txt", ".csv", ".log", ".md"];

    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    internal static bool IsAllowedFolder(string? folder)
        => !string.IsNullOrWhiteSpace(folder) && FolderNames.Contains(folder.Trim().ToLowerInvariant());

    internal static string GetFolderPath(string folderName) => folderName.Trim().ToLowerInvariant() switch {
        "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        _ => throw new ArgumentException($"Неизвестная папка: {folderName}", nameof(folderName))
    };

    internal static bool TryGetFileName(string? fileName, out string validated, out string? error) {
        validated = string.Empty;

        if (string.IsNullOrWhiteSpace(fileName)) {
            error = "Имя файла не может быть пустым";
            return false;
        }

        var trimmed = fileName.Trim();
        if (trimmed is "." or ".." ||
            Path.IsPathRooted(trimmed) ||
            trimmed.Contains(Path.DirectorySeparatorChar) ||
            trimmed.Contains(Path.AltDirectorySeparatorChar) ||
            trimmed.Any(character => InvalidFileNameChars.Contains(character))) {
            error = "Недопустимое имя файла: разделители путей и служебные символы запрещены";
            return false;
        }

        validated = trimmed;
        error = null;
        return true;
    }

    internal static bool TryResolveFile(
        string folderName,
        string fileName,
        out string fullPath,
        out string? error) {
        fullPath = string.Empty;

        if (!IsAllowedFolder(folderName)) {
            error = $"Недопустимая папка. Разрешены: {string.Join(", ", FolderNames)}";
            return false;
        }

        var basePath = GetFolderPath(folderName);
        if (string.IsNullOrWhiteSpace(basePath)) {
            error = $"Папка {folderName} недоступна в этой системе";
            return false;
        }

        return TryResolveFileInBase(basePath, fileName, out fullPath, out error);
    }

    internal static bool TryResolveReadableFile(
        string folderName,
        string fileName,
        out string fullPath,
        out string? error) {
        if (!TryResolveFile(folderName, fileName, out fullPath, out error))
            return false;

        if (!IsReadableExtension(Path.GetExtension(fullPath))) {
            fullPath = string.Empty;
            error = "Для чтения разрешены только текстовые файлы: .txt, .csv, .log, .md";
            return false;
        }

        return true;
    }

    internal static bool IsReadableExtension(string extension)
        => ReadableExtensions.Contains(extension.ToLowerInvariant());

    internal static bool TryResolveFileInBase(
        string basePath,
        string fileName,
        out string fullPath,
        out string? error) {
        fullPath = string.Empty;

        if (!TryGetFileName(fileName, out _, out error))
            return false;

        var candidate = Path.GetFullPath(Path.Combine(basePath, fileName));
        if (!IsUnder(basePath, candidate)) {
            error = "Попытка выйти за пределы разрешённой папки заблокирована";
            return false;
        }

        fullPath = candidate;
        error = null;
        return true;
    }

    internal static bool IsUnder(string basePath, string candidatePath) {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(basePath));
        var candidate = Path.GetFullPath(candidatePath);
        var relative = Path.GetRelativePath(root, candidate);

        if (relative == "." || Path.IsPathRooted(relative))
            return false;

        return relative != ".." &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
               !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    internal static bool TryGetExtension(string? fileType, out string extension, out string? error) {
        extension = string.Empty;

        if (string.IsNullOrWhiteSpace(fileType)) {
            extension = ".txt";
            error = null;
            return true;
        }

        if (FileTypes.TryGetValue(fileType.Trim(), out var extensions) && extensions.Length > 0) {
            extension = extensions[0];
            error = null;
            return true;
        }

        error = $"Неподдерживаемый тип файла: {fileType}. Разрешены: {string.Join(", ", FileTypes.Keys)}";
        return false;
    }

    internal static bool TryGetExtensions(string? fileType, out string[] extensions, out string? error) {
        if (string.IsNullOrWhiteSpace(fileType)) {
            extensions = [.. AllowedExtensions];
            error = null;
            return true;
        }

        if (FileTypes.TryGetValue(fileType.Trim(), out var found)) {
            extensions = found;
            error = null;
            return true;
        }

        extensions = [];
        error = $"Неподдерживаемый тип файла: {fileType}. Разрешены: {string.Join(", ", FileTypes.Keys)}";
        return false;
    }

    internal static bool IsAllowedExtension(string extension)
        => AllowedExtensions.Contains(extension.ToLowerInvariant());

    internal static bool TryFindFile(
        string folderPath,
        string targetFileName,
        string? fileType,
        out string foundPath,
        out string? error) {
        foundPath = string.Empty;

        if (!TryGetFileName(targetFileName, out var validatedName, out error))
            return false;
        if (!TryGetExtensions(fileType, out var allowedExtensions, out error))
            return false;

        var stem = Path.GetFileNameWithoutExtension(validatedName);

        string[] matches;
        try {
            matches = Directory.EnumerateFiles(folderPath)
                .Where(path => IsAllowedExtension(Path.GetExtension(path)) &&
                               allowedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .Where(path => Path.GetFileNameWithoutExtension(path)
                    .StartsWith(stem, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            error = $"Не удалось просмотреть папку: {ex.GetType().Name}";
            return false;
        }

        if (matches.Length == 0) {
            error = $"Файл '{validatedName}' не найден среди разрешённых типов";
            return false;
        }

        foundPath = matches
            .OrderByDescending(path => Path.GetFileNameWithoutExtension(path)
                .Equals(stem, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(path => Similarity(Path.GetFileNameWithoutExtension(path), stem))
            .First();
        error = null;
        return true;
    }

    private static double Similarity(string source, string target) {
        var maxLength = Math.Max(source.Length, target.Length);
        if (maxLength == 0)
            return 100.0;

        var distance = Levenshtein(source, target);
        return (1.0 - (double)distance / maxLength) * 100.0;
    }

    private static int Levenshtein(string source, string target) {
        if (source.Length == 0) return target.Length;
        if (target.Length == 0) return source.Length;

        var distance = new int[source.Length + 1, target.Length + 1];
        for (var i = 0; i <= source.Length; i++) distance[i, 0] = i;
        for (var j = 0; j <= target.Length; j++) distance[0, j] = j;

        for (var i = 1; i <= source.Length; i++) {
            for (var j = 1; j <= target.Length; j++) {
                var cost = target[j - 1] == source[i - 1] ? 0 : 1;
                distance[i, j] = Math.Min(
                    Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                    distance[i - 1, j - 1] + cost);
            }
        }

        return distance[source.Length, target.Length];
    }
}
