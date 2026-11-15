using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NAudio.CoreAudioApi;
using System.ComponentModel;
using System.Text.Json;
using Windows.Media.Control;

namespace Yuki.Mcp.MediaPlayer.Tools;

internal class MediaPlayerTools {
    private static GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private static GlobalSystemMediaTransportControlsSession? _currentSession;

    [McpServerTool]
    [Description("Получает информацию о текущем треке: исполнитель и название")]
    public static async Task<string> GetCurrentTrackInfo() {
        try {
            var session = await GetCurrentSession();
            if (session == null) {
                return JsonSerializer.Serialize(new {
                    status = "warning",
                    cause = "no_player",
                    description = "Ни один плеер сейчас не воспроизводит музыку"
                });
            }

            var mediaProperties = await session.TryGetMediaPropertiesAsync();
            if (mediaProperties == null) {
                return JsonSerializer.Serialize(new {
                    status = "error",
                    cause = "no_media_info",
                    description = "Не удалось получить информацию о треке"
                });
            }

            string title = mediaProperties.Title ?? "Неизвестно";
            string artist = mediaProperties.Artist ?? "Неизвестный исполнитель";

            return JsonSerializer.Serialize(new {
                status = "done",
                message = $"{artist} - {title}",
                titleMusic = title,
                artistMusic = artist,
                fullTrack = $"{artist} - {title}"
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "error",
                cause = "exception",
                description = $"Ошибка получения информации: {ex.Message}"
            });
        }
    }

    [McpServerTool]
    [Description("Ставит музыку на паузу или возобновляет воспроизведение")]
    public static async Task<string> PlayPause() {
        try {
            var session = await GetCurrentSession();
            if (session == null) {
                return JsonSerializer.Serialize(new {
                    status = "error",
                    cause = "no_player",
                    description = "Нет активного плеера"
                });
            }

            var playbackInfo = session.GetPlaybackInfo();
            var currentStatus = playbackInfo.PlaybackStatus;

            if (currentStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) {
                await session.TryPauseAsync();
                return JsonSerializer.Serialize(new {
                    status = "done",
                    message = "Воспроизведение поставлено на паузу",
                    action = "pause",
                    currentStatus = "paused"
                });
            }
            else {
                await session.TryPlayAsync();
                return JsonSerializer.Serialize(new {
                    status = "done",
                    message = "Воспроизведение возобновлено",
                    action = "play",
                    currentStatus = "playing"
                });
            }
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "error",
                cause = "exception",
                description = $"Ошибка: {ex.Message}"
            });
        }
    }

    [McpServerTool]
    [Description("Переключает на следующий трек")]
    public static async Task<string> NextTrack() {
        try {
            var session = await GetCurrentSession();
            if (session == null) {
                return JsonSerializer.Serialize(new {
                    status = "error",
                    cause = "no_player",
                    description = "Нет активного плеера"
                });
            }

            return await session.TrySkipNextAsync()
                ? JsonSerializer.Serialize(new {
                    status = "done",
                    message = "Следующий трек",
                    action = "next"
                })
                : JsonSerializer.Serialize(new {
                    status = "error",
                    cause = "unknown",
                    description = "Попытка переключить трек не удалась по неизвестной причине"
                });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "error",
                cause = "exception",
                description = $"Ошибка: {ex.Message}"
            });
        }
    }

    [McpServerTool]
    [Description("Переключает на предыдущий трек")]
    public static async Task<string> PreviousTrack() {
        try {
            var session = await GetCurrentSession();
            if (session == null) {
                return JsonSerializer.Serialize(new {
                    status = "error",
                    cause = "no_player",
                    description = "Нет активного плеера"
                });
            }

            await session.TrySkipPreviousAsync();

            return JsonSerializer.Serialize(new {
                status = "done",
                message = "Предыдущий трек",
                action = "previous"
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "error",
                cause = "exception",
                description = $"Ошибка: {ex.Message}"
            });
        }
    }

    [McpServerTool]
    [Description("Останавливает воспроизведение")]
    public static async Task<string> Stop() {
        try {
            var session = await GetCurrentSession();
            if (session == null) {
                return JsonSerializer.Serialize(new {
                    status = "error",
                    cause = "no_player",
                    description = "Нет активного плеера"
                });
            }

            await session.TryStopAsync();

            return JsonSerializer.Serialize(new {
                status = "done",
                message = "Воспроизведение остановлено",
                action = "stop"
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "error",
                cause = "exception",
                description = $"Ошибка: {ex.Message}"
            });
        }
    }

    [McpServerTool]
    [Description("Изменение уровня громкости системы")]
    public static string ChangeVolume([Description("Значение от 0.0 до 1.0. Например: 'громкость 50%' значит 0.5f")] float volume) {
        try {
            var clampedVolume = Math.Clamp(volume, 0.0f, 1.0f);

            var devices = new MMDeviceEnumerator();
            var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = clampedVolume;

            var percent = (int)(clampedVolume * 100);

            return JsonSerializer.Serialize(new {
                status = "DONE",
                message = $"Громкость установлена на {percent}%",
                volume = clampedVolume,
                percentVolume = percent
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "audio_error",
                description = $"Ошибка изменения громкости: {ex.Message}"
            });
        }
    }

    [McpServerTool]
    [Description("Отключение громкости системы")]
    public static string VolumeTurnOff() {
        try {
            var devices = new MMDeviceEnumerator();
            var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            device.AudioEndpointVolume.Mute = true;

            return JsonSerializer.Serialize(new {
                status = "DONE",
                message = "Звук выключен",
                isMuted = true
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "audio_error",
                description = $"Ошибка отключения звука: {ex.Message}"
            });
        }
    }

    private static async Task<GlobalSystemMediaTransportControlsSessionManager> GetSessionManager() {
        if (_sessionManager == null) {
            _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        return _sessionManager;
    }

    private static async Task<GlobalSystemMediaTransportControlsSession?> GetCurrentSession() {
        var manager = await GetSessionManager();
        _currentSession = manager.GetCurrentSession();
        return _currentSession;
    }
}
