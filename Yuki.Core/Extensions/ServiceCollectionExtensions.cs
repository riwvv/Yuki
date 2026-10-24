using BlackSharp.Core.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yuki.Core.Configurations;
using Yuki.Core.HostAgent.Contracts;
using Yuki.Core.HostAgent.Services;
using Yuki.Core.ResourceManagement.Contracts;
using Yuki.Core.ResourceManagement.Services;
using Yuki.Core.ServantAgent.Contracts;
using Yuki.Core.ServantAgent.Services;
using Yuki.Core.STT.Contracts;
using Yuki.Core.STT.Services;
using Yuki.Core.TTS.Contracts;
using Yuki.Core.TTS.Models;
using Yuki.Core.TTS.Services;
using Yuki.Core.Wrappers.Contracts;
using Yuki.Core.Wrappers.Services;
using Yuki.Core.Wrappers.Utils;

namespace Yuki.Core.Extensions;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddYukiCore(this IServiceCollection services, IConfiguration configuration) {
        services.Configure<STTSettings>(configuration.GetSection("STT"));
        services.Configure<TTSSettings>(configuration.GetSection("TTS"));
        services.Configure<HostSettings>(configuration.GetSection("Host"));
        services.Configure<WakeWordSettings>(configuration.GetSection("WakeWord"));
        services.Configure<AudioCaptureSettings>(configuration.GetSection("AudioCapture"));
        services.Configure<ResourceManagerSettings>(configuration.GetSection("ResourceManager"));

        services.AddHttpClient("Vosk", client => {
            client.BaseAddress = new Uri(configuration.GetSection("STT:Vosk:BaseUrl").Get<string>() ?? "https://alphacephei.com/vosk/models/");
        });

        services.AddSingleton<ILlamaChatEngineFactory, LlamaChatEngineFactory>();
        services.AddSingleton<IHostAgentService, HostAgentService>();
        services.AddSingleton<IWakeWordDetector, WakeWordDetector>();
        services.AddSingleton<IAudioCaptureService, MicrophoneCaptureService>();
        services.AddSingleton<IWakeWordListener, WakeWordListener>();
        services.AddSingleton<IVoiceActivityDetector, VoiceActivityDetector>();
        services.AddSingleton<VoskModelProvisioner>();
        services.AddSingleton<VoskModelPathProvider>();
        services.AddSingleton<ISpeechRecognizer>(sp => {
            var pathProvider = sp.GetRequiredService<VoskModelPathProvider>();
            if (pathProvider.ModelPath is null)
                throw new InvalidOperationException("Модель Vosk ещё не готова - провижининг должен завершиться раньше первого обращения к ISpeechRecognizer.");
            return new SpeechRecognizer(pathProvider.ModelPath);
        });
        services.AddSingleton<IVoiceInteractionService, VoiceInteractionService>();
        services.AddSingleton<IGpuLayerResolver, GpuLayerResolver>();
        services.AddSingleton<LhmVramProvider>();
        services.AddSingleton<IVramProvider>(sp => sp.GetRequiredService<LhmVramProvider>());
        services.AddSingleton<IGpuLoadProvider>(sp => sp.GetRequiredService<LhmVramProvider>());
        services.AddSingleton<ISafeToReloadGate, SafeToReloadGate>();
        services.AddSingleton<Config>(sp => {
            var onnxDir = Utils.GetTtsResourcePath("onnx");
            return Helper.LoadCfgs(onnxDir);
        });
        services.AddSingleton<Style>(sp => {
            var voiceStylePath = Utils.GetTtsResourcePath(Path.Combine("voice_styles", "F5.json"));
            return Helper.LoadVoiceStyle(voiceStylePath);
        });
        services.AddSingleton<IUnicodeProcessor, UnicodeProcessor>();
        services.AddSingleton<ITextToSpeechCore>(sp => {
            var config = sp.GetRequiredService<Config>();
            var onnxDir = Utils.GetTtsResourcePath("onnx");
            return new TextToSpeechCore(config, onnxDir);
        });
        services.AddSingleton<ITextToSpeechPlayer>(sp => {
            var tts = sp.GetRequiredService<ITextToSpeechCore>();
            var style = sp.GetRequiredService<Style>();
            var logger = sp.GetRequiredService<ILogger<TextToSpeechPlayer>>();
            return new TextToSpeechPlayer(tts, style, logger);
        });
        services.AddSingleton<IServantAgentService, ServantAgentService>();
        services.AddSingleton<LlamaWeightsProvider>(sp => {
            var hostSettings = sp.GetRequiredService<IOptions<HostSettings>>().Value;
            var modelPath = Utils.GetLlmModelPath(hostSettings.ModelName);
            var gpuLayerResolver = sp.GetRequiredService<IGpuLayerResolver>();
            var logger = sp.GetRequiredService<ILogger<LlamaWeightsProvider>>();
            var gpuLayers = gpuLayerResolver.ResolveGpuLayers(modelPath, 4096);
            logger.LogInformation($"Resource manager: выделяю {gpuLayers} слоёв на GPU для Host");
            return new LlamaWeightsProvider(modelPath, gpuLayers);
        });

        services.AddHostedService<ResourceMonitorBackgroundService>();
        services.AddHostedService<VoiceInteractionHostedService>();

        return services;
    }
}
