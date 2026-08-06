using Yuki.Core.ResourceManagement;

namespace Yuki.Core.Tests;

public class ResourceCalculatorTests {
    [Theory]
    [InlineData(1524, 0, 200, 100, 2)]
    [InlineData(3524, 1024, 200, 100, 7)]
    [InlineData(500, 0, 200, 100, 0)]
    [InlineData(5000, 4500, 200, 100, 0)]
    [InlineData(50000, 0, 100, 10, 10)]
    public void ComputeGpuLayers_RoundsDown_NotUp(long freeVramMb, long kvCacheMb, long perLayerMb, int totalLayers, int expected) {
        var result = ResourceCalculator.ComputeGpuLayers(freeVramMb, kvCacheMb, perLayerMb, totalLayers);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ComputeGpuLayers_ThrowsZeroSizeLayer() {
        Assert.Throws<ArgumentOutOfRangeException>(() => ResourceCalculator.ComputeGpuLayers(
            freeVramMb: 5000,
            kvCacheMb: 0,
            perLayerMb: 0,
            totalLayers: 100
        ));
    }
}
