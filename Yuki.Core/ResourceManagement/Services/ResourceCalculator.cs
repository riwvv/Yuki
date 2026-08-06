namespace Yuki.Core.ResourceManagement.Services;

public static class ResourceCalculator {
    public const long SAFETY_MARGIN_MB = 1024;
    public static int ComputeGpuLayers(long freeVramMb, long kvCacheMb, long perLayerMb, int totalLayers) {
        if (perLayerMb <= 0) throw new ArgumentOutOfRangeException(nameof(perLayerMb), "Размер слоя должен быть положительным");

        var usableBudget = freeVramMb - SAFETY_MARGIN_MB - kvCacheMb;
        if (usableBudget <= 0) return 0;

        var layers = (int)(usableBudget / perLayerMb);
        return Math.Clamp(layers, 0, totalLayers);
    }

    public static long ComputeKvCacheMb(int layerCount, int kvHeadCount, int embeddingSize, int headCount, int contextLength, int bytesPerElement = 2) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(headCount);

        var headDim = embeddingSize / headCount;
        var kvCacheBytes = 2L * layerCount * kvHeadCount * headDim * contextLength * bytesPerElement;
        return kvCacheBytes / (1024 * 1024);
    }
}
