using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Windows;
using Yuki.Core.Extensions;
using Yuki.Extensions;
using Yuki.ViewModels;
using Yuki.Views;

namespace Yuki;

public partial class App : Application {
    private readonly IHost _host;

    public App() {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddSerilog(config => config
            .ReadFrom.Configuration(builder.Configuration)
            .WriteTo.Debug()
            .WriteTo.File("logs/yuki-.log", rollingInterval: RollingInterval.Day));

        builder.Services.AddYuki();
        builder.Services.AddYukiCore(builder.Configuration);

        _host = builder.Build();
    }

    protected override async void OnStartup(StartupEventArgs e) {
        try {
            await _host.StartAsync();

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            var mainWindowViewModel = _host.Services.GetRequiredService<MainViewModel>();
            mainWindow.DataContext = mainWindowViewModel;
            mainWindow.Show();

            base.OnStartup(e);
        }
        catch (Exception) {
            throw;
        }
    }

    protected override async void OnExit(ExitEventArgs e) {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }
}
