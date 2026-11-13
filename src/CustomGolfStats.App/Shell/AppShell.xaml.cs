using Windows.UI.ViewManagement;

namespace CustomGolfStats.App.Shell;

/// <summary>Hosts the navigation frame, the startup state and the failure banner, and keeps content out of the system bars.</summary>
public sealed partial class AppShell : UserControl
{
    public AppShell()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ApplySafeArea();
        // Status-bar text colour follows the theme of the page on screen.
        // Deferred: on Android the theme switch reaches the page after this event, and the system can reset the bar meanwhile.
        ActualThemeChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(() => (RootFrame.Content as Views.RoutePage)?.ApplyStatusBar());
            _ = Task.Delay(500).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => (RootFrame.Content as Views.RoutePage)?.ApplyStatusBar()),
                TaskScheduler.Default);
        };
        RootFrame.Navigated += (_, _) => DispatcherQueue.TryEnqueue(() => (RootFrame.Content as Views.RoutePage)?.ApplyStatusBar());
        Loaded += (_, _) => ApplySafeArea();
    }

    public Frame Frame => RootFrame;

    /// <summary>Hides the startup panel once the first page is showing.</summary>
    public void StartupFinished() => StartupPanel.Visibility = Visibility.Collapsed;

    /// <summary>Startup failed (for example the database is from a newer app version): say so instead of a blank screen.</summary>
    public void StartupFailed(string message)
    {
        StartupError.Text = message;
        StartupError.Visibility = Visibility.Visible;
    }

    public void ShowBanner(string text)
    {
        BannerText.Text = text;
        Banner.Visibility = Visibility.Visible;
    }

    private void OnDismissBanner(object sender, RoutedEventArgs e) => Banner.Visibility = Visibility.Collapsed;

    /// <summary>Pads the content by the status bar / notch / navigation bar insets (the window is edge to edge on both platforms).
    /// Pages draw their own background, so the bars show the page colour behind them.</summary>
    private void ApplySafeArea()
    {
        if (XamlRoot is null)
            return;
        var visible = ApplicationView.GetForCurrentView().VisibleBounds;
        var size = XamlRoot.Size;
        if (visible.Width <= 0 || visible.Height <= 0 || size.Width <= 0)
            return;
        var left = Math.Max(0, visible.Left);
        var top = Math.Max(0, visible.Top);
        var right = Math.Max(0, size.Width - visible.Right);
        var bottom = Math.Max(0, size.Height - visible.Bottom);
        // The top inset is the pages' to handle (a round header band extends under the status bar).
        Root.Padding = new Thickness(left, 0, right, bottom);
        StartupPanel.Padding = new Thickness(0, top, 0, 0);
        Banner.Margin = new Thickness(12);
        SafeArea.SetTop(top);
    }
}
