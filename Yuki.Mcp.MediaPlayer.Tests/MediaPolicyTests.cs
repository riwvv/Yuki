using Yuki.Mcp.MediaPlayer.Tools;

namespace Yuki.Mcp.MediaPlayer.Tests;

public class MediaPolicyTests {
    [Theory]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    public void Volume_AcceptsAllowedRange(int input, int expected) {
        var ok = MediaPolicy.TryValidateVolume(input, out var percent, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(expected, percent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Volume_RejectsOutOfRange(int input) {
        var ok = MediaPolicy.TryValidateVolume(input, out _, out var error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public async Task GuardAsync_ReturnsValue() {
        var result = await MediaPolicy.GuardAsync(() => Task.FromResult(42));

        Assert.True(result.Success);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task GuardAsync_DoesNotExposeExceptionMessage() {
        var result = await MediaPolicy.GuardAsync<int>(() => throw new InvalidOperationException("SECRET_INTERNAL_DETAIL"));

        Assert.False(result.Success);
        Assert.DoesNotContain("SECRET_INTERNAL_DETAIL", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GuardAsync_EnforcesTimeout() {
        var never = new TaskCompletionSource<int>().Task;

        var result = await MediaPolicy.GuardAsync(() => never, TimeSpan.FromMilliseconds(20));

        Assert.False(result.Success);
        Assert.Contains("лимит", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
