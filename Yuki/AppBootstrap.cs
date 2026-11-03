using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Windows;
using Yuki.Core.HostAgent.Contracts;
using Yuki.Core.ServantAgent.Contracts;
using Yuki.Core.STT.Contracts;
using Yuki.Core.STT.Services;
using Yuki.Core.TTS.Contracts;
using Yuki.ViewModels;
using Yuki.Views;
using static LLama.Common.ChatHistory;

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
            logger.LogInformation("System: TTS-движок успешно инициализирован");
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
            logger.LogInformation($"Voice state: {state}");
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
            logger.LogInformation($"Распознано: {requestText}");
            try {
                if (ttsPlayer == null)
                    throw new Exception($"{nameof(ttsPlayer)} is null");
                var fullResponse = new StringBuilder();
                await foreach (var messageBlock in hostAgent.RespondToUserAsync(requestText)) {
                    fullResponse.Append(messageBlock);
                    ttsPlayer.EnqueueText(messageBlock);
                    logger.LogInformation($"Yuki: {fullResponse}");
                }
            }
            catch (Exception ex) {
                logger.LogError(ex, "Ошибка при генерации ответа Host'ом");
            }
            finally {
                voiceInteraction.EndBusy();
            }
        };

        hostAgent.ResponseGenerated += responseText => {
            logger.LogInformation($"Фоновый ответ от Host: {responseText}");
            try {
                ttsPlayer?.EnqueueText(responseText);
            }
            catch (Exception ex) {
                logger.LogError(ex, "Ошибка при отправке фонового ответа в TTS");
            }
        };
    }

    public static async Task HostStart(MainViewModel mainWindowViewModel, IHost host) {
        await host.StartAsync();
        mainWindowViewModel.State = "Готова";
    }

    // TODO УДАЛИТЬ! ПОДКЛЮЧЕНИЕ К ЧАТУ
    public static async Task SendToChat(IHostAgentService hostAgent, HubConnection connection, string message, ILogger logger) {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<string> handler = text => tcs.TrySetResult(text);
        hostAgent.ResponseGenerated += handler;

        try {
            var fullResponse = new StringBuilder();
            await foreach (var messageBlock in hostAgent.RespondToUserAsync(message))
                fullResponse.Append(messageBlock);

            var first = fullResponse.ToString();
            logger.LogInformation($"Обработано: {first}");
            await connection.InvokeAsync("SendMessage", "Yuki", first);

            if (hostAgent.IsServantTaskActive) {
                logger.LogInformation("SendToChat: Servant выполняется — жду фоновый ответ");
                try {
                    var bg = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(60));
                    logger.LogInformation($"Фоновый ответ: {bg}");
                    await connection.InvokeAsync("SendMessage", "Yuki", bg);
                }
                catch (TimeoutException) {
                    logger.LogWarning("SendToChat: Servant не ответил за 60 секунд");
                }
            }

            logger.LogInformation("Отправлено");
        }
        catch (Exception ex) {
            logger.LogError(ex, "Ошибка отправки сообщения");
        }
        finally {
            hostAgent.ResponseGenerated -= handler;
        }
    }
}
