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
}
