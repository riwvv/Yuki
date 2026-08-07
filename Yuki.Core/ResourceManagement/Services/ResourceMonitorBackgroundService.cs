using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yuki.Core.Configurations;
using Yuki.Core.HostAgent.Contracts;
using Yuki.Core.ResourceManagement.Contracts;
using Yuki.Core.STT.Contracts;

namespace Yuki.Core.ResourceManagement.Services;

public class ResourceMonitorBackgroundService(IVramProvider _vramProvider, IGpuLayerResolver _gpuLayerResolver, IHostAgentService _hostAgent, IVoiceInteractionService _voice, ISafeToReloadGate _gate, IOptions<ResourceManagerSettings> _settings, ILogger<ResourceMonitorBackgroundService> _logger) : BackgroundService {
    private int _deficitStreak, _recoveryStreak;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_settings.Value.PollIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken)) {
            try { await CheckAndReactAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "Ошибка в цикле мониторинга ресурсов"); }
        }
    }

    private async Task CheckAndReactAsync() {
        var freeVramMb = _vramProvider.GetFreeVramMb();
        var currentLayers = _hostAgent.CurrentGpuLayerCount;
        var newLayers = _gpuLayerResolver.RecomputeGpuLayers(freeVramMb, currentLayers);

        var deficitSignal = newLayers < currentLayers;
        var recoverySignal = newLayers > currentLayers;

        _deficitStreak = deficitSignal ? _deficitStreak + 1 : 0;
        _recoveryStreak = recoverySignal ? _recoveryStreak + 1 : 0;

        var threshold = _settings.Value.ConfirmationThreshold;
        if ((_deficitStreak >= threshold || _recoveryStreak >= threshold) && _gate.IsSafeToReload()) {
            if (_voice.TryBeginBusy()) {
                try {
                    var direction = newLayers < currentLayers
                        ? "нагрузка на видеокарту выросла, и тебе приходится немного потесниться, уступив часть мощности"
                        : "нагрузка на видеокарту наконец спала, и можно вернуть себе больше мощности";

                    _logger.LogInformation($"Resource manager: подтверждена смена нагрузки, {currentLayers} => {newLayers} слоёв");
                    await _voice.SayAsync($"[Системное событие] Ты заметила, что {direction}. Скажи об этом пользователю в своём стиле.");
                    _hostAgent.ReloadEngine(newLayers);
                    _deficitStreak = _recoveryStreak = 0;
                }
                finally { _voice.EndBusy(); }
            }
        }
    }
}
