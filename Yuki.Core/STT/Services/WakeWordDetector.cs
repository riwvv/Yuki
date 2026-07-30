using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Yuki.Core.Configurations;
using Yuki.Core.STT.Contracts;
using Yuki.Core.Wrappers.Utils;

namespace Yuki.Core.STT.Services;

public class WakeWordDetector(IOptions<WakeWordSettings> settings) : IWakeWordDetector, IDisposable {
    private readonly InferenceSession _session = new(Utils.LoadWakeWord("wakeword.onnx"));
    private readonly float _threshold = settings.Value.Threshold;

    private bool _disposed = false;

    private const string INPUT_NAME = "waveform";
    private const string OUTPUT_NAME = "probabilities";

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
            var inputTensor = new DenseTensor<float>(waveform, [1, waveform.Length]);

            var inputs = new List<NamedOnnxValue> {
                NamedOnnxValue.CreateFromTensor(INPUT_NAME, inputTensor)
            };

            using var results = _session.Run(inputs);
            var probabilities = results.First(r => r.Name == OUTPUT_NAME)
                .AsEnumerable<float>()
                .ToArray();

            var positiveProb = probabilities[1];
            return (positiveProb > _threshold, positiveProb);
        }
        catch (Exception ex) {
            Console.WriteLine($"Ошибка детекции: {ex.Message}");
            return (false, 0f);
        }
    }

    /// <summary>
    /// Приводит аудио к фиксированной длине, которую ожидает модель
    /// (дополняет нулями или обрезает). Аудио должно быть уже ресемплировано
    /// в частоту config.sample_rate вызывающей стороной.
    /// </summary>
    private float[] PrepareWaveform(float[] audioSamples) {
        var prepared = new float[settings.Value.NumSamples];
        var count = Math.Min(audioSamples.Length, settings.Value.NumSamples);
        Array.Copy(audioSamples, prepared, count);
        return prepared;
    }

    public void Dispose() {
        if (!_disposed) {
            _session?.Dispose();
            _disposed = true;
        }
    }
}