using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Yuki.Mcp.FileSystem.Resources;

namespace Yuki.Mcp.FileSystem.Tools;

/// <summary>
/// MCP-инструменты для Whitelist-папок. Модель не может передать произвольный путь
/// или расширение: всё проходит через <see cref="SafePaths"/>.
/// </summary>
internal class FileSystemTools(FileSystemResources _resources) {
    [McpServerTool]
    [Description("Показывает файлы и папки внутри одной из разрешённых папок: downloads, documents, pictures, videos, music, desktop. Произвольные пути не принимаются.")]
    public string GetInfoFolder(
        [Description("Название одной из папок: downloads, documents, pictures, videos, music, desktop")] string folderName) {
        if (!SafePaths.IsAllowedFolder(folderName))
            return InvalidFolder(folderName);

        try {
            var folderPath = SafePaths.GetFolderPath(folderName);
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return Error("folder_not_found", $"Папка {folderName} недоступна");

            var entries = Directory.GetFileSystemEntries(folderPath);
            var files = Directory.GetFiles(folderPath);
            var directories = Directory.GetDirectories(folderPath);
            var size = ConversionToTheOptimalUnit(GetDirectorySize(new DirectoryInfo(folderPath)));

            return Ok($"В папке {folderName} {entries.Length} объектов, размер {size}", new {
                folder = folderName,
                totalObject = entries.Length,
                amountFiles = files.Length,
                amountDirectories = directories.Length,
                files = files.Select(Path.GetFileName).ToArray(),
                directories = directories.Select(Path.GetFileName).ToArray(),
                totalSize = size
            });
        }
        catch (Exception ex) {
            return Error("folder_info_failed", $"Не удалось получить информацию о папке: {ex.GetType().Name}");
        }
    }

    [McpServerTool]
    [Description("Открывает файл из разрешённой папки в программе по умолчанию. Разрешены только известные типы файлов (текст, фото, видео, документы); исполняемые файлы и скрипты открыть нельзя.")]
    public string OpenFileInFolder(
        [Description("Название одной из папок: downloads, documents, pictures, videos, music, desktop")] string folderName,
        [Description("Название файла или его часть")] string fileName,
        [Description("Тип файла: текстовик, фото, видео, презентация, ворд, таблица. Если не указан — ищет среди всех разрешённых типов")] string? fileType = null) {
        if (!SafePaths.IsAllowedFolder(folderName))
            return InvalidFolder(folderName);
        if (!SafePaths.TryGetFileName(fileName, out _, out var nameError))
            return Error("invalid_file_name", nameError!);
        if (!SafePaths.TryGetExtensions(fileType, out _, out var typeError))
            return Error("unsupported_file_type", typeError!);

        try {
            var folderPath = SafePaths.GetFolderPath(folderName);
            if (!SafePaths.TryFindFile(folderPath, fileName, fileType, out var foundPath, out var findError))
                return Error("file_not_found", findError!);

            return OpenInExplorer(foundPath);
        }
        catch (Exception ex) {
            return Error("open_failed", $"Не удалось открыть файл: {ex.GetType().Name}");
        }
    }

    [McpServerTool]
    [Description("Перемещает файл между разрешёнными папками. Разрешены только известные типы файлов; произвольные пути не принимаются.")]
    public string MoveFileFromFolderToFolder(
        [Description("Начальная папка: downloads, documents, pictures, videos, music, desktop")] string startFolder,
        [Description("Конечная папка: downloads, documents, pictures, videos, music, desktop")] string finishFolder,
        [Description("Название файла или его часть")] string fileName,
        [Description("Тип файла: текстовик, фото, видео, презентация, ворд, таблица")] string? fileType = null) {
        if (!SafePaths.IsAllowedFolder(startFolder))
            return InvalidFolder(startFolder);
        if (!SafePaths.IsAllowedFolder(finishFolder))
            return InvalidFolder(finishFolder);
        if (!SafePaths.TryGetFileName(fileName, out _, out var nameError))
            return Error("invalid_file_name", nameError!);
        if (!SafePaths.TryGetExtensions(fileType, out _, out var typeError))
            return Error("unsupported_file_type", typeError!);

        try {
            var startPath = SafePaths.GetFolderPath(startFolder);
            if (!SafePaths.TryFindFile(startPath, fileName, fileType, out var sourcePath, out var findError))
                return Error("file_not_found", findError!);

            var fileNameOnly = Path.GetFileName(sourcePath);
            var finishPath = SafePaths.GetFolderPath(finishFolder);
            if (!SafePaths.TryResolveFileInBase(finishPath, fileNameOnly, out var destinationPath, out var pathError))
                return Error("invalid_file_name", pathError!);

            if (File.Exists(destinationPath))
                return Error("already_exists", "Файл с таким названием уже существует в папке назначения");

            File.SetAttributes(sourcePath, FileAttributes.Normal);
            File.Move(sourcePath, destinationPath);

            return Ok($"Файл {fileNameOnly} перемещён из {startFolder} в {finishFolder}", new {
                file = fileNameOnly,
                fromFolder = startFolder,
                toFolder = finishFolder
            });
        }
        catch (Exception ex) {
            return Error("move_failed", $"Не удалось переместить файл: {ex.GetType().Name}");
        }
    }

    [McpServerTool]
    [Description("Создаёт пустой файл в разрешённой папке. Тип файла должен быть из whitelist (текстовик, фото, видео, презентация, ворд, таблица); по умолчанию .txt.")]
    public string CreateNewFileInSpecialFolder(
        [Description("Папка: downloads, documents, pictures, videos, music, desktop")] string folderName,
        [Description("Название файла без расширения")] string fileName,
        [Description("Тип файла: текстовик, фото, видео, презентация, ворд, таблица")] string? fileType = null) {
        if (!SafePaths.IsAllowedFolder(folderName))
            return InvalidFolder(folderName);
        if (!SafePaths.TryGetFileName(fileName, out var validatedName, out var nameError))
            return Error("invalid_file_name", nameError!);
        if (!SafePaths.TryGetExtension(fileType, out var extension, out var typeError))
            return Error("unsupported_file_type", typeError!);

        try {
            var folderPath = SafePaths.GetFolderPath(folderName);
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return Error("folder_not_found", $"Папка {folderName} недоступна");

            var fullFileName = Path.GetFileNameWithoutExtension(validatedName) + extension;
            if (!SafePaths.TryResolveFileInBase(folderPath, fullFileName, out var fullPath, out var pathError))
                return Error("invalid_file_name", pathError!);

            if (File.Exists(fullPath))
                return Error("already_exists", $"Файл '{fullFileName}' уже существует в папке {folderName}");

            using (File.Create(fullPath)) { }

            return Ok($"Файл '{fullFileName}' создан в папке {folderName}", new {
                name = Path.GetFileNameWithoutExtension(validatedName),
                folder = folderName,
                fullFileName,
                fileType = extension
            });
        }
        catch (UnauthorizedAccessException) {
            return Error("access_denied", $"Нет прав на создание файла в папке {folderName}");
        }
        catch (IOException) {
            return Error("io_error", "Ошибка ввода-вывода при создании файла");
        }
        catch (Exception ex) {
            return Error("create_failed", $"Не удалось создать файл: {ex.GetType().Name}");
        }
    }

    [McpServerTool]
    [Description("Переименовывает существующий файл внутри разрешённой папки. Расширение сохраняется; произвольные пути не принимаются.")]
    public string RenameExistsFile(
        [Description("Папка: downloads, documents, pictures, videos, music, desktop")] string folderName,
        [Description("Старое название файла или его часть")] string oldFileName,
        [Description("Новое название файла без расширения")] string newFileName,
        [Description("Тип файла: текстовик, фото, видео, презентация, ворд, таблица")] string? fileType = null) {
        if (!SafePaths.IsAllowedFolder(folderName))
            return InvalidFolder(folderName);
        if (!SafePaths.TryGetFileName(oldFileName, out _, out var oldNameError))
            return Error("invalid_file_name", oldNameError!);
        if (!SafePaths.TryGetFileName(newFileName, out var validatedNewName, out var newNameError))
            return Error("invalid_file_name", newNameError!);
        if (!SafePaths.TryGetExtensions(fileType, out _, out var typeError))
            return Error("unsupported_file_type", typeError!);

        try {
            var folderPath = SafePaths.GetFolderPath(folderName);
            if (!SafePaths.TryFindFile(folderPath, oldFileName, fileType, out var oldFullPath, out var findError))
                return Error("file_not_found", findError!);

            var extension = Path.GetExtension(oldFullPath);
            var newFullFileName = Path.GetFileNameWithoutExtension(validatedNewName) + extension;
            if (!SafePaths.TryResolveFileInBase(folderPath, newFullFileName, out var newFullPath, out var pathError))
                return Error("invalid_file_name", pathError!);

            if (File.Exists(newFullPath))
                return Error("already_exists", $"Файл '{newFullFileName}' уже существует в папке {folderName}");

            File.Move(oldFullPath, newFullPath);

            return Ok($"Файл переименован на {newFullFileName}", new {
                oldName = Path.GetFileName(oldFullPath),
                newName = newFullFileName,
                folder = folderName
            });
        }
        catch (Exception ex) {
            return Error("rename_failed", $"Не удалось переименовать файл: {ex.GetType().Name}");
        }
    }

    [McpServerTool]
    [Description("Считывает текстовое содержимое файла из разрешённой папки. Разрешены только текстовые файлы (.txt, .csv, .log, .md).")]
    public string ReadFileContent(
        [Description("Папка: downloads, documents, pictures, videos, music, desktop")] string folderName,
        [Description("Название файла")] string fileName) {
        try {
            var content = _resources.ReadTextFile(folderName, fileName);
            return Ok($"Файл {fileName} прочитан", new {
                file = fileName,
                folder = folderName,
                content
            });
        }
        catch (McpException ex) {
            return Error("read_error", ex.Message);
        }
        catch (Exception) {
            return Error("read_error", "Не удалось прочитать файл");
        }
    }

    private static string OpenInExplorer(string path) {
        try {
            // Явно explorer.exe + ArgumentList: путь не склеивается в строку
            // и не может быть интерпретирован как дополнительные аргументы.
            var startInfo = new ProcessStartInfo("explorer.exe") {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add(path);

            using var process = Process.Start(startInfo);
            if (process is null)
                return Error("process_failed", "Не удалось открыть файл");

            var name = Path.GetFileName(path);
            return Ok($"Файл {name} открыт", new { file = name });
        }
        catch (Exception ex) {
            return Error("process_failed", $"Не удалось открыть файл: {ex.GetType().Name}");
        }
    }

    private static string InvalidFolder(string? folderName)
        => Error("invalid_folder", $"Недопустимая папка. Разрешены: {string.Join(", ", SafePaths.FolderNames)}");

    private static string Ok(string message, object? data = null)
        => JsonSerializer.Serialize(new { status = "done", message, data });

    private static string Error(string cause, string description)
        => JsonSerializer.Serialize(new { status = "error", cause, description });

    private static string ConversionToTheOptimalUnit(long bytes) => bytes switch {
        < 1000 => $"{bytes} байт",
        >= 1000 and < 1000000 => $"{bytes / 1000} килобайт",
        >= 1000000 and < 1000000000 => $"{bytes / 1000000} мегабайт",
        >= 1000000000 and < 1000000000000 => $"{bytes / 1000000000} гигабайт",
        >= 1000000000000 and < 1000000000000000 => $"{bytes / 1000000000000} терабайт",
        >= 1000000000000000 and < 1000000000000000000 => $"{bytes / 1000000000000000} петабайт",
        _ => $"{bytes} байт"
    };

    private static long GetDirectorySize(DirectoryInfo directory, int depth = 0) {
        if (depth > 16)
            return 0;

        long size = 0;
        try {
            // Не ходим по junction/symlink — иначе можно выйти за пределы папки.
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                return 0;

            size = directory.GetFiles().Sum(file => file.Length);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) {
            // Часть файлов может быть недоступна — это нормально.
        }

        foreach (var subDirectory in directory.GetDirectories()) {
            try {
                size += GetDirectorySize(subDirectory, depth + 1);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) {
                // То же самое для вложенных папок.
            }
        }

        return size;
    }
}
