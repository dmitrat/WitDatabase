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
        InstallCrashHandlers();

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Fatal("Startup", ex);
            throw;
        }
        finally
        {
            s_serviceProvider?.Dispose();
        }
    }

    #endregion

    #region Crash Handling

    /// <summary>
    /// Studio had no unhandled-exception handling of any kind, and RelayCommandAsync.Execute is
    /// 'async void' - so an exception escaping any command body ended the process with no message,
    /// no trace and, since the console provider writes nowhere in a WinExe, no log either.
    ///
    /// These do not swallow anything. They write the failure down before it takes the process, and an
    /// unobserved task exception is marked observed so that a fire-and-forget continuation cannot kill
    /// a session over something nobody was waiting for.
    /// </summary>
    private static void InstallCrashHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Fatal("AppDomain.UnhandledException", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Fatal("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    private static void Fatal(string source, Exception? exception)
    {
        try
        {
            var logger = s_serviceProvider?.GetService<ILogger<Program>>();

            if (logger != null)
            {
                logger.LogCritical(exception, "Unhandled exception from {Source}", source);
                return;
            }

            // The failure may predate the service provider, so fall back to the same file directly.
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [CRT] {source} - {exception}"
                + Environment.NewLine;

            var path = FileLoggerProvider.DefaultPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, line);
        }
        catch
        {
            // nothing left to do; never fail inside the failure handler
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
        // ChaCha20-Poly1305 registers itself through a [ModuleInitializer], which runs when the
        // assembly is LOADED - and an assembly nothing has touched yet may not be. Asking explicitly
        // is the difference between the second encryption algorithm working and working sometimes.
        OutWit.Database.Core.BouncyCastle.BouncyCastleProviderRegistration.EnsureRegistered();

        var services = new ServiceCollection();

        // Logging - the console provider writes nowhere in a WinExe, so the file is the real one.
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddProvider(new FileLoggerProvider(FileLoggerProvider.DefaultPath()));
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Services. One manager, as many sessions as the user opens databases - this used to be one
        // IDatabaseService holding one connection, which is what made every tab share a target.
        services.AddSingleton<IConnectionManager, ConnectionManager>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IExportService, ExportService>();

        // The saved connections (WS-68), in a file of their own beside the settings: the two are
        // cleared for different reasons, and "reset settings" must not read as "forget my databases".
        services.AddSingleton<IConnectionProfileStore, ConnectionProfileStore>();

        // The interface language (WS-63). Built from the setting rather than from the machine's
        // culture: what language Studio speaks and how it writes a decimal are separate questions, and
        // deriving one from the other is how a value stops pasting into SQL.
        services.AddSingleton<Services.Localization.ILocalizationService>(provider =>
            new Services.Localization.LocalizationService(
                provider.GetRequiredService<ISettingsService>().Current.Language));

        // The notification centre (WS-7). One list for the whole application, and it writes every
        // entry to the log as well, so a trimmed list never loses what happened.
        services.AddSingleton<INotificationService, NotificationService>();

        // The owner window is resolved when the question is asked, not now: the ViewModel graph is
        // built before any window exists, and this singleton outlives every one of them.
        // The settings are read the same way and for the same reason: the service is a singleton and
        // the settings are live, so a question must ask what the setting says NOW rather than what it
        // said when the container was built.
        services.AddSingleton<IConfirmationService>(
            provider => new ConfirmationService(
                () => ViewModels.ApplicationViewModel.Instance.MainWindow,
                () => provider.GetRequiredService<ISettingsService>().Current));

        // The only place in Studio that is allowed to construct a window.
        services.AddSingleton<IDialogService>(
            _ => new DialogService(() => ViewModels.ApplicationViewModel.Instance.MainWindow));

        // The query history (WS-29), in a WitDatabase of Studio's own. It is opened on a background
        // thread and never waited for: a store that will not open leaves every query working.
        services.AddSingleton<IQueryHistoryService>(provider =>
        {
            var history = new QueryHistoryService(QueryHistoryService.DefaultPath(),
                provider.GetRequiredService<ILogger<QueryHistoryService>>());

            _ = history.InitializeAsync();

            return history;
        });

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
