using System.Threading.Channels;

namespace Yuki.Core.Tests;

public class ChannelReaderTests {
    [Fact]
    public void UnboundedReader_CountIsNotSupported() {
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = false
        });

        Assert.Throws<NotSupportedException>(() => _ = channel.Reader.Count);
    }

    [Fact]
    public void UnboundedReader_TryPeekWorksWithoutCount() {
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = false
        });
        Assert.False(channel.Reader.TryPeek(out _));

        channel.Writer.TryWrite("привет");

        Assert.True(channel.Reader.TryPeek(out var peeked));
        Assert.Equal("привет", peeked);
    }
}
