using LLama;
using LLama.Common;
using Microsoft.Extensions.Options;
using Yuki.Core.Configurations;
using Yuki.Core.ResourceManagement.Contracts;

namespace Yuki.Core.ResourceManagement.Services;

public class GpuLayerResolver(IVramProvider _vramProvider, IOptions<ResourceManagerSettings> _settings) : IGpuLayerResolver {
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
        var freeVramMb = _vramProvider.GetFreeVramMb() - _settings.Value.ReservedVramMb;

        return ResourceCalculator.ComputeGpuLayers(freeVramMb, kvCacheMb, perLayerMb, handle.LayerCount);
    }
}
