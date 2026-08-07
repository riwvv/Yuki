namespace Yuki.Core.ResourceManagement.Contracts;

public interface IGpuLoadProvider {
    float GetGpuCoreLoadPercent();
}
