using Microsoft.Extensions.Logging.Abstractions;
using Yuki.Core.TTS.Contracts;
using Yuki.Core.TTS.Models;
using Yuki.Core.TTS.Services;

namespace Yuki.Core.Tests;

public class TextToSpeechPlayerTests {
    [Fact]
    public void IsSpeaking_DoesNotThrow_WithSingleReaderChannel() {
        using var player = new TextToSpeechPlayer(
            new ThrowingTtsCore(),
            new Style([], [1], [], [1]),
            NullLogger<TextToSpeechPlayer>.Instance);

        Assert.Null(Record.Exception(() => _ = player.IsSpeaking));

        player.EnqueueText("привет");

        Assert.Null(Record.Exception(() => _ = player.IsSpeaking));
    }

    private sealed class ThrowingTtsCore : ITextToSpeechCore {
        public int SampleRate => 16000;

        public (float[] wav, float[] duration) Call(string text, Style style, int totalStep = 6, float speed = 1.05f)
            => throw new InvalidOperationException("Тест: синтез не нужен");

        public void Dispose() { }
    }
}
