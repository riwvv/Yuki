namespace Yuki.Core.Configurations;

public class STTSettings {
    public int SampleRate { get; set; } = 16000;
    public float VadThreshold { get; set; } = 0.5f;
    public double EndOfSpeectSilenceMs { get; set; } = 800;
    public int StartSilenceTimeoutMs { get; set; } = 5000;

    // Окно повторного обращения: открывается только после того, как Юки
    // договорила (TTS пуст и Servant свободен), и закрывается по тишине.
    public int FollowUpWindowMs { get; set; } = 7000;
    public int FollowUpStartDelayMs { get; set; } = 300;
    public int FollowUpQuietMs { get; set; } = 500;

    public VoskSettings Vosk { get; set; } = new();
}
