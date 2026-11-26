using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Yuki.Core.Configurations;
using Yuki.Core.ServantAgent.Contracts;
using Yuki.Core.STT.Contracts;
using Yuki.Core.STT.Services;
using Yuki.Core.TTS.Contracts;

namespace Yuki.Core.Tests;

public class VoiceInteractionServiceTests {
    [Fact]
    public async Task EndBusy_ResumesWakeWord_ButDoesNotOpenWindow_WhileTtsSpeaking() {
        var wake = new FakeWakeWordListener();
        var tts = new FakeTts { IsSpeaking = true };
        var service = Create(wake: wake, tts: tts);

        Assert.True(service.TryBeginBusy());
        service.EndBusy();

        await Task.Delay(250);

        // Ключевое слово вернулось сразу, иначе Юки остаётся глухой.
        // А окно слушания не открылось, пока она говорит.
        Assert.Equal(VoiceInteractionState.Idle, service.State);
        Assert.Equal(1, wake.ResumeCount);
        Assert.Equal(0, wake.PauseCount);
    }

    [Fact]
    public void WakeWord_IsIgnored_WhileTtsSpeaking() {
        var wake = new FakeWakeWordListener();
        var tts = new FakeTts { IsSpeaking = true };
        var service = Create(wake: wake, tts: tts);

        service.Start();
        wake.Raise();

        Assert.Equal(VoiceInteractionState.Idle, service.State);
        Assert.Equal(0, wake.PauseCount);
    }

    [Fact]
    public async Task FollowUp_WaitsForServant_BeforeOpeningWindow() {
        var wake = new FakeWakeWordListener();
        var servant = new FakeServant { IsIdle = false };
        var service = Create(wake: wake, servant: servant);

        Assert.True(service.TryBeginBusy());
        service.EndBusy();

        await Task.Delay(400);
        Assert.Equal(VoiceInteractionState.Idle, service.State);
        Assert.Equal(1, wake.ResumeCount);

        servant.IsIdle = true;
        Assert.True(await WaitUntilAsync(() => service.State == VoiceInteractionState.Listening, TimeSpan.FromSeconds(2)));
        Assert.Equal(1, wake.ResumeCount); // окно открылось — ключевое слово снова на паузе
    }

    [Fact]
    public async Task FollowUp_AcceptsRepeatRequest_WithoutWakeWord() {
        var wake = new FakeWakeWordListener();
        var audio = new FakeAudioCapture();
        var vad = new FakeVad(1f);
        var recognizer = new FakeRecognizer();
        string? received = null;

        var service = Create(wake: wake, audio: audio, vad: vad, recognizer: recognizer);
        service.RequestReady = text => {
            received = text;
            return Task.CompletedTask;
        };

        Assert.True(service.TryBeginBusy());
        service.EndBusy();

        Assert.True(await WaitUntilAsync(() => service.State == VoiceInteractionState.Listening, TimeSpan.FromSeconds(2)));
        Assert.Equal(1, wake.ResumeCount);

        recognizer.Result = "ещё вопрос";
        audio.Emit(new float[1600]);

        Assert.True(await WaitUntilAsync(() => received != null, TimeSpan.FromSeconds(2)));
        Assert.Equal("ещё вопрос", received);
        Assert.Equal(VoiceInteractionState.Processing, service.State);
    }

    [Fact]
    public async Task FollowUp_ExpiresOnSilence_AndResumesWakeWord() {
        var wake = new FakeWakeWordListener();
        var audio = new FakeAudioCapture();
        var vad = new FakeVad(0f);
        var service = Create(wake: wake, audio: audio, vad: vad, followUpWindowMs: 100);

        Assert.True(service.TryBeginBusy());
        service.EndBusy();

        Assert.True(await WaitUntilAsync(() => service.State == VoiceInteractionState.Listening, TimeSpan.FromSeconds(2)));

        // Один тихий чанк на секунду длиннее окна — слушание должно закрыться.
        audio.Emit(new float[16000]);

        Assert.True(await WaitUntilAsync(() => wake.ResumeCount == 2, TimeSpan.FromSeconds(2)));
        Assert.Equal(VoiceInteractionState.Idle, service.State);
    }

    private static VoiceInteractionService Create(
        FakeWakeWordListener? wake = null,
        FakeAudioCapture? audio = null,
        FakeVad? vad = null,
        FakeRecognizer? recognizer = null,
        FakeTts? tts = null,
        FakeServant? servant = null,
        int followUpWindowMs = 2000) {
        return new VoiceInteractionService(
            wake ?? new FakeWakeWordListener(),
            audio ?? new FakeAudioCapture(),
            vad ?? new FakeVad(0f),
            recognizer ?? new FakeRecognizer(),
            tts ?? new FakeTts(),
            servant ?? new FakeServant(),
            Options.Create(new STTSettings {
                SampleRate = 16000,
                VadThreshold = 0.5f,
                EndOfSpeectSilenceMs = 0,
                StartSilenceTimeoutMs = 1000,
                FollowUpWindowMs = followUpWindowMs,
                FollowUpStartDelayMs = 0,
                FollowUpQuietMs = 50
            }),
            NullLogger<VoiceInteractionService>.Instance);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout) {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline) {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    private sealed class FakeWakeWordListener : IWakeWordListener {
        public event Action<float>? WakeWordDetected;
        public int PauseCount;
        public int ResumeCount;

        public void Start() { }
        public void Stop() { }
        public void Pause() => PauseCount++;
        public void Resume() => ResumeCount++;
        public void Raise(float probability = 0.9f) => WakeWordDetected?.Invoke(probability);
        public void Dispose() { }
    }

    private sealed class FakeAudioCapture : IAudioCaptureService {
        public event Action<float[]>? AudioAvailable;

        public void Start() { }
        public void Stop() { }
        public void Emit(float[] chunk) => AudioAvailable?.Invoke(chunk);
        public void Dispose() { }
    }

    private sealed class FakeVad(float probability) : IVoiceActivityDetector {
        public float Probability { get; set; } = probability;

        public void Reset() { }
        public float GetSpeechProbability(float[] chunk) => Probability;
        public void Dispose() { }
    }

    private sealed class FakeRecognizer : ISpeechRecognizer {
        public string Result = string.Empty;

        public void Reset() => Result = string.Empty;
        public void AcceptAudio(float[] chunk) { }
        public string GetFinalResult() => Result;
        public void Dispose() { }
    }

    private sealed class FakeTts : ITextToSpeechPlayer {
        public bool IsSpeaking { get; set; }

        public void EnqueueText(string text) { }
        public void Dispose() { }
    }

    private sealed class FakeServant : IServantAgentService {
        public bool IsIdle { get; set; } = true;
        public event Action<string>? TaskCompleted;

        public void RaiseCompleted(string result) => TaskCompleted?.Invoke(result);
        public Task ExecuteTaskAsync(string taskDescription, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> TryAcquireReloadLockAsync(TimeSpan timeout, CancellationToken ct = default) => Task.FromResult(true);
        public void ReleaseReloadLock() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
