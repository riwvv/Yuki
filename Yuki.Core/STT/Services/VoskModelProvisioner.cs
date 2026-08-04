using Microsoft.Extensions.Options;
using System.IO.Compression;
using Yuki.Core.Configurations;

namespace Yuki.Core.STT.Services;

public class VoskModelProvisioner {
    private readonly string _modelName;
    private static readonly string ModelsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Yuki", "Models", "vosk");
    private readonly HttpClient _httpClient;

    public VoskModelProvisioner(IHttpClientFactory httpFactory, IOptions<STTSettings> settings) {
        _httpClient = httpFactory.CreateClient("Vosk");
        _modelName = settings.Value.Vosk.ModelName;
    }

    public async Task<string> EnsureModelAsync(CancellationToken ct = default) {
        var modelDir = Path.Combine(ModelsRoot, _modelName);
        if (Directory.Exists(modelDir)) return modelDir;

        Directory.CreateDirectory(ModelsRoot);

        var tempZip = Path.Combine(ModelsRoot, $"{_modelName}.zip.tmp");
        var tempExtractDir = Path.Combine(ModelsRoot, $"{_modelName}.extracting");

        using var response = await _httpClient.GetAsync($"{_modelName}.zip", HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var fileStream = File.Create(tempZip);
        await response.Content.CopyToAsync(fileStream, ct);

        if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true);
        ZipFile.ExtractToDirectory(tempZip, tempExtractDir);
        File.Delete(tempZip);

        var extractedInner = Path.Combine(tempExtractDir, _modelName);
        Directory.Move(extractedInner, modelDir);
        Directory.Delete(tempExtractDir, true);

        return modelDir;
    }
}
