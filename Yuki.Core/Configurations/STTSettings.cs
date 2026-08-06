namespace Yuki.Core.Configurations;

public class STTSettings {
    public int SampleRate { get; set; } = 16000;
    public float VadThreshold { get; set; } = 0.5f;
    public double EndOfSpeectSilenceMs { get; set; } = 800;
    public VoskSettings Vosk { get; set; } = new();
}
