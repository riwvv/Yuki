using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Yuki.Core.STT.Contracts;

namespace Yuki.Core.STT.Services;

public sealed class VoiceInteractionHostedService(IVoiceInteractionService _voiceInteraction, IWakeWordListener _wakeWordListener, ILogger<VoiceInteractionHostedService> _logger) : IHostedService, IDisposable {
    private bool _started;

    public Task StartAsync(CancellationToken cancellationToken) {
        if (_started) return Task.CompletedTask;
        _started = true;

        try {
            _voiceInteraction.Start();
            _wakeWordListener.Start();
            _logger.LogInformation("Голос: детектор ключевого слова и микрофон запущены");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Голос: ошибка запуска");
            _started = false;
            throw;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        if (!_started) return Task.CompletedTask;
        _started = false;

        try {
            _voiceInteraction.Stop();
            _wakeWordListener.Stop();
            _logger.LogInformation("Голос: ключевое слово и микрофон остановлены");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Голос: ошибка остановки");
            _started = false;
            throw;
        }

        return Task.CompletedTask;
    }

    public void Dispose() {
        _voiceInteraction?.Dispose();
        _wakeWordListener?.Dispose();
    }
}
