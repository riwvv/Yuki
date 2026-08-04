using System.Text.Json;
using Vosk;
using Yuki.Core.STT.Contracts;

namespace Yuki.Core.STT.Services;

public class SpeechRecognizer : ISpeechRecognizer {
    private const float SampleRate = 16000f;

    private readonly Model _model;
    private readonly VoskRecognizer _recognizer;
    private bool _disposed;

    public SpeechRecognizer(string modelPath) {
        Vosk.Vosk.SetLogLevel(-1);
        _model = new Model(modelPath);
        _recognizer = new VoskRecognizer(_model, SampleRate);
    }

    public void Reset() => _recognizer.Reset();

    public void AcceptAudio(float[] chunk) {
        var scaled = new float[chunk.Length];
        for (int i = 0; i < chunk.Length; i++)
            scaled[i] = Math.Clamp(chunk[i], -1f, 1f) * short.MaxValue;

        _recognizer.AcceptWaveform(scaled, scaled.Length);
    }

    public string GetFinalResult() {
        var json = _recognizer.FinalResult();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("text", out var text) ? text.GetString() ?? "" : "";
    }

    public void Dispose() {
        if (!_disposed) {
            _recognizer?.Dispose();
            _model?.Dispose();
            _disposed = true;
        }
    }
}
