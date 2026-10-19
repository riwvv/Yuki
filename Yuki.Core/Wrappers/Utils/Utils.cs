using System.Reflection;

namespace Yuki.Core.Wrappers.Utils;

public static class Utils {
    public static string ReadSystemPromptFromFile(string fileName) {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"Yuki.Core.Prompts.{fileName}";
        using var stream = assembly.GetManifestResourceStream(resourceName) ?? throw new FileNotFoundException($"Внедрённый промпт не найден: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static byte[] LoadWakeWord(string fileName) {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"Yuki.Core.STT.Resources.{fileName}";
        using var stream = assembly.GetManifestResourceStream(resourceName) ?? throw new FileNotFoundException($"Внедрённый wake word не найден: {resourceName}");
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }

    public static string GetLlmModelPath(string fileName) {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("Имя модели не может быть пустым", nameof(fileName));
        var folderName = "LlmModels";
        var modelPath = string.Empty;
#if DEBUG
        modelPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\", "Yuki.Core", folderName, fileName));
#else
        modelPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, folderName, fileName));
#endif
        if (File.Exists(modelPath))
            return modelPath;
        throw new FileNotFoundException($"Модель '{fileName}' не найдена. Убедитесь, что она лежит в папке '{folderName}' в корне проекта или рядом с исполняемым файлом.");
    }
}
