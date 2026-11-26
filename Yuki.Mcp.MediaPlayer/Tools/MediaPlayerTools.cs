using ModelContextProtocol.Server;
using System.ComponentModel;
using Windows.Media.Control;
using NAudio.CoreAudioApi;

namespace Yuki.Mcp.MediaPlayer.Tools;

/// <summary>
/// MCP-инструменты управления воспроизведением и системной громкостью.
/// Все вызовы ограничены по времени, COM-объекты освобождаются,
/// громкость и mute требуют явного подтверждения.
/// </summary>
internal class MediaPlayerTools {
    [McpServerTool]
    [Description("Получает информацию о текущем треке: исполнитель и название")]
    public static async Task<string> GetCurrentTrackInfo() {
        var sessionResult = await GetSessionAsync();
        if (!sessionResult.Success)
            return Error("player_error", sessionResult.Error!);

        var session = sessionResult.Value;
        if (session is null)
            return Error("no_player", "Ни один плеер сейчас не воспроизводит музыку");

        var propertiesResult = await MediaPolicy.GuardAsync(async () => await session.TryGetMediaPropertiesAsync());
        if (!propertiesResult.Success)
            return Error("player_error", propertiesResult.Error!);

        var mediaProperties = propertiesResult.Value;
        if (mediaProperties is null)
            return Error("no_media_info", "Не удалось получить информацию о треке");

        var title = string.IsNullOrWhiteSpace(mediaProperties.Title) ? "Неизвестно" : mediaProperties.Title;
        var artist = string.IsNullOrWhiteSpace(mediaProperties.Artist) ? "Неизвестный исполнитель" : mediaProperties.Artist;
        var fullTrack = $"{artist} - {title}";

        return Ok(fullTrack, new {
            titleMusic = title,
            artistMusic = artist,
            fullTrack
        });
    }

    [McpServerTool]
    [Description("Ставит музыку на паузу или возобновляет воспроизведение")]
    public static async Task<string> PlayPause() {
        var sessionResult = await GetSessionAsync();
        if (!sessionResult.Success)
            return Error("player_error", sessionResult.Error!);

        var session = sessionResult.Value;
        if (session is null)
            return Error("no_player", "Нет активного плеера");

        var playbackResult = await MediaPolicy.GuardAsync(() => Task.FromResult(session.GetPlaybackInfo()));
        if (!playbackResult.Success)
            return Error("player_error", playbackResult.Error!);

        var playbackInfo = playbackResult.Value;
        if (playbackInfo is null)
            return Error("player_error", "Не удалось получить состояние воспроизведения");

        var isPlaying = playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        var actionResult = isPlaying
            ? await MediaPolicy.GuardAsync(async () => await session.TryPauseAsync())
            : await MediaPolicy.GuardAsync(async () => await session.TryPlayAsync());

        if (!actionResult.Success)
            return Error("player_error", actionResult.Error!);
        if (!actionResult.Value)
            return Error("operation_failed", "Плеер отказался выполнить операцию");

        return Ok(isPlaying ? "Воспроизведение поставлено на паузу" : "Воспроизведение возобновлено", new {
            action = isPlaying ? "pause" : "play",
            playbackState = isPlaying ? "paused" : "playing"
        });
    }

    [McpServerTool]
    [Description("Переключает на следующий трек")]
    public static async Task<string> NextTrack() {
        var sessionResult = await GetSessionAsync();
        if (!sessionResult.Success)
            return Error("player_error", sessionResult.Error!);

        var session = sessionResult.Value;
        if (session is null)
            return Error("no_player", "Нет активного плеера");

        var result = await MediaPolicy.GuardAsync(async () => await session.TrySkipNextAsync());
        if (!result.Success)
            return Error("player_error", result.Error!);
        if (!result.Value)
            return Error("operation_failed", "Попытка переключить трек не удалась");

        return Ok("Следующий трек", new { action = "next" });
    }

    [McpServerTool]
    [Description("Переключает на предыдущий трек")]
    public static async Task<string> PreviousTrack() {
        var sessionResult = await GetSessionAsync();
        if (!sessionResult.Success)
            return Error("player_error", sessionResult.Error!);

        var session = sessionResult.Value;
        if (session is null)
            return Error("no_player", "Нет активного плеера");

        var result = await MediaPolicy.GuardAsync(async () => await session.TrySkipPreviousAsync());
        if (!result.Success)
            return Error("player_error", result.Error!);
        if (!result.Value)
            return Error("operation_failed", "Попытка переключить трек не удалась");

        return Ok("Предыдущий трек", new { action = "previous" });
    }

    [McpServerTool]
    [Description("Останавливает воспроизведение")]
    public static async Task<string> Stop() {
        var sessionResult = await GetSessionAsync();
        if (!sessionResult.Success)
            return Error("player_error", sessionResult.Error!);

        var session = sessionResult.Value;
        if (session is null)
            return Error("no_player", "Нет активного плеера");

        var result = await MediaPolicy.GuardAsync(async () => await session.TryStopAsync());
        if (!result.Success)
            return Error("player_error", result.Error!);
        if (!result.Value)
            return Error("operation_failed", "Попытка остановить воспроизведение не удалась");

        return Ok("Воспроизведение остановлено", new { action = "stop" });
    }

    [McpServerTool]
    [Description("Изменение уровня громкости системы. Опасная операция: требует подтверждения confirm = true.")]
    public static async Task<string> ChangeVolume(
        [Description("Значение от 1 до 100")] int volume,
        [Description("Подтверждение. Ставь true только после явной просьбы пользователя")] bool confirm = false) {
        if (!confirm)
            return Error("confirmation_required", "Для изменения громкости требуется confirm = true");
        if (!MediaPolicy.TryValidateVolume(volume, out var percent, out var error))
            return Error("invalid_volume", error!);

        var result = await MediaPolicy.GuardAsync(() => Task.Run(() => {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = percent / 100f;
            return percent;
        }));

        if (!result.Success)
            return Error("audio_error", result.Error!);

        return Ok($"Громкость установлена на {percent}%", new {
            volume = percent / 100f,
            percentVolume = percent
        });
    }

    [McpServerTool]
    [Description("Выключает системный звук. Опасная операция: требует подтверждения confirm = true.")]
    public static async Task<string> VolumeTurnOff(
        [Description("Подтверждение. Ставь true только после явной просьбы пользователя")] bool confirm = false) {
        if (!confirm)
            return Error("confirmation_required", "Для выключения звука требуется confirm = true");

        var result = await MediaPolicy.GuardAsync(() => Task.Run(() => {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.Mute = true;
            return true;
        }));

        if (!result.Success)
            return Error("audio_error", result.Error!);

        return Ok("Звук выключен", new { isMuted = true });
    }

    private static Task<GuardedResult<GlobalSystemMediaTransportControlsSession?>> GetSessionAsync()
        => MediaPolicy.GuardAsync(async () => {
            // Не кэшируем сессию: старый экземпляр может остаться от закрытого плеера.
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            return (GlobalSystemMediaTransportControlsSession?)manager.GetCurrentSession();
        });

    private static string Ok(string message, object? data = null)
        => MediaResponses.Ok(message, data);

    private static string Error(string cause, string description)
        => MediaResponses.Error(cause, description);
}
