using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Text;
using System.Windows;
using Yuki.Core.Extensions;
using Yuki.Core.HostAgent.Contracts;
using Yuki.Core.STT.Contracts;
using Yuki.Core.STT.Services;
using Yuki.Core.TTS.Contracts;
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

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            var mainWindowViewModel = _host.Services.GetRequiredService<MainViewModel>();
            mainWindow.DataContext = mainWindowViewModel;
            mainWindow.Show();

            // Первый слой - инициализация Vosk
            mainWindowViewModel.State = "Инициализация...";
            var modelPath = await _host.Services.GetRequiredService<VoskModelProvisioner>().EnsureModelAsync();
            _host.Services.GetRequiredService<VoskModelPathProvider>().ModelPath = modelPath;

            // Второй слой - инициализация TTS
            mainWindowViewModel.State = "Загрузка TTS-движка...";
            ITextToSpeechPlayer? ttsPlayer = null;
            try
            {
                await Task.Run(() => ttsPlayer = _host.Services.GetRequiredService<ITextToSpeechPlayer>());
                _logger.LogInformation("System: TTS-движок успешно инициализирован");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось инициализировать TTS-движок");
            }

            // Третий слой - инициализация STT
            mainWindowViewModel.State = "Загрузка STT-движка...";
            var voiceInteraction = await Task.Run(() => _host.Services.GetRequiredService<IVoiceInteractionService>());
            voiceInteraction.StateChanged += state =>
            {
                _logger.LogInformation($"Voice state: {state}");
                Current.Dispatcher.Invoke(() =>
                {
                    mainWindowViewModel.State = state switch
                    {
                        VoiceInteractionState.Idle => "Готова",
                        VoiceInteractionState.Listening => "Слушаю...",
                        VoiceInteractionState.Processing => "Обработка...",
                        _ => "Неизвестно"
                    };
                });
            };

            // Четвёртый слой - инициализация HostAgent
            var hostAgent = _host.Services.GetRequiredService<IHostAgentService>();

            // Пятый слой - реализация голосового цикла
            voiceInteraction.RequestReady += async requestText =>
            {
                _logger.LogInformation($"Распознано: {requestText}");
                try
                {
                    if (ttsPlayer == null)
                        throw new Exception($"{nameof(ttsPlayer)} is null");
                    var fullResponse = new StringBuilder();
                    await foreach (var messageBlock in hostAgent.RespondAsync(requestText))
                    {
                        fullResponse.Append(messageBlock);
                        ttsPlayer.EnqueueText(messageBlock);
                        _logger.LogInformation($"Yuki: {fullResponse}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при генерации ответа Host'ом");
                }
                finally
                {
                    voiceInteraction.EndBusy();
                }
            };

            // Последний слой - старт хоста
            await _host.StartAsync();
            mainWindowViewModel.State = "Готова";
            base.OnStartup(e);

            //var voiceInteraction = await Task.Run(() => _host.Services.GetRequiredService<IVoiceInteractionService>());
            //voiceInteraction.StateChanged += state => {
            //    _logger.LogInformation($"Voice state: {state}");
            //    Current.Dispatcher.Invoke(() => {
            //        mainWindowViewModel.State = state switch {
            //            VoiceInteractionState.Idle => "Готова",
            //            VoiceInteractionState.Listening => "Слушаю...",
            //            VoiceInteractionState.Processing => "Обработка...",
            //            _ => "Неизвестно"
            //        };
            //    });
            //};

            //mainWindowViewModel.State = "Загрузка TTS-движка...";
            //try {
            //    await Task.Run(() => _host.Services.GetRequiredService<ITextToSpeechPlayer>());
            //    _logger.LogInformation("TTS-движок успешно инициализирован.");
            //}
            //catch (Exception ex) {
            //    _logger.LogError(ex, "Не удалось инициализировать TTS-движок.");
            //}
            //var ttsPlayer = await Task.Run(() => _host.Services.GetRequiredService<ITextToSpeechPlayer>());
            //voiceInteraction.ResponseReady += response => {
            //    _logger.LogInformation($"Yuki: {response}");
            //    Task.Run(() => {
            //        try {
            //            ttsPlayer.Speak(response);
            //        }
            //        catch (Exception ex) {
            //            _logger.LogError(ex, "Ошибка при озвучке ответа TTS");
            //            throw;
            //        }
            //    });
            //};
            //await _host.StartAsync();
            //mainWindowViewModel.State = "Готова";
            //base.OnStartup(e);
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

        _mutex.ReleaseMutex();
        _mutex.Dispose();

        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
