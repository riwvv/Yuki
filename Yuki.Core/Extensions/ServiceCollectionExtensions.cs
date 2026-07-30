using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yuki.Core.Configurations;

namespace Yuki.Core.Extensions;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddYukiCore(this IServiceCollection services, IConfiguration configuration) {
        // Register core services here
        // Example: services.AddSingleton<IMyService, MyService>();

        services.Configure<STTSettings>(configuration.GetSection("STT"));
        services.Configure<TTSSettings>(configuration.GetSection("TTS"));

        return services;
    }
}
