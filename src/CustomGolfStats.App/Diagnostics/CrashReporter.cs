using Microsoft.UI.Dispatching;

namespace CustomGolfStats.App.Diagnostics;

/// <summary>
/// The last line of defence: view models already turn expected failures into ErrorMessage (ViewModelBase.TryAsync), so anything
/// that reaches here is a bug. It is logged without tokens and the golfer sees a short banner instead of the app closing.
/// </summary>
public static class CrashReporter
{
    private static DispatcherQueue? _dispatcher;
    private static Action<string>? _showBanner;

    public const string BannerText = "Something went wrong. Your rounds are saved on this device.";

    /// <summary>Hooks every global exception source. Call once at startup, before the first page.</summary>
    public static void Install(Application app, DispatcherQueue dispatcher, Action<string> showBanner)
    {
        _dispatcher = dispatcher;
        _showBanner = showBanner;

        app.UnhandledException += (_, e) =>
        {
            e.Handled = true; // keep the app alive: local data is safe in SQLite
            Report("UI thread", e.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Report("background thread", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            // A cancelled background task that nobody waited for is not a failure the golfer needs to hear about.
            if (e.Exception.Flatten().InnerExceptions.All(IsCancellation))
            {
                SafeLog.Info("unobserved cancellation ignored");
                return;
            }
            Report("unobserved task", e.Exception);
        };
    }

    private static bool IsCancellation(Exception ex) =>
        ex is OperationCanceledException || ex.GetType().Name == "OperationCanceledException"; // also Android.OS.OperationCanceledException

    /// <summary>Logs the failure and shows the banner (on the UI thread).</summary>
    public static void Report(string context, Exception ex)
    {
        SafeLog.Error(context, ex);
        var show = _showBanner;
        if (show is null)
            return;
        if (_dispatcher is { } d && !d.HasThreadAccess)
            d.TryEnqueue(() => show(BannerText));
        else
            show(BannerText);
    }
}
