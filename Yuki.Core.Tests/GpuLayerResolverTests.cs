using LLama.Native;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Yuki.Core.Tests;

public class GpuLayerResolverTests(ITestOutputHelper _output) {
    private const string MODEL_PATH = @"C:\Users\riwvv\source\repos\llama-test\models\qwen2.5-3b-instruct-q4_k_m.gguf";

    [Fact(Skip = "LLamaSharp не грузит нативную llama.dll под VSTest test host — известная проблема, не связана с логикой")]
    public void VocabOnly_ReturnsTheSameMetadataAsAFullLoad() {
        var sw = Stopwatch.StartNew();
        var vocabParams = LLamaModelParams.Default();
        vocabParams.vocab_only = true;
        using var vocabHandle = SafeLlamaModelHandle.LoadFromFile(MODEL_PATH, vocabParams);
        var vocabMs = sw.ElapsedMilliseconds;

        sw.Restart();
        var fullParams = LLamaModelParams.Default();
        fullParams.n_gpu_layers = 0;
        using var fullHandle = SafeLlamaModelHandle.LoadFromFile(MODEL_PATH, fullParams);
        var fullMs = sw.ElapsedMilliseconds;

        _output.WriteLine($"vocab_only: {vocabMs}ms, LayerCount={vocabHandle.LayerCount}, SizeInBytes={vocabHandle.SizeInBytes}");
        _output.WriteLine($"vocab_only: {fullMs}ms, LayerCount={fullHandle.LayerCount}, SizeInBytes={fullHandle.SizeInBytes}");

        Assert.Equal(fullHandle.LayerCount, vocabHandle.LayerCount);
        Assert.Equal(fullHandle.SizeInBytes, vocabHandle.SizeInBytes);
        Assert.Equal(fullHandle.KVHeadCount, vocabHandle.KVHeadCount);
        Assert.Equal(fullHandle.HeadCount, vocabHandle.HeadCount);
        Assert.Equal(fullHandle.EmbeddingSize, vocabHandle.EmbeddingSize);
    }
}
