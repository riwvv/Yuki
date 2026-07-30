namespace Yuki.Core.Configurations;

public class WakeWordSettings {
    public int SampleRate { get; set; } = 16000;
    public int NumSamples { get; set; } = 16000;
    public float Threshold { get; set; } = 0.8f;
}
