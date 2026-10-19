using Yuki.Core.TTS.Models;

namespace Yuki.Core.TTS.Contracts;

public interface ITextToSpeechCore : IDisposable {
    int SampleRate { get; }
    (float[] wav, float[] duration) Call(string text, Style style, int totalStep = 6, float speed = 1.05f);
}
