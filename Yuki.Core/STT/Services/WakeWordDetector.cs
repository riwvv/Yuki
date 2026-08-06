using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Yuki.Core.Configurations;
using Yuki.Core.STT.Contracts;
using Yuki.Core.Wrappers.Utils;

namespace Yuki.Core.STT.Services;

/// <summary>
/// Детектор wake word на трёх заморожённых/обученных onnx-моделях:
/// mel-спектрограмма -> Google speech_embedding (обе — фиксированный
/// предобученный бэкенд openWakeWord) -> наш классификатор поверх
/// эмбеддингов. Раньше был один onnx с CNN, обученной с нуля на паре тысяч
/// клипов — эта версия учится только различать "Юки" в уже готовом
/// акустическом пространстве, которое бэкенд выучил на огромном объёме речи.
/// </summary>
public class WakeWordDetector(IOptions<WakeWordSettings> settings, ILogger<WakeWordDetector> logger) : IWakeWordDetector, IDisposable {
    // Структурные константы бэкенда openWakeWord — фиксированы его
    // архитектурой, а не конкретной обученной моделью "Юки", поэтому не в
    // WakeWordSettings (тот содержит только то, что реально может меняться
    // между переобучениями: NumSamples/SampleRate/Threshold).
    private const int WindowSize = 76;
    private const int StepSize = 8;
    private const int EmbeddingDim = 96;
    private const int NumWindows = 3;

    private const string ClassifierInputName = "features";
    private const string ClassifierOutputName = "probabilities";

    private readonly InferenceSession _melSession = new(Utils.LoadWakeWord("melspectrogram.onnx"));
    private readonly InferenceSession _embeddingSession = new(Utils.LoadWakeWord("embedding_model.onnx"));
    private readonly InferenceSession _classifierSession = new(Utils.LoadWakeWord("wakeword_classifier.onnx"));
    private readonly float _threshold = settings.Value.Threshold;

    private bool _disposed = false;

    public bool Detect(float[] audioSamples) => DetectWithProbability(audioSamples).detected;

    /// <summary>
    /// Проверяет, содержит ли аудио-фрагмент wake word, и возвращает вероятность.
    /// </summary>
    /// <param name="audioSamples">Аудио-данные (float, частота config.sample_rate)</param>
    public (bool detected, float probability) DetectWithProbability(float[] audioSamples) {
        if (audioSamples == null || audioSamples.Length == 0)
            return (false, 0f);

        try {
            var waveform = PrepareWaveform(audioSamples);
            var mel = ComputeMelspectrogram(waveform);
            // Нормализация (train-статистика mean/std) запечена в сам onnx
            // классификатора при экспорте — тут просто сырые эмбеддинги.
            var features = ComputeFeatures(mel);

            var inputTensor = new DenseTensor<float>(features, [1, features.Length]);
            var inputs = new List<NamedOnnxValue> {
                NamedOnnxValue.CreateFromTensor(ClassifierInputName, inputTensor)
            };

            using var results = _classifierSession.Run(inputs);
            var probabilities = results.First(r => r.Name == ClassifierOutputName)
                .AsEnumerable<float>()
                .ToArray();

            var positiveProb = probabilities[1];
            return (positiveProb > _threshold, positiveProb);
        }
        catch (Exception ex) {
            logger.LogError($"Ошибка детекции: {ex.Message}");
            return (false, 0f);
        }
    }

    /// <summary>
    /// Приводит аудио к фиксированной длине, которую ожидает бэкенд
    /// (дополняет нулями или обрезает). Аудио должно быть уже ресемплировано
    /// в частоту settings.SampleRate вызывающей стороной.
    /// </summary>
    private float[] PrepareWaveform(float[] audioSamples) {
        var prepared = new float[settings.Value.NumSamples];
        var count = Math.Min(audioSamples.Length, settings.Value.NumSamples);
        Array.Copy(audioSamples, prepared, count);
        return prepared;
    }

    /// <summary>
    /// melspectrogram.onnx ожидает диапазон значений 16-бит PCM (как float,
    /// не как тип int16) — то же самое, что делает Python-пайплайн при
    /// обучении. Несовпадение диапазона входа даёт мусорные признаки.
    /// Питон приводит через <c>.astype(np.int16)</c>, что усекает дробную
    /// часть (в сторону нуля), а не округляет — здесь то же самое через
    /// явный cast, иначе вход чуть разъезжается с тем, на чём училась модель.
    /// </summary>
    private static float[] ToPcm16Range(float[] waveform) {
        var pcm = new float[waveform.Length];
        for (int i = 0; i < waveform.Length; i++) {
            var clipped = Math.Clamp(waveform[i], -1f, 1f);
            pcm[i] = (short)(clipped * 32767f);
        }
        return pcm;
    }

    /// <summary>
    /// Возвращает mel-спектрограмму как (frames, melBins), уже с трансформом
    /// x/10+2, которого ожидает embedding-модель (задокументировано в
    /// openWakeWord). Размерности == 1 отбрасываются, а не хардкодятся —
    /// так надёжнее к возможным вариациям формы выхода melspectrogram.onnx.
    /// </summary>
    private float[,] ComputeMelspectrogram(float[] waveform) {
        var pcm = ToPcm16Range(waveform);
        var inputTensor = new DenseTensor<float>(pcm, [1, pcm.Length]);
        var inputName = _melSession.InputMetadata.Keys.First();

        using var results = _melSession.Run(new List<NamedOnnxValue> {
            NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
        });

        var tensor = results.First().AsTensor<float>();
        var dims = tensor.Dimensions.ToArray().Where(d => d != 1).ToArray();
        int frames = dims[0];
        int melBins = dims[1];

        var flat = tensor.ToArray();
        var mel = new float[frames, melBins];
        for (int t = 0; t < frames; t++)
            for (int m = 0; m < melBins; m++)
                mel[t, m] = flat[t * melBins + m] / 10f + 2f;

        return mel;
    }

    /// <summary>
    /// Скользящие окна (WindowSize, шаг StepSize) по mel-спектрограмме ->
    /// батч в embedding-модель -> конкатенация первых NumWindows эмбеддингов
    /// в один вектор фиксированной длины (то же самое, что при обучении).
    /// </summary>
    private float[] ComputeFeatures(float[,] mel) {
        int frames = mel.GetLength(0);
        int melBins = mel.GetLength(1);

        var windowStarts = new List<int>();
        for (int i = 0; i + WindowSize <= frames; i += StepSize)
            windowStarts.Add(i);

        var features = new float[NumWindows * EmbeddingDim];
        if (windowStarts.Count == 0) return features;

        var batch = new float[windowStarts.Count * WindowSize * melBins];
        for (int w = 0; w < windowStarts.Count; w++) {
            int start = windowStarts[w];
            for (int t = 0; t < WindowSize; t++)
                for (int m = 0; m < melBins; m++)
                    batch[(w * WindowSize + t) * melBins + m] = mel[start + t, m];
        }

        var inputTensor = new DenseTensor<float>(batch, [windowStarts.Count, WindowSize, melBins, 1]);
        var inputName = _embeddingSession.InputMetadata.Keys.First();

        using var results = _embeddingSession.Run(new List<NamedOnnxValue> {
            NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
        });
        var embFlat = results.First().AsEnumerable<float>().ToArray();

        int available = Math.Min(windowStarts.Count, NumWindows);
        for (int w = 0; w < available; w++)
            for (int d = 0; d < EmbeddingDim; d++)
                features[w * EmbeddingDim + d] = embFlat[w * EmbeddingDim + d];
        // Если окон меньше NumWindows, хвост остаётся нулями — как np.pad в Python.

        return features;
    }

    public void Dispose() {
        if (!_disposed) {
            _melSession?.Dispose();
            _embeddingSession?.Dispose();
            _classifierSession?.Dispose();
            _disposed = true;
        }
    }
}
