using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Threading.Channels;
using Yuki.Core.Configurations;
using Yuki.Core.HostAgent.Contracts;
using Yuki.Core.STT.Contracts;

namespace Yuki.Core.STT.Services;

public class VoiceInteractionService(IWakeWordListener _wakeWordListener, IAudioCaptureService _audioCapture, IVoiceActivityDetector _vad, ISpeechRecognizer _recognizer, IHostAgentService _hostAgent, IOptions<STTSettings> _settings, ILogger<VoiceInteractionService> _logger) : IVoiceInteractionService {
    private VoiceInteractionState _state;
    public VoiceInteractionState State => _state;

    public event Action<VoiceInteractionState>? StateChanged;
    public event Action<string>? ResponseReady;

    private Channel<float[]>? _audioChannel;
    private CancellationTokenSource? _listeningCts;

    public void Start() => _wakeWordListener.WakeWordDetected += OnWakeWordDetected;

    public void Stop() {
        _wakeWordListener.WakeWordDetected -= OnWakeWordDetected;
        _listeningCts?.Cancel();
    }

    public bool TryBeginBusy() {
        if (_state != VoiceInteractionState.Idle) return false;
        SetState(VoiceInteractionState.Processing); return true;
    }

    public async Task SayAsync(string prompt) {
        if (!string.IsNullOrWhiteSpace(prompt)) {
            var responseText = new StringBuilder();
            await foreach (var token in _hostAgent.RespondAsync(prompt))
                responseText.Append(token);
            ResponseReady?.Invoke(responseText.ToString());
        }
    }

    public void EndBusy() {
        SetState(VoiceInteractionState.Idle);
        _wakeWordListener.Resume();
    }

    private void OnWakeWordDetected(float probability) {
        if (_state != VoiceInteractionState.Idle) return;

        _logger.LogInformation($"Wake word detected ({probability:F2}) - начинаем слушать");

        _wakeWordListener.Pause();
        _vad.Reset();
        _recognizer.Reset();

        _audioChannel = Channel.CreateUnbounded<float[]>();
        _listeningCts = new CancellationTokenSource();
        _audioCapture.AudioAvailable += OnAudioChunkReceived;

        SetState(VoiceInteractionState.Listening);

        _ = ProcessAudioLoopAsync(_audioChannel.Reader, _listeningCts.Token);
    }

    private void OnAudioChunkReceived(float[] chunk) => _audioChannel?.Writer.TryWrite(chunk);

    private async Task ProcessAudioLoopAsync(ChannelReader<float[]> reader, CancellationToken ct) {
        var silenceDuration = TimeSpan.Zero;
        var hasHeardSpeech = false;

        try {
            await foreach (var chunk in reader.ReadAllAsync(ct)) {
                var speechProbability = _vad.GetSpeechProbability(chunk);
                _recognizer.AcceptAudio(chunk);

                var chunkDuration = TimeSpan.FromSeconds((double)chunk.Length / _settings.Value.SampleRate);

                if (speechProbability > _settings.Value.VadThreshold) {
                    hasHeardSpeech = true;
                    silenceDuration = TimeSpan.Zero;
                }
                else 
                    silenceDuration += chunkDuration;

                if (hasHeardSpeech && silenceDuration.TotalMilliseconds >= _settings.Value.EndOfSpeectSilenceMs)
                    break;
            }
        }
        catch (OperationCanceledException) {
            return;
        }

        await FinishListeningAsync();
    }

    private async Task FinishListeningAsync() {
        _audioCapture.AudioAvailable -= OnAudioChunkReceived;
        _audioChannel = null;

        var transcript = _recognizer.GetFinalResult();
        _logger.LogInformation($"Распознано: {transcript}");

        SetState(VoiceInteractionState.Processing);

        if (!string.IsNullOrWhiteSpace(transcript)) {
            var responseText = new StringBuilder();
            await foreach (var token in _hostAgent.RespondAsync(transcript))
                responseText.Append(token);
            ResponseReady?.Invoke(responseText.ToString());
        }

        SetState(VoiceInteractionState.Idle);
        _wakeWordListener.Resume();
    }

    private void SetState(VoiceInteractionState newState) {
        _state = newState;
        StateChanged?.Invoke(newState);
    }

    public void Dispose() {
        Stop();
    }
}
