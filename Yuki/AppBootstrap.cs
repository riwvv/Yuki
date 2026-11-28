using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Windows;
using Yuki.Core.HostAgent.Contracts;
using Yuki.Core.STT.Contracts;
using Yuki.Core.STT.Services;
using Yuki.Core.TTS.Contracts;
using Yuki.Core.TTS.Utils;
using Yuki.ViewModels;
using Yuki.Views;

namespace Yuki;

public static class AppBootstrap {
    public static void HostValidation(Mutex mutex) {
        if (!mutex.WaitOne(TimeSpan.Zero, true)) {
            MessageBox.Show("Приложение уже запущено.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            throw new AbandonedMutexException("Приложение уже запущено");
        }
    }

    public static MainViewModel InitializationAndStartupMainWindow(IHost host) {
        var mainWindow = host.Services.GetRequiredService<MainWindow>();
        var mainWindowViewModel = host.Services.GetRequiredService<MainViewModel>();
        mainWindow.DataContext = mainWindowViewModel;
        mainWindow.Show();

        return mainWindowViewModel;
    }

    public static async Task VoskInitialization(MainViewModel mainWindowViewModel, IHost host) {
        mainWindowViewModel.State = "Инициализация...";
        var modelPath = await host.Services.GetRequiredService<VoskModelProvisioner>().EnsureModelAsync();
        host.Services.GetRequiredService<VoskModelPathProvider>().ModelPath = modelPath;
    }

    public static async Task<ITextToSpeechPlayer> TtsInitialization(MainViewModel mainWindowViewModel, IHost host, ILogger logger) {
        mainWindowViewModel.State = "Загрузка TTS-движка...";
        ITextToSpeechPlayer? ttsPlayer = null;
        try {
            await Task.Run(() => ttsPlayer = host.Services.GetRequiredService<ITextToSpeechPlayer>());
            logger.LogInformation("TTS: движок инициализирован");
            return ttsPlayer ?? throw new NullReferenceException($"{nameof(ttsPlayer)} is null");
        }
        catch (Exception ex) {
            throw new Exception("Не удалось инициализировать TTS-движок", ex);
        }
    }

    public static async Task<IVoiceInteractionService> SttInitialization(MainViewModel mainWindowViewModel, IHost host, ILogger logger) {
        mainWindowViewModel.State = "Загрузка STT-движка...";
        var voiceInteraction = await Task.Run(() => host.Services.GetRequiredService<IVoiceInteractionService>());
        voiceInteraction.StateChanged += state => {
            logger.LogInformation("Голос: состояние {State}", state);
            Application.Current.Dispatcher.Invoke(() => {
                mainWindowViewModel.State = state switch {
                    VoiceInteractionState.Idle => "Готова",
                    VoiceInteractionState.Listening => "Слушаю...",
                    VoiceInteractionState.Processing => "Обработка...",
                    _ => "Неизвестно"
                };
            });
        };
        return voiceInteraction;
    }

    public static IHostAgentService HostAgentInitialization(IHost host) => host.Services.GetRequiredService<IHostAgentService>();

    public static void ImplementationVoiceCycle(IVoiceInteractionService voiceInteraction, ITextToSpeechPlayer ttsPlayer, IHostAgentService hostAgent, ILogger logger) {
        voiceInteraction.RequestReady = async requestText => {
            logger.LogInformation("Голос: распознано «{Text}»", requestText);
            try {
                if (ttsPlayer == null)
                    throw new Exception($"{nameof(ttsPlayer)} is null");

                // TTS сам режет текст на предложения и делает паузы между ними,
                // поэтому отдаём весь ответ одной фразой. Поточная подача по
                // токенам давала дробную, роботизированную озвучку и огромную
                // очередь: каждое слово синтезировалось отдельно.
                var response = await ResponseTextAggregator.AggregateAsync(
                    hostAgent.RespondToUserAsync(requestText));

                ttsPlayer.EnqueueText(response);
                logger.LogInformation("Юки: {Response}", response);
            }
            catch (Exception ex) {
                logger.LogError(ex, "Юки: не удалось получить ответ от Host");
            }
            finally {
                voiceInteraction.EndBusy();
            }
        };

        hostAgent.ResponseGenerated += responseText => {
            logger.LogInformation("Юки (фоновый ответ): {Response}", responseText);
            try {
                // Ответ по отчёту Servant'а приходит уже после EndBusy —
                // помечаем его, чтобы окно повторного обращения ждало озвучку.
                ttsPlayer?.EnqueueReport(responseText);
            }
            catch (Exception ex) {
                logger.LogError(ex, "Юки: не удалось передать фоновый ответ в TTS");
            }
        };
    }

    public static async Task HostStart(MainViewModel mainWindowViewModel, IHost host) {
        await host.StartAsync();
        mainWindowViewModel.State = "Готова";
    }
}
