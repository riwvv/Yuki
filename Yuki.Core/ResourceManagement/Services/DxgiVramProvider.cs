using Vortice.DXGI;
using Yuki.Core.ResourceManagement.Contracts;

namespace Yuki.Core.ResourceManagement.Services;

public class DxgiVramProvider : IVramProvider, IDisposable {
    private readonly IDXGIFactory1 _factory;
    private readonly IDXGIAdapter1 _adapter1;
    private readonly IDXGIAdapter3 _adapter3;
    private bool _disposed;

    public DxgiVramProvider() {
        DXGI.CreateDXGIFactory1(out IDXGIFactory1? rawFactory).CheckError();
        var factory = rawFactory!;
        factory.EnumAdapters1(0, out IDXGIAdapter1? rawAdapter1).CheckError();
        var adapter1 = rawAdapter1!;

        _factory = factory;
        _adapter1 = adapter1;
        _adapter3 = _adapter1.QueryInterface<IDXGIAdapter3>();
    }

    public long GetFreeVramMb() {
        var info = _adapter3.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local);
        var freeBytes = info.Budget > info.CurrentUsage ? info.Budget - info.CurrentUsage : 0;
        return (long)(freeBytes / (1024 * 1024));
    }

    public void Dispose() {
        if (!_disposed) {
            _adapter3?.Dispose();
            _adapter1?.Dispose();
            _factory?.Dispose();
            _disposed = true;
        }
    }
}
