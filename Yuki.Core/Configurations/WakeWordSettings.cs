namespace Yuki.Core.Configurations;

public class WakeWordSettings {
    public int SampleRate { get; set; } = 16000;
    public int NumSamples { get; set; } = 16000;
    public float Threshold { get; set; } = 0.8f;
    public int CheckIntervalMs { get; set; } = 300;
    public double CooldownSeconds { get; set; } = 1.5;

    // Сколько проверок подряд должны перейти порог, прежде чем считать
    // слово реально произнесённым. Одна проверка — это одно окно из
    // множества перекрывающихся (шаг 300мс при окне 1с), и единичный
    // всплеск вероятности на нём — устранимая причина ложных срабатываний.
    public int RequiredConsecutiveDetections { get; set; } = 2;
}
