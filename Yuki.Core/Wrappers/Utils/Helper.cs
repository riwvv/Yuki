using System.Text.Json;
using Yuki.Core.TTS.Models;
using Yuki.Core.TTS.Services;

namespace Yuki.Core.Wrappers.Utils;

public static class Helper {
    public static Config LoadCfgs(string onnxDir) {
        var json = File.ReadAllText(Path.Combine(onnxDir, "tts.json"));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new Config {
            AE = new Config.AEConfig {
                SampleRate = root.GetProperty("ae").GetProperty("sample_rate").GetInt32(),
                BaseChunkSize = root.GetProperty("ae").GetProperty("base_chunk_size").GetInt32()
            },
            TTL = new Config.TTLConfig {
                ChunkCompressFactor = root.GetProperty("ttl").GetProperty("chunk_compress_factor").GetInt32(),
                LatentDim = root.GetProperty("ttl").GetProperty("latent_dim").GetInt32()
            }
        };
    }

    public static Style LoadVoiceStyle(string voiceStylePath) {
        var json = File.ReadAllText(voiceStylePath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var ttlDims = ParseInt64Array(root.GetProperty("style_ttl").GetProperty("dims"));
        var dpDims = ParseInt64Array(root.GetProperty("style_dp").GetProperty("dims"));

        var ttlFlat = Flatten3DArray(root.GetProperty("style_ttl").GetProperty("data"));
        var dpFlat = Flatten3DArray(root.GetProperty("style_dp").GetProperty("data"));

        return new Style(ttlFlat, ttlDims, dpFlat, dpDims);
    }

    private static float[] Flatten3DArray(JsonElement element) {
        var list = new List<float>();
        foreach (var b in element.EnumerateArray())
            foreach (var r in b.EnumerateArray())
                foreach (var v in r.EnumerateArray())
                    list.Add(v.GetSingle());
        return [.. list];
    }

    private static long[] ParseInt64Array(JsonElement element) {
        return [.. element.EnumerateArray().Select(v => v.GetInt64())];
    }

    public static TextToSpeechCore LoadTextToSpeech(string onnxDir) {
        var cfgs = LoadCfgs(onnxDir);
        return new TextToSpeechCore(cfgs, onnxDir);
    }
}