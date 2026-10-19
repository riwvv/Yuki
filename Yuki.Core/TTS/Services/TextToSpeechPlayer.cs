using NAudio.Wave;
using Yuki.Core.TTS.Contracts;
using Yuki.Core.TTS.Models;

namespace Yuki.Core.TTS.Services;

public class TextToSpeechPlayer : ITextToSpeechPlayer {
    private readonly ITextToSpeechCore _tts;
    private readonly Style _style;

    public TextToSpeechPlayer(ITextToSpeechCore tts, Style style) {
        _tts = tts;
        _style = style;
    }

    public void Speak(string text) {
        (float[] wav, float[] _) = _tts.Call(
            text: text,
            style: _style,
            totalStep: 7,
            speed: 1.1f
        );

        PlayAudio(wav);
    }

    private void PlayAudio(float[] samples) {
        byte[] buffer = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, buffer, 0, buffer.Length);

        var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_tts.SampleRate, 1);
        using var stream = new RawSourceWaveStream(new MemoryStream(buffer), waveFormat);
        using var output = new WaveOut();
        output.Init(stream);
        output.Play();

        while (output.PlaybackState == PlaybackState.Playing) {
            Thread.Sleep(50);
        }
    }

    public void Dispose() {
        _tts?.Dispose();
    }
}
