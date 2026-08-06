using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Yuki.Core.STT.Contracts;
using Yuki.Core.Wrappers.Utils;

namespace Yuki.Core.STT.Services;

public class VoiceActivityDetector : IVoiceActivityDetector {
    private const int SampleRate = 16000;
    private const int StateSize = 64;

    private const int WindowSize = 512;

    private readonly InferenceSession _session;
    private readonly List<float> _pending = new();

    private float[] _h = new float[2 * StateSize];
    private float[] _c = new float[2 * StateSize];
    private float _lastProbability;

    private bool _disposed;

    public VoiceActivityDetector() => _session = new InferenceSession(Utils.LoadWakeWord("silero_vad.onnx"));

    public void Reset() {
        Array.Clear(_h);
        Array.Clear(_c);
        _pending.Clear();
        _lastProbability = 0f;
    }

    public float GetSpeechProbability(float[] chunk) {
        _pending.AddRange(chunk);

        while (_pending.Count >= WindowSize) {
            var window = _pending.GetRange(0, WindowSize).ToArray();
            _pending.RemoveRange(0, WindowSize);
            _lastProbability = RunInference(window);
        }

        return _lastProbability;
    }

    private float RunInference(float[] window) {
        var inputTensor = new DenseTensor<float>(window, [1, window.Length]);
        var srTensor = new DenseTensor<long>(new long[] { SampleRate }, Array.Empty<int>());
        var hTensor = new DenseTensor<float>(_h, [2, 1, StateSize]);
        var cTensor = new DenseTensor<float>(_c, [2, 1, StateSize]);

        var inputs = new List<NamedOnnxValue> {
            NamedOnnxValue.CreateFromTensor("input", inputTensor),
            NamedOnnxValue.CreateFromTensor("sr", srTensor),
            NamedOnnxValue.CreateFromTensor("h", hTensor),
            NamedOnnxValue.CreateFromTensor("c", cTensor)
        };

        using var results = _session.Run(inputs);

        var probability = results.First(r => r.Name == "output").AsEnumerable<float>().First();
        _h = [.. results.First(r => r.Name == "hn").AsEnumerable<float>()];
        _c = [.. results.First(r => r.Name == "cn").AsEnumerable<float>()];

        return probability;
    }

    public void Dispose() {
        if (!_disposed) {
            _session.Dispose();
            _disposed = true;
        }
    }
}
