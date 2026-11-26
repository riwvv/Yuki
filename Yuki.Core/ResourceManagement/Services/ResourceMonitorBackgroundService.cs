using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yuki.Core.ResourceManagement.Contracts;
using Yuki.Core.Configurations;
using Yuki.Core.HostAgent.Contracts;
using Yuki.Core.STT.Contracts;
using Yuki.Core.ServantAgent.Contracts;

namespace Yuki.Core.ResourceManagement.Services;

public class ResourceMonitorBackgroundService(IVramProvider _vramProvider, IGpuLayerResolver _gpuLayerResolver, IHostAgentService _hostAgent, IServantAgentService _servant, IVoiceInteractionService _voice, ISafeToReloadGate _gate, IOptions<ResourceManagerSettings> _settings, ILogger<ResourceMonitorBackgroundService> _logger) : BackgroundService {
    private int _deficitStreak, _recoveryStreak;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_settings.Value.PollIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken)) {
            try { await CheckAndReactAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Ресурсы: ошибка в цикле мониторинга"); }
        }
    }

    private async Task CheckAndReactAsync(CancellationToken ct) {
        var freeVramMb = _vramProvider.GetFreeVramMb();
        var currentLayers = _hostAgent.CurrentGpuLayerCount;
        var newLayers = _gpuLayerResolver.RecomputeGpuLayers(freeVramMb, currentLayers);
        _logger.LogDebug("Ресурсы: свободно {FreeMb} МБ VRAM, слоёв сейчас {Layers}, расчёт {TargetLayers}", freeVramMb, currentLayers, newLayers);

        var delta = Math.Abs(newLayers - currentLayers);
        var deficitSignal = newLayers < currentLayers && delta >= _settings.Value.MinLayerDeltaForReload;
        var recoverySignal = newLayers > currentLayers && delta >= _settings.Value.MinLayerDeltaForReload;

        _deficitStreak = deficitSignal ? _deficitStreak + 1 : 0;
        _recoveryStreak = recoverySignal ? _recoveryStreak + 1 : 0;

        var threshold = _settings.Value.ConfirmationThreshold;
        if (_deficitStreak < threshold && _recoveryStreak < threshold)
            return;

        if (!_gate.IsSafeToReload())
            return;

        if (!_voice.TryBeginBusy())
            return;

        try {
            if (!await _servant.TryAcquireReloadLockAsync(TimeSpan.FromSeconds(2), ct)) {
                _logger.LogInformation("Ресурсы: Servant занят, откладываю смену слоёв до следующей проверки");
                return;
            }

            var direction = newLayers < currentLayers
                ? "нагрузка на видеокарту выросла, и тебе приходится немного потесниться, уступив часть мощности"
                : "нагрузка на видеокарту наконец спала, и можно вернуть себе больше мощности";

            try {
                _logger.LogInformation("Ресурсы: смена нагрузки — {From} => {To} слоёв GPU", currentLayers, newLayers);

                try {
                    await _hostAgent.ReloadEngineAsync(newLayers, ct);
                }
                catch (Exception ex) {
                    _logger.LogError(ex, "Ресурсы: reload не удался");
                    return;
                }
            }
            finally {
                _servant.ReleaseReloadLock();
            }

            await _hostAgent.SaySystemEventAsync($"[Системное событие] Ты заметила, что {direction}. Скажи об этом пользователю в своём стиле.", ct);
            _deficitStreak = _recoveryStreak = 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            _logger.LogDebug("Ресурсы: смена слоёв отменена при остановке приложения");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Ресурсы: ошибка при смене слоёв");
        }
        finally {
            _voice.EndBusy();
        }
    }
}
