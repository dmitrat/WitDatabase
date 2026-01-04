using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.ViewModels;
using System;

namespace OutWit.Database.Studio;

sealed class Program
{
    #region Fields

    private static ServiceProvider? s_serviceProvider;

    #endregion

    #region Main

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            s_serviceProvider?.Dispose();
        }
    }

    #endregion

    #region Avalonia Configuration

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .AfterSetup(_ => ConfigureServices());

    #endregion

    #region Dependency Injection

    private static void ConfigureServices()
    {
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Services
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<ISettingsService, SettingsService>();

        // Application ViewModel (main container)
        services.AddSingleton<ApplicationViewModel>();

        s_serviceProvider = services.BuildServiceProvider();
    }

    public static T GetService<T>()
    {
        if (s_serviceProvider == null)
            throw new InvalidOperationException("Service provider not initialized");
            
        return s_serviceProvider.GetRequiredService<T>();
    }

    #endregion
}
