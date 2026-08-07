using LLama;
using LLama.Common;
using Microsoft.Extensions.Options;
using Yuki.Core.Configurations;
using Yuki.Core.ResourceManagement.Contracts;

namespace Yuki.Core.ResourceManagement.Services;

public class GpuLayerResolver(IVramProvider _vramProvider, IOptions<ResourceManagerSettings> _settings) : IGpuLayerResolver {
    private long _kvCacheMb, _perLayerMb;
    private int _layerCount;
    private bool _isResolved;

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

        var perLayerMb = (long)(handle.SizeInBytes / ((ulong)handle.LayerCount * 1024 * 1024));
        var freeVramMb = _vramProvider.GetFreeVramMb() - _settings.Value.ReservedVramMb;

        _kvCacheMb = kvCacheMb;
        _perLayerMb = perLayerMb;
        _layerCount = handle.LayerCount;
        _isResolved = true;

        return ResourceCalculator.ComputeGpuLayers(freeVramMb, _kvCacheMb, _perLayerMb, _layerCount);
    }

    public int RecomputeGpuLayers(long freeVramMb, int currentLayers) {
        if (!_isResolved) throw new InvalidOperationException();

        var reclaimableMb = currentLayers * _perLayerMb;
        var effectiveFreeVramMb = freeVramMb + reclaimableMb - _settings.Value.ReservedVramMb;
        return ResourceCalculator.ComputeGpuLayers(effectiveFreeVramMb, _kvCacheMb, _perLayerMb, _layerCount);
    }
}
