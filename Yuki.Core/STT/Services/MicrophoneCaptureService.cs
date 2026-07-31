using NAudio.Wave;
using Yuki.Core.STT.Contracts;

namespace Yuki.Core.STT.Services;

public class MicrophoneCaptureService : IAudioCaptureService {
    private readonly WaveInEvent _waveIn;
    public event Action<float[]>? AudioAvailable;

    public MicrophoneCaptureService() {
        _waveIn = new WaveInEvent {
            WaveFormat = new WaveFormat(16000, 16, 1)
        };

        _waveIn.DataAvailable += OnDataAvailable;
    }

    public void Start() => _waveIn.StartRecording();

    public void Stop() => _waveIn.StopRecording();

    private void OnDataAvailable(object? sender, WaveInEventArgs e) {
        var samples = new float[e.BytesRecorded / 2];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;

        AudioAvailable?.Invoke(samples);
    }

    public void Dispose() => _waveIn.Dispose();
}
