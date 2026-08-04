using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yuki.Core.Configurations;
using Yuki.Core.HostAgent.Contracts;
using Yuki.Core.HostAgent.Services;
using Yuki.Core.STT.Contracts;
using Yuki.Core.STT.Services;
using Yuki.Core.Wrappers.Contracts;
using Yuki.Core.Wrappers.Services;

namespace Yuki.Core.Extensions;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddYukiCore(this IServiceCollection services, IConfiguration configuration) {
        // Register core services here
        // Example: services.AddSingleton<IMyService, MyService>();

        services.Configure<STTSettings>(configuration.GetSection("STT"));
        services.Configure<TTSSettings>(configuration.GetSection("TTS"));
        services.Configure<HostSettings>(configuration.GetSection("Host"));
        services.Configure<WakeWordSettings>(configuration.GetSection("WakeWord"));
        services.Configure<AudioCaptureSettings>(configuration.GetSection("AudioCapture"));

        services.AddSingleton<ILlamaChatEngineFactory, LlamaChatEngineFactory>();
        services.AddSingleton<IHostAgentService, HostAgentService>();
        services.AddSingleton<IWakeWordDetector, WakeWordDetector>();
        services.AddSingleton<IAudioCaptureService, MicrophoneCaptureService>();
        services.AddSingleton<IWakeWordListener, WakeWordListener>();
        services.AddSingleton<IVoiceActivityDetector, VoiceActivityDetector>();
        services.AddSingleton<VoskModelPathProvider>();
        services.AddSingleton<ISpeechRecognizer>(sp => {
            var pathProvider = sp.GetRequiredService<VoskModelPathProvider>();
            if (pathProvider.ModelPath is null)
                throw new InvalidOperationException("Модель Vosk ещё не готова - провижининг должен завершиться раньше первого обращения к ISpeechRecognizer.");
            return new SpeechRecognizer(pathProvider.ModelPath);
        });
        services.AddSingleton<IVoiceInteractionService, VoiceInteractionService>();

        return services;
    }
}
