namespace Yuki.Core.ResourceManagement.Contracts;

public interface IGpuLayerResolver {
    int ResolveGpuLayers(string modelPath, int contextLength);
}
