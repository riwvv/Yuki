using System.Text.Json;
using Yuki.Mcp.MediaPlayer.Tools;

namespace Yuki.Mcp.MediaPlayer.Tests;

public class MediaPlayerToolsTests {
    [Fact]
    public async Task ChangeVolume_RequiresConfirmation() {
        var json = await MediaPlayerTools.ChangeVolume(50);

        AssertError(json, "confirmation_required");
    }

    [Fact]
    public async Task VolumeTurnOff_RequiresConfirmation() {
        var json = await MediaPlayerTools.VolumeTurnOff();

        AssertError(json, "confirmation_required");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    public async Task ChangeVolume_RejectsInvalidVolumeBeforeTouchingAudio(int volume) {
        var json = await MediaPlayerTools.ChangeVolume(volume, confirm: true);

        AssertError(json, "invalid_volume");
    }

    [Fact]
    public async Task Errors_UseUnifiedFormat() {
        var responses = new[] {
            await MediaPlayerTools.ChangeVolume(50),
            await MediaPlayerTools.VolumeTurnOff(),
            await MediaPlayerTools.ChangeVolume(101, confirm: true)
        };

        foreach (var json in responses) {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.Equal("error", root.GetProperty("status").GetString());
            Assert.False(root.TryGetProperty("message", out _));
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("cause").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("description").GetString()));
        }
    }

    private static void AssertError(string json, string expectedCause) {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("error", root.GetProperty("status").GetString());
        Assert.Equal(expectedCause, root.GetProperty("cause").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("description").GetString()));
    }
}
