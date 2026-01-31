using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OTFE.Services;
using OTFE.ViewModels;

namespace OTFE;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private readonly ServiceProvider _serviceProvider;

    public App()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Logging
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddDebug();
        });

        // Services
        services.AddSingleton<ITraceService, TraceService>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<IAnomalyDetectionService, AnomalyDetectionService>();

        // ViewModels
        services.AddTransient<MainWindowViewModel>();

        // Views
        services.AddTransient<MainWindow>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider.Dispose();
        base.OnExit(e);
    }
}
