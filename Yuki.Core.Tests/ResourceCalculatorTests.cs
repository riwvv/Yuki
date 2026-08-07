using Yuki.Core.ResourceManagement.Services;

namespace Yuki.Core.Tests;

public class ResourceCalculatorTests {
    [Theory]
    [InlineData(1524, 0, 200, 100, 7)]
    [InlineData(3524, 1024, 200, 100, 12)]
    [InlineData(500, 0, 200, 100, 2)]
    [InlineData(5000, 4500, 200, 100, 2)]
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

    [Theory]
    [InlineData(36, 2, 2048, 16, 4096, 2)]
    public void ComputeKvCacheMb_CalculatesCorrectly(int layerCount, int kvHeadCount, int embeddingSize, int headCount, int contextLength, int bytesPerElement = 2) {
        var result = ResourceCalculator.ComputeKvCacheMb(layerCount, kvHeadCount, embeddingSize, headCount, contextLength, bytesPerElement);
        Assert.Equal(144, result);
    }
}
