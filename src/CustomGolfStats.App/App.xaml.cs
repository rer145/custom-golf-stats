using CustomGolfStats.App.Composition;
using CustomGolfStats.App.Core.ApiAccess;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Diagnostics;
using CustomGolfStats.App.Navigation;
using CustomGolfStats.App.Shell;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Uno.Resizetizer;

namespace CustomGolfStats.App;

public partial class App : Application
{
    private static IServiceProvider? _services;

    public App()
    {
        InitializeComponent();
    }

    /// <summary>The composition root's container, available once startup has opened the database. Pages resolve their view model here.</summary>
    public static IServiceProvider Services => _services ?? throw new InvalidOperationException("The app has not finished starting.");

    protected Window? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new Window();
        if (MainWindow.Content is not AppShell shell)
        {
            shell = new AppShell();
            MainWindow.Content = shell;
        }

        var dispatcher = DispatcherQueue.GetForCurrentThread();
        CrashReporter.Install(this, dispatcher, shell.ShowBanner);
        shell.Frame.NavigationFailed += (_, e) =>
        {
            e.Handled = true;
            CrashReporter.Report($"navigation to {e.SourcePageType?.Name}", e.Exception);
        };

        MainWindow.SetWindowIcon();
        MainWindow.Activate();

        _ = StartAsync(shell, dispatcher);
    }

    /// <summary>Opens the local database, composes the services and shows the sign-in route (which goes straight home when a
    /// verified session exists).</summary>
    private static async Task StartAsync(AppShell shell, DispatcherQueue dispatcher)
    {
        try
        {
            var db = await LocalDb.OpenAsync(AppConfig.DatabasePath, new SystemClock());
            var navigator = new FrameNavigator(shell.Frame, dispatcher);
            var services = AppServices.Build(db, navigator, AppConfig.ApiBaseUrl);
            _services = services;

            // Raised by TokenService on whatever thread ran the refresh: the navigator marshals to the UI thread.
            services.GetRequiredService<TokenService>().SessionExpired += () =>
                _ = navigator.NavigateAsync(Route.SignIn).ContinueWith(
                    t => CrashReporter.Report("session expired navigation", t.Exception!), TaskContinuationOptions.OnlyOnFaulted);

            SystemBackButton.Attach(shell.Frame);
            await navigator.NavigateAsync(Route.SignIn);
            shell.StartupFinished();
        }
        catch (Exception ex)
        {
            SafeLog.Error("startup", ex);
            shell.StartupFailed(ex is InvalidOperationException && ex.Message.Contains("newer than this app", StringComparison.Ordinal)
                ? "This device's data was saved by a newer version of the app. Update the app to continue."
                : "The app could not start. Close it and try again.");
        }
    }

    /// <summary>Uno framework logging (debug builds only). The app's own log is <see cref="SafeLog"/>.</summary>
    public static void InitializeLogging()
    {
#if DEBUG
        var factory = LoggerFactory.Create(builder =>
        {
#if __IOS__
            builder.AddProvider(new global::Uno.Extensions.Logging.OSLogLoggerProvider());
#endif
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddFilter("Uno", LogLevel.Warning);
            builder.AddFilter("Windows", LogLevel.Warning);
            builder.AddFilter("Microsoft", LogLevel.Warning);
        });

        global::Uno.Extensions.LogExtensionPoint.AmbientLoggerFactory = factory;
#if HAS_UNO
        global::Uno.UI.Adapter.Microsoft.Extensions.Logging.LoggingAdapter.Initialize();
#endif
#endif
    }
}
