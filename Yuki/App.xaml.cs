using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text;
using System.Windows;
using Serilog;
using Serilog.Events;
using Yuki.Core.Extensions;
using Yuki.Extensions;

namespace Yuki;

public partial class App : Application {
    private const string LogTemplate = "{Timestamp:HH:mm:ss.fff} {Level:u3} [{SourceContext}] {Message:lj}{NewLine}{Exception}";

    private readonly IHost? _host;
    private readonly ILogger<App>? _logger;
    private static readonly Mutex _mutex = new(true, "YukiAppMutex");
    private bool _mutexAcquired = false;

    public App() {
        // Ранний логгер: ловит ошибки до того, как Host и DI будут собраны.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug(outputTemplate: LogTemplate)
            .CreateLogger();

        try {
            // WPF-приложение обычно запускается без консоли — не падаем, если её нет.
            try { Console.OutputEncoding = new UTF8Encoding(false); }
            catch (IOException) { }

            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddSerilog(config => config
                .ReadFrom.Configuration(builder.Configuration)
                .Enrich.FromLogContext()
                .WriteTo.Debug(outputTemplate: LogTemplate)
                .WriteTo.Console(outputTemplate: LogTemplate, standardErrorFromLevel: LogEventLevel.Error)
                .WriteTo.File(
                    Path.Combine(AppContext.BaseDirectory, "logs", "yuki-.log"),
                    outputTemplate: LogTemplate,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    encoding: new UTF8Encoding(false)));

            builder.Services.AddYuki();
            builder.Services.AddYukiCore(builder.Configuration);

            _host = builder.Build();
            _logger = _host.Services.GetRequiredService<ILogger<App>>();
        }
        catch (Exception ex) {
            _host = null;
            _logger = null;
            Log.Fatal(ex, "Ошибка при инициализации приложения");
        }
    }

    protected override async void OnStartup(StartupEventArgs e) {
        try {
            ArgumentNullException.ThrowIfNull(_host);
            ArgumentNullException.ThrowIfNull(_logger);

            _logger.LogInformation("=== Запуск Юки ===");

            AppBootstrap.HostValidation(_mutex);
            _mutexAcquired = true;
            _logger.LogInformation("Запуск: проверка одного экземпляра пройдена");

            var mainWindowViewModel = AppBootstrap.InitializationAndStartupMainWindow(_host);
            _logger.LogInformation("Запуск: главное окно открыто");

            await AppBootstrap.VoskInitialization(mainWindowViewModel, _host);
            _logger.LogInformation("Запуск: модель Vosk готова");

            var ttsPlayer = await AppBootstrap.TtsInitialization(mainWindowViewModel, _host, _logger);
            var voiceInteraction = await AppBootstrap.SttInitialization(mainWindowViewModel, _host, _logger);
            var hostAgent = AppBootstrap.HostAgentInitialization(_host);
            _logger.LogInformation("Запуск: TTS, STT и LLM инициализированы");

            AppBootstrap.ImplementationVoiceCycle(voiceInteraction, ttsPlayer, hostAgent, _logger);

            await AppBootstrap.HostStart(mainWindowViewModel, _host);
            _logger.LogInformation("Запуск: сервисы запущены. Юки готова слушать");

            base.OnStartup(e);
        }
        catch (Exception ex) {
            _logger?.LogCritical(ex, "Старт прерван — приложение закрывается");
            Log.Fatal(ex, "Старт прерван — приложение закрывается");
            Shutdown();
        }
    }

    protected override async void OnExit(ExitEventArgs e) {
        _logger?.LogInformation("=== Остановка Юки ===");

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
