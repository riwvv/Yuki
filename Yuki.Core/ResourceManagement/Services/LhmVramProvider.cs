using Microsoft.Extensions.Logging;
using LibreHardwareMonitor.Hardware;
using Yuki.Core.ResourceManagement.Contracts;

namespace Yuki.Core.ResourceManagement.Services;

public class LhmVramProvider : IVramProvider, IGpuLoadProvider, IDisposable {
    private readonly ILogger<LhmVramProvider> _logger;
    private readonly IHardware _hw;
    private Computer? _computer;
    private bool _disposed;

    public LhmVramProvider(ILogger<LhmVramProvider> logger) {
        _logger = logger;
        _computer = new Computer() { IsGpuEnabled = true };
        _computer.Open();

        var hardware = _computer.Hardware.FirstOrDefault(x => x.HardwareType == HardwareType.GpuNvidia) ?? throw new InvalidOperationException("NVIDIA GPU не найден");
        _hw = hardware;
    }

    public long GetFreeVramMb() {
        _hw.Update();

        var sensor = _hw.Sensors.FirstOrDefault(x => x.SensorType == SensorType.SmallData && x.Name == "GPU Memory Free");
        _logger.LogInformation($"Sensor: {sensor?.Name} - {sensor?.Value}");

        return sensor == null || !sensor.Value.HasValue ? throw new InvalidOperationException("Сенсор 'GPU Memory Free' не найден") : (long)sensor.Value;
    }

    public float GetGpuCoreLoadPercent() {
        _hw.Update();

        var sensor = _hw.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Load && x.Name == "GPU Core");
        _logger.LogInformation($"Sensor: {sensor?.Name} - {sensor?.Value}");

        return sensor == null || !sensor.Value.HasValue ? throw new InvalidOperationException("Сенсор 'GPU Core' не найден") : (float)sensor.Value;
    }

    public void Dispose() {
        if (!_disposed) {
            _computer?.Close();
            _computer = null;
            _disposed = true;
        }
    }
}
