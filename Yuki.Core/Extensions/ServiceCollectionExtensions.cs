using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yuki.Core.Configurations;
using Yuki.Core.HostAgent.Contracts;
using Yuki.Core.HostAgent.Services;
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

        services.AddSingleton<ILlamaChatEngineFactory, LlamaChatEngineFactory>();
        services.AddSingleton<IHostAgentService, HostAgentService>();

        return services;
    }
}
