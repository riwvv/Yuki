using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Windows;
using Serilog;
using Yuki.Core.Extensions;
using Yuki.Extensions;

namespace Yuki;

public partial class App : Application {
    private readonly IHost? _host;
    private readonly ILogger<App>? _logger;
    private static readonly Mutex _mutex = new(true, "YukiAppMutex");
    private bool _mutexAcquired = false;

    public App() {
        try {
            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddSerilog(config => config
                .ReadFrom.Configuration(builder.Configuration)
                .WriteTo.Debug()
                .WriteTo.File("logs/yuki-.log", rollingInterval: RollingInterval.Day));

            builder.Services.AddYuki();
            builder.Services.AddYukiCore(builder.Configuration);

            _host = builder.Build();
            _logger = _host.Services.GetRequiredService<ILogger<App>>();
        }
        catch (Exception ex) {
            _host = null;
            _logger = null;
            Log.Error(ex, "Ошибка при инициализации приложения");
        }
    }

    protected override async void OnStartup(StartupEventArgs e) {
        try {
            ArgumentNullException.ThrowIfNull(_host);
            ArgumentNullException.ThrowIfNull(_logger);

            AppBootstrap.HostValidation(_mutex);
            _mutexAcquired = true;

            var mainWindowViewModel = AppBootstrap.InitializationAndStartupMainWindow(_host);

            await AppBootstrap.VoskInitialization(mainWindowViewModel, _host);

            var ttsPlayer =  await AppBootstrap.TtsInitialization(mainWindowViewModel, _host, _logger);
            var voiceInteraction = await AppBootstrap.SttInitialization(mainWindowViewModel, _host, _logger);
            var hostAgent = AppBootstrap.HostAgentInitialization(_host);

            AppBootstrap.ImplementationVoiceCycle(voiceInteraction, ttsPlayer, hostAgent, _logger);

            await AppBootstrap.HostStart(mainWindowViewModel, _host);
            base.OnStartup(e);
        }
        catch (Exception ex) {
            Log.Error(ex, "Ошибка при инициализации приложения");
            Shutdown();
        }
    }

    protected override async void OnExit(ExitEventArgs e) {
        if (_host != null) {
            await _host.StopAsync();
            _host.Dispose();
        }

        if (_mutexAcquired) {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
