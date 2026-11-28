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

    [Fact]
    public async Task Events_MarkReportSeparately_AndCompleteOnlyAfterQueueDrains() {
        var queued = 0;
        var reports = 0;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var player = new TextToSpeechPlayer(
            new ThrowingTtsCore(),
            new Style([], [1], [], [1]),
            NullLogger<TextToSpeechPlayer>.Instance);

        player.SpeechQueued += () => queued++;
        player.ReportQueued += () => reports++;
        player.SpeechCompleted += () => completed.TrySetResult();

        player.EnqueueText("привет");
        player.EnqueueReport("отчёт");

        Assert.Equal(1, queued);
        Assert.Equal(1, reports);

        var finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(2))) == completed.Task;
        Assert.True(finished);
    }

    private sealed class ThrowingTtsCore : ITextToSpeechCore {
        public int SampleRate => 16000;

        public (float[] wav, float[] duration) Call(string text, Style style, int totalStep = 6, float speed = 1.05f)
            => throw new InvalidOperationException("Тест: синтез не нужен");

        public void Dispose() { }
    }
}
