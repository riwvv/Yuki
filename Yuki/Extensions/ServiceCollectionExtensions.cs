using Microsoft.Extensions.DependencyInjection;
using Yuki.ViewModels;
using Yuki.Views;

namespace Yuki.Extensions;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddYuki(this IServiceCollection services) {
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainViewModel>();

        return services;
    }
}
