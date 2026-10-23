using Microsoft.Extensions.Logging;
using System.Threading.Channels;
using NAudio.Wave;
using Yuki.Core.TTS.Contracts;
using Yuki.Core.TTS.Models;

namespace Yuki.Core.TTS.Services;

public sealed class TextToSpeechPlayer : ITextToSpeechPlayer {
    private readonly ITextToSpeechCore _tts;
    private readonly Style _style;
    private readonly ILogger<TextToSpeechPlayer> _logger;

    private readonly Channel<string> _queue;
    private readonly CancellationTokenSource _globalCts;
    private readonly Lock _playbackLock;
    private readonly Task _workerTask;

    private IWavePlayer? _currentOutput;
    private CancellationTokenSource? _currentPlaybackCts;

    public TextToSpeechPlayer(ITextToSpeechCore tts, Style style, ILogger<TextToSpeechPlayer> logger) {
        _tts = tts;
        _style = style;
        _logger = logger;

        _globalCts = new CancellationTokenSource();
        _playbackLock = new Lock();
        _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = false
        });

        _workerTask = Task.Run(ProcessQueueAsync);
    }

    public void EnqueueText(string text) {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!_queue.Writer.TryWrite(text))
            _logger.LogWarning("Не удалось добавить текст в очередь TTS: {Text}", text);
    }

    public void Stop() {
        _logger.LogInformation("TTS: прерывание воспроизведения");
        lock (_playbackLock) {
            _currentPlaybackCts?.Cancel();
            _currentOutput?.Stop();
        }
        while (_queue.Reader.TryRead(out _)) { }
    }

    public bool IsSpeaking {
        get {
            lock (_playbackLock) {
                return _queue.Reader.Count > 0 || _currentOutput?.PlaybackState == PlaybackState.Playing;
            }
        }
    }

    private async Task ProcessQueueAsync() {
        try {
            await foreach (var text in _queue.Reader.ReadAllAsync(_globalCts.Token)) {
                CancellationTokenSource playbackCts;
                lock (_playbackLock) {
                    _currentPlaybackCts?.Dispose();
                    playbackCts = new CancellationTokenSource();
                    _currentPlaybackCts = playbackCts;
                }

                try {
                    var (wav, _) = await Task.Run(() =>
                        _tts.Call(text: text, style: _style, totalStep: 7, speed: 1.1f),
                        playbackCts.Token);

                    await PlayAudioAsync(wav, playbackCts.Token);
                }
                catch (OperationCanceledException) {
                    _logger.LogDebug("TTS: синтез/воспроизведение прервано пользователем (Barge-in)");
                }
                catch (Exception ex) {
                    _logger.LogError(ex, "TTS: ошибка синтеза/воспроизведения текста: {Text}", text);
                }
            }
        }
        catch (OperationCanceledException) {
            _logger.LogInformation("TTS: воркер очереди остановлен штатно");
        }
        catch (Exception ex) {
            _logger.LogCritical(ex, "TTS: фатальная ошибка в фоновом воркере очереди");
        }
    }

    private async Task PlayAudioAsync(float[] samples, CancellationToken ct) {
        int sampleRate = _tts.SampleRate;

        int silenceSamples = (int)(sampleRate * 0.05f);
        float[] padded = new float[samples.Length + silenceSamples];
        Array.Copy(samples, padded, samples.Length);

        byte[] buffer = new byte[padded.Length * sizeof(float)];
        Buffer.BlockCopy(padded, 0, buffer, 0, buffer.Length);

        var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
        using var stream = new RawSourceWaveStream(new MemoryStream(buffer), waveFormat);

        var playbackFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_playbackLock) {
            _currentOutput = new WaveOut();
            _currentOutput.PlaybackStopped += (_, _) => playbackFinished.TrySetResult();
            _currentOutput.Init(stream);
            _currentOutput.Play();
        }

        try {
            await playbackFinished.Task.WaitAsync(ct);
        }
        finally {
            lock (_playbackLock) {
                if (_currentOutput != null) {
                    _currentOutput.Stop();
                    _currentOutput.Dispose();
                    _currentOutput = null;
                }
            }
        }
    }

    public void Dispose() {
        _logger.LogInformation("TTS: завершение работы плеера");
        _globalCts.Cancel();
        _queue.Writer.TryComplete();
        Stop();

        try {
            _workerTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException ae) when (ae.InnerExceptions.All(e => e is OperationCanceledException)) {
            _logger.LogDebug("TTS: фоновый воркер успешно остановлен (отмена)");
        }
        catch (AggregateException ae) {
            _logger.LogError(ae, "TTS: ошибка при завершении фонового воркера");
        }

        lock (_playbackLock) {
            _currentPlaybackCts?.Dispose();
        }
        _globalCts.Dispose();
    }
}