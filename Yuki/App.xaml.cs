using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Windows;
using Yuki.Core.Extensions;
using Yuki.Core.STT.Contracts;
using Yuki.Core.STT.Services;
using Yuki.Extensions;
using Yuki.ViewModels;
using Yuki.Views;

namespace Yuki;

public partial class App : Application {
    private readonly IHost? _host;
    private readonly ILogger<App>? _logger;
    private static readonly Mutex _mutex = new(true, "YukiAppMutex");

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
        if (!_mutex.WaitOne(TimeSpan.Zero, true)) {
            MessageBox.Show("Приложение уже запущено.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        try {
            if (_host == null || _logger == null)
                throw new InvalidOperationException("Хост или логгер приложения не инициализирован");

            await _host.StartAsync();

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            var mainWindowViewModel = _host.Services.GetRequiredService<MainViewModel>();
            mainWindow.DataContext = mainWindowViewModel;
            mainWindow.Show();

            mainWindowViewModel.State = "Инициализация...";
            var modelPath = await _host.Services.GetRequiredService<VoskModelProvisioner>().EnsureModelAsync();
            _host.Services.GetRequiredService<VoskModelPathProvider>().ModelPath = modelPath;

            mainWindowViewModel.State = "Загрузка...";
            await Test(mainWindowViewModel);

            base.OnStartup(e);
        }
        catch (Exception ex) {
            Log.Error(ex, "Ошибка при инициализации приложения");
            Shutdown();
        }
    }

    private async Task Test(MainViewModel viewModel) {
        if (_host == null || _logger == null) return;

        var voiceInteraction = await Task.Run(() => _host.Services.GetRequiredService<IVoiceInteractionService>());

        voiceInteraction.StateChanged += state => {
            _logger.LogInformation($"Voice state: {state}");
            Current.Dispatcher.Invoke(() => {
                viewModel.State = state switch {
                    VoiceInteractionState.Idle => "Готова",
                    VoiceInteractionState.Listening => "Слушаю...",
                    VoiceInteractionState.Processing => "Обработка...",
                    _ => "Неизвестно"
                };
            });
        };
        voiceInteraction.ResponseReady += response => _logger.LogInformation($"Yuki: {response}");
        voiceInteraction.Start();

        var wakeWordListener = _host.Services.GetRequiredService<IWakeWordListener>();
        wakeWordListener.Start();

        viewModel.State = "Готова";
    }

    protected override async void OnExit(ExitEventArgs e) {
        if (_host != null) {
            await _host.StopAsync();
            _host.Dispose();
        }

        _mutex.ReleaseMutex();
        _mutex.Dispose();

        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
