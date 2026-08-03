using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yuki.Core.Configurations;
using Yuki.Core.STT.Contracts;

namespace Yuki.Core.STT.Services;

/// <summary>
/// Склеивает захват аудио (<see cref="IAudioCaptureService"/>) и детектор
/// (<see cref="IWakeWordDetector"/>): копит скользящее окно последних
/// N сэмплов и периодически проверяет его на наличие ключевого слова.
/// </summary>
public class WakeWordListener : IWakeWordListener {
    private readonly ILogger<WakeWordListener> _logger;
    private readonly IAudioCaptureService _audioCapture;
    private readonly IWakeWordDetector _detector;
    private readonly WakeWordSettings _settings;

    // Кольцевой буфер: фиксированного размера, новые сэмплы затирают старые
    // по кругу. Не пересоздаём массив на каждый чанк — только дописываем.
    private readonly float[] _ringBuffer;
    private int _writePosition;

    // Сколько сэмплов накопилось с прошлой проверки — считаем каждый чанк,
    // и когда накопится достаточно (CheckIntervalMs) — проверяем и сбрасываем.
    private int _samplesSinceLastCheck;
    private readonly int _checkIntervalSamples;

    // Пока флаг true — фоновая проверка ещё не закончилась, новую не запускаем.
    // Технически на этот флаг претендуют два потока (аудио-поток и Task.Run),
    // но цена гонки тут мягкая (лишняя параллельная проверка) — не усложняем.
    private bool _inferenceInProgress;

    private DateTime _lastDetectionUtc = DateTime.MinValue;

    // Сколько проверок подряд уже перешли порог — сбрасывается любой
    // проверкой ниже порога. Мутируется только внутри Task.Run в
    // OnAudioAvailable, а _inferenceInProgress гарантирует, что в моменте
    // выполняется не больше одного такого коллбэка — гонки нет.
    private int _consecutiveDetections;

    public event Action<float>? WakeWordDetected;

    public WakeWordListener(IAudioCaptureService audioCapture, IWakeWordDetector detector, IOptions<WakeWordSettings> settings, ILogger<WakeWordListener> logger) {
        _logger = logger;
        _audioCapture = audioCapture;
        _detector = detector;
        _settings = settings.Value;

        _ringBuffer = new float[_settings.NumSamples];
        _checkIntervalSamples = _settings.SampleRate * _settings.CheckIntervalMs / 1000;

        _audioCapture.AudioAvailable += OnAudioAvailable;
    }

    public void Start() => _audioCapture.Start();

    public void Stop() => _audioCapture.Stop();

    private void OnAudioAvailable(float[] samples) {
        _logger.LogInformation($"Audio chunk: {samples.Length} samples, max amplitude: {samples.Max(Math.Abs)}");
        // 1. Дописываем новый чанк в кольцевой буфер по кругу.
        foreach (var sample in samples) {
            _ringBuffer[_writePosition] = sample;
            _writePosition = (_writePosition + 1) % _ringBuffer.Length;
        }

        // 2. Проверяем не чаще, чем раз в CheckIntervalMs — незачем гонять
        // модель на каждый маленький чанк, окно почти не меняется за 100мс.
        _samplesSinceLastCheck += samples.Length;
        if (_samplesSinceLastCheck < _checkIntervalSamples) return;
        _samplesSinceLastCheck = 0;

        // 3. Cooldown — не даём тому же произнесённому слову сработать
        // повторно на следующей же проверке, пока оно ещё "сидит" в окне.
        if ((DateTime.UtcNow - _lastDetectionUtc).TotalSeconds < _settings.CooldownSeconds) return;

        // 4. Уже идёт проверка — не запускаем вторую поверх.
        if (_inferenceInProgress) return;

        // 5. Снимаем снапшот (копию, распрямлённую в хронологический порядок)
        // прямо тут, на аудио-потоке — это просто копирование памяти, быстро.
        var snapshot = GetOrderedSnapshot();
        _inferenceInProgress = true;

        // 6. А сам инференс — уже в фоне, чтобы не задерживать поток захвата
        // аудио, даже если модель вдруг отработает не мгновенно.
        Task.Run(() => {
            try {
                var (detected, probability) = _detector.DetectWithProbability(snapshot);

                if (!detected) {
                    _consecutiveDetections = 0;
                    return;
                }

                // Требуем несколько проверок подряд выше порога, прежде чем
                // считать это реальным произнесением слова — одиночный
                // всплеск на одном из перекрывающихся окон слишком часто
                // оказывался ложным срабатыванием.
                _consecutiveDetections++;
                if (_consecutiveDetections < _settings.RequiredConsecutiveDetections) return;

                _consecutiveDetections = 0;
                _lastDetectionUtc = DateTime.UtcNow;
                WakeWordDetected?.Invoke(probability);
            }
            finally {
                _inferenceInProgress = false;
            }
        });
    }

    /// <summary>
    /// Кольцевой буфер хранит сэмплы "по кругу" — сам по себе он не в
    /// хронологическом порядке. Модели нужен waveform от старого к новому,
    /// поэтому тут "распрямляем" буфер в обычный линейный массив.
    /// _writePosition — это индекс, куда запишется следующий сэмпл,
    /// то есть ровно там же лежит самый старый ещё не перезаписанный сэмпл.
    /// </summary>
    private float[] GetOrderedSnapshot() {
        var snapshot = new float[_ringBuffer.Length];
        Array.Copy(_ringBuffer, _writePosition, snapshot, 0, _ringBuffer.Length - _writePosition);
        Array.Copy(_ringBuffer, 0, snapshot, _ringBuffer.Length - _writePosition, _writePosition);
        return snapshot;
    }

    public void Dispose() {
        _audioCapture.AudioAvailable -= OnAudioAvailable;
        _audioCapture.Dispose();
    }
}
