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
            _logger.LogInformation("System: Голосовое взаимодействие запущено");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Ошибка при запуске голосового взаимодействия");
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
            _logger.LogInformation("System: Голосовое взаимодействие остановлено");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Ошибка при остановке голосового взаимодействия");
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
