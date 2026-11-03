using LLama;
using LLama.Common;

namespace Yuki.Core.Wrappers.Services;

public class LlamaWeightsProvider : IDisposable {
    private readonly string _modelPath;
    public LLamaWeights Weights { get; private set; }
    public int GpuLayerCount { get; private set; }

    public event Action<LLamaWeights>? WeightsReloaded;

    public LlamaWeightsProvider(string modelPath, int gpuLayerCount) {
        _modelPath = modelPath;
        GpuLayerCount = gpuLayerCount;
        Weights = LoadWeights(gpuLayerCount);
    }

    public void ReloadWeights(int newGpuLayerCount) {
        Weights.Dispose();
        GpuLayerCount = newGpuLayerCount;
        Weights = LoadWeights(newGpuLayerCount);

        WeightsReloaded?.Invoke(Weights);
    }

    private LLamaWeights LoadWeights(int layers) => LLamaWeights.LoadFromFile(new ModelParams(_modelPath) { GpuLayerCount = layers });

    public void Dispose() => Weights?.Dispose();
}
