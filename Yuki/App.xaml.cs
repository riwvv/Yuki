using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Text;
using System.Windows;
using Yuki.Core.Extensions;
using Yuki.Core.HostAgent.Contracts;
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

            Test();

            base.OnStartup(e);
        }
        catch (Exception) {
            throw;
        }
    }

    private async void Test() {
        var logger = _host.Services.GetRequiredService<ILogger<App>>();
        var agent = _host.Services.GetRequiredService<IHostAgentService>();

        var text = new StringBuilder();

        await foreach (var token in agent.RespondAsync("Привет! Кратко расскажи о себе")) {
            text.Append(token);
        }

        logger.LogInformation(text.ToString().TrimEnd("\nUser:").ToString());
    }

    protected override async void OnExit(ExitEventArgs e) {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }
}
