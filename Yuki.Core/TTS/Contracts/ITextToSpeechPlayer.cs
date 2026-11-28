namespace Yuki.Core.TTS.Contracts;

public interface ITextToSpeechPlayer : IDisposable {
    /// <summary>Обычная реплика Юки положена в очередь — озвучка ещё не началась.</summary>
    event Action? SpeechQueued;

    /// <summary>Ответ по отчёту Servant'а/системному событию положен в очередь.</summary>
    event Action? ReportQueued;

    /// <summary>Очередь полностью опустела после реального проигрывания.</summary>
    event Action? SpeechCompleted;

    void EnqueueText(string text);
    void EnqueueReport(string text);
    void Stop();
    bool IsSpeaking { get; }
}
