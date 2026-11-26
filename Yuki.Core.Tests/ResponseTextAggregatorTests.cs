using System.Runtime.CompilerServices;
using Yuki.Core.TTS.Utils;

namespace Yuki.Core.Tests;

public class ResponseTextAggregatorTests {
    [Fact]
    public async Task AggregateAsync_JoinsTokensIntoOneUtterance() {
        var tokens = Tokens(["Привет", "! ", "Как ", " дела?", "  "]);

        var text = await ResponseTextAggregator.AggregateAsync(tokens);

        Assert.Equal("Привет! Как дела?", text);
    }

    [Fact]
    public async Task AggregateAsync_ReturnsEmptyString_WhenNoTokens() {
        var text = await ResponseTextAggregator.AggregateAsync(Tokens([]));

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public async Task AggregateAsync_StopsOnCancellation() {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ResponseTextAggregator.AggregateAsync(Tokens(["а", "б"]), cts.Token));
    }

    private static async IAsyncEnumerable<string> Tokens(
        IEnumerable<string> values,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        foreach (var value in values) {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return value;
        }
    }
}
