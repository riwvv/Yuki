using LLama;
using LLama.Common;
using Yuki.Core.ResourceManagement.Contracts;

namespace Yuki.Core.ResourceManagement.Services;

public class GpuLayerResolver(IVramProvider _vramProvider) : IGpuLayerResolver {
    public int ResolveGpuLayers(string modelPath, int contextLength) {
        var probeParams = new ModelParams(modelPath) { GpuLayerCount = 0 };
        using var probeWeights = LLamaWeights.LoadFromFile(probeParams);
        var handle = probeWeights.NativeHandle;

        var kvCacheMb = ResourceCalculator.ComputeKvCacheMb(
            layerCount: handle.LayerCount,
            kvHeadCount: handle.KVHeadCount,
            embeddingSize: handle.EmbeddingSize,
            headCount: handle.HeadCount,
            contextLength: contextLength
        );

        var perLayerMb = (long)(handle.SizeInBytes / (ulong)handle.LayerCount / (1024 * 1024));
        var freeVramMb = _vramProvider.GetFreeVramMb();

        return ResourceCalculator.ComputeGpuLayers(freeVramMb, kvCacheMb, perLayerMb, handle.LayerCount);
    }
}
