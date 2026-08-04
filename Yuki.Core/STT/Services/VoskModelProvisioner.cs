using System.IO.Compression;

namespace Yuki.Core.STT.Services;

public static class VoskModelProvisioner {
    private const string ModelName = "vosk-model-ru-0.42";
    private static readonly string ModelsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Yuki", "Models", "vosk");

    public static async Task<string> EnsureModelAsync(CancellationToken ct = default) {
        var modelDir = Path.Combine(ModelsRoot, ModelName);
        if (Directory.Exists(modelDir)) return modelDir;

        Directory.CreateDirectory(ModelsRoot);

        var zipUrl = $"https://alphacephei.com/vosk/models/{ModelName}.zip";
        var tempZip = Path.Combine(ModelsRoot, $"{ModelName}.zip.tmp");
        var tempExtractDir = Path.Combine(ModelsRoot, $"{ModelName}.extracting");

        using (var http = new HttpClient()) {
            using var response = await http.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using var fileStrean = File.Create(tempZip);
            await response.Content.CopyToAsync(fileStrean, ct);
        }

        if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true);
        ZipFile.ExtractToDirectory(tempZip, tempExtractDir);
        File.Delete(tempZip);

        var extractedInner = Path.Combine(tempExtractDir, ModelName);
        Directory.Move(extractedInner, modelDir);
        Directory.Delete(tempExtractDir, true);

        return modelDir;
    }
}
