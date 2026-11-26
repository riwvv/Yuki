using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.Channels;
using Yuki.Core.Configurations;
using Yuki.Core.ServantAgent.Contracts;
using Yuki.Core.STT.Contracts;
using Yuki.Core.TTS.Contracts;

namespace Yuki.Core.STT.Services;

public class VoiceInteractionService(
    IWakeWordListener _wakeWordListener,
    IAudioCaptureService _audioCapture,
    IVoiceActivityDetector _vad,
    ISpeechRecognizer _recognizer,
    ITextToSpeechPlayer _tts,
    IServantAgentService _servant,
    IOptions<STTSettings> _settings,
    ILogger<VoiceInteractionService> _logger) : IVoiceInteractionService {
    private VoiceInteractionState _state;
    public VoiceInteractionState State => _state;

    public event Action<VoiceInteractionState>? StateChanged;
    public Func<string, Task>? RequestReady { get; set; }

    private Channel<float[]>? _audioChannel;
    private CancellationTokenSource? _listeningCts;
    private CancellationTokenSource? _followUpCts;
    private bool _followUpArmed;

    public void Start() => _wakeWordListener.WakeWordDetected += OnWakeWordDetected;

    public void Stop() {
        _wakeWordListener.WakeWordDetected -= OnWakeWordDetected;
        _audioCapture.AudioAvailable -= OnAudioChunkReceived;
        CancelFollowUp();
        _listeningCts?.Cancel();
        _audioChannel = null;
    }

    public bool TryBeginBusy() {
        if (_state != VoiceInteractionState.Idle) return false;

        CancelFollowUp();
        SetState(VoiceInteractionState.Processing);
        return true;
    }

    public void SayAsync(string prompt) {
        if (!string.IsNullOrWhiteSpace(prompt))
            RequestReady?.Invoke(prompt);
    }

    // Возвращаем ключевое слово сразу: иначе во время долгого TTS Юки
    // остаётся совсем глухой. Окно повторного обращения откроется отдельно,
    // когда WaitForFollowUpAsync дождётся реальной тишины.
    public void EndBusy() {
        SetState(VoiceInteractionState.Idle);
        ResumeWakeWord();
        ArmFollowUp();
    }

    private void OnWakeWordDetected(float probability) {
        if (_state != VoiceInteractionState.Idle) return;

        // Пока Юки говорит, не включаем распознавание — иначе она ловит сама себя.
        if (_tts.IsSpeaking) {
            _logger.LogDebug("Голос: ключевое слово проигнорировано — Юки ещё говорит");
            return;
        }

        CancelFollowUp();
        _logger.LogInformation("Голос: ключевое слово обнаружено ({Probability:F2}), начинаю слушать", probability);
        StartListening(isFollowUp: false, CancellationToken.None);
    }

    private void ArmFollowUp() {
        CancelFollowUp();

        var cts = new CancellationTokenSource();
        _followUpCts = cts;
        _followUpArmed = true;
        _ = WaitForFollowUpAsync(cts.Token);
    }

    private void CancelFollowUp() {
        _followUpArmed = false;
        _followUpCts?.Cancel();
        _followUpCts?.Dispose();
        _followUpCts = null;
    }

    private async Task WaitForFollowUpAsync(CancellationToken ct) {
        try {
            if (_settings.Value.FollowUpStartDelayMs > 0)
                await Task.Delay(_settings.Value.FollowUpStartDelayMs, ct);

            // Ждём не просто «TTS пуст», а устойчивой тишины: между ответом
            // Host и озвучкой результата Servant'а бывает короткий зазор.
            var quietMs = Math.Max(0, _settings.Value.FollowUpQuietMs);
            var deadline = DateTime.UtcNow.AddSeconds(60);
            DateTime? quietSince = null;

            while (DateTime.UtcNow < deadline) {
                var quiet = !_tts.IsSpeaking && _servant.IsIdle;
                quietSince = quiet ? quietSince ?? DateTime.UtcNow : null;

                if (quietSince.HasValue && DateTime.UtcNow - quietSince.Value >= TimeSpan.FromMilliseconds(quietMs))
                    break;

                await Task.Delay(100, ct);
            }

            if (ct.IsCancellationRequested || !_followUpArmed)
                return;

            if (_state != VoiceInteractionState.Idle || _tts.IsSpeaking || !_servant.IsIdle) {
                _logger.LogDebug("Голос: окно повторного обращения не открыто — Юки ещё говорит или выполняет задачу");
                return;
            }

            _logger.LogInformation("Голос: окно повторного обращения открыто на {Seconds:0.0} с",
                _settings.Value.FollowUpWindowMs / 1000.0);
            StartListening(isFollowUp: true, ct);
        }
        catch (OperationCanceledException) {
            // Пользователь снова позвал Юки или приложение останавливается.
        }
    }

    private void StartListening(bool isFollowUp, CancellationToken followUpToken) {
        _wakeWordListener.Pause();
        _vad.Reset();
        _recognizer.Reset();

        _audioChannel = Channel.CreateUnbounded<float[]>();
        _listeningCts = followUpToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(followUpToken)
            : new CancellationTokenSource();

        _audioCapture.AudioAvailable += OnAudioChunkReceived;
        SetState(VoiceInteractionState.Listening);

        var silenceTimeoutMs = isFollowUp
            ? _settings.Value.FollowUpWindowMs
            : _settings.Value.StartSilenceTimeoutMs;

        _ = ProcessAudioLoopAsync(_audioChannel.Reader, _listeningCts.Token, isFollowUp, silenceTimeoutMs);
    }

    private void OnAudioChunkReceived(float[] chunk) => _audioChannel?.Writer.TryWrite(chunk);

    private async Task ProcessAudioLoopAsync(ChannelReader<float[]> reader, CancellationToken ct, bool isFollowUp, double silenceTimeoutMs) {
        var silenceDuration = TimeSpan.Zero;
        var hasHeardSpeech = false;
        var timeSinceListeningStarted = TimeSpan.Zero;

        try {
            await foreach (var chunk in reader.ReadAllAsync(ct)) {
                var speechProbability = _vad.GetSpeechProbability(chunk);
                _recognizer.AcceptAudio(chunk);

                var chunkDuration = TimeSpan.FromSeconds((double)chunk.Length / _settings.Value.SampleRate);
                timeSinceListeningStarted += chunkDuration;

                if (speechProbability > _settings.Value.VadThreshold) {
                    hasHeardSpeech = true;
                    silenceDuration = TimeSpan.Zero;
                }
                else
                    silenceDuration += chunkDuration;

                if (hasHeardSpeech && silenceDuration.TotalMilliseconds >= _settings.Value.EndOfSpeectSilenceMs)
                    break;

                if (!hasHeardSpeech && timeSinceListeningStarted.TotalMilliseconds >= silenceTimeoutMs)
                    break;
            }
        }
        catch (OperationCanceledException) {
            return;
        }

        _logger.LogDebug(
            "Голос: слушание завершено — речь={Speech}, длительность={Duration:0.0} с",
            hasHeardSpeech, timeSinceListeningStarted.TotalSeconds);

        await FinishListeningAsync(isFollowUp, hasHeardSpeech);
    }

    private async Task FinishListeningAsync(bool isFollowUp, bool hasHeardSpeech) {
        _audioCapture.AudioAvailable -= OnAudioChunkReceived;
        _audioChannel = null;

        var transcript = _recognizer.GetFinalResult();

        if (string.IsNullOrWhiteSpace(transcript)) {
            if (isFollowUp)
                _logger.LogInformation("Голос: окно повторного обращения закрыто — {Reason}",
                    hasHeardSpeech ? "не удалось распознать речь" : "тишина");
            else
                _logger.LogWarning("Голос: пустой текст распознавания — команда пропущена");

            CloseSession();
            return;
        }

        _followUpArmed = false;
        SetState(VoiceInteractionState.Processing);

        if (RequestReady == null) {
            _logger.LogWarning("Голос: обработчик запроса не задан — команда пропущена");
            CloseSession();
            return;
        }

        if (isFollowUp)
            _logger.LogInformation("Голос: повторное обращение «{Text}»", transcript);

        await RequestReady.Invoke(transcript);
    }

    private void CloseSession() {
        SetState(VoiceInteractionState.Idle);
        ResumeWakeWord();
    }

    private void ResumeWakeWord() => _wakeWordListener.Resume();

    private void SetState(VoiceInteractionState newState) {
        _state = newState;
        StateChanged?.Invoke(newState);
    }

    public void Dispose() {
        Stop();
        _listeningCts?.Dispose();
    }
}
