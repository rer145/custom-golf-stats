using CustomGolfStats.App.Diagnostics;
using CustomGolfStats.App.Shell;
using Microsoft.UI.Xaml.Navigation;

namespace CustomGolfStats.App.Views;

/// <summary>
/// Base for every route's page. A page creates its view model in a field initializer with <see cref="Resolve{T}"/> (so x:Bind to
/// <c>ViewModel</c> works from the start), and overrides <see cref="LoadAsync"/> to pass the navigation parameter to the view model.
/// A load failure is reported by the global handler; view models already turn expected failures into ErrorMessage.
/// </summary>
public abstract partial class RoutePage : Page
{
    protected RoutePage()
    {
        Style = (Style)Application.Current.Resources["BrandPageStyle"];
        // Keep focus (and so the on-screen keyboard) off the first text box until the golfer taps one.
        IsTabStop = true;
        Loaded += (_, _) =>
        {
            Focus(FocusState.Programmatic);
            ApplyTopInset();
            ApplyStatusBar();
        };
        Unloaded += (_, _) => SafeArea.TopChanged -= ApplyTopInset;
        SafeArea.TopChanged += ApplyTopInset;
        ActualThemeChanged += (_, _) => ApplyStatusBar(); // the shell also re-applies it when the system theme changes
    }

    /// <summary>The page's round header band, if it has one (named "HeaderBand" in XAML).</summary>
    private Border? HeaderBand => FindName("HeaderBand") as Border;

    private Thickness? _bandPadding;

    /// <summary>A page with a header band draws it under the status bar (the band grows by the inset); any other page starts below it.</summary>
    private void ApplyTopInset()
    {
        var top = SafeArea.Top;
        if (HeaderBand is { } band)
        {
            _bandPadding ??= band.Padding;
            var p = _bandPadding.Value;
            band.Padding = new Thickness(p.Left, p.Top + top, p.Right, p.Bottom);
        }
        else if (Content is FrameworkElement content)
        {
            content.Margin = new Thickness(0, top, 0, 0);
        }
    }

    /// <summary>Light status-bar text over a header band (always dark) or a dark page; dark text over a light page.</summary>
    internal void ApplyStatusBar()
    {
#if __IOS__ || __ANDROID__
        try
        {
            var dark = ActualTheme == ElementTheme.Dark
                || (ActualTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
            var light = HeaderBand is not null || dark;
            Windows.UI.ViewManagement.StatusBar.GetForCurrentView().ForegroundColor =
                light ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        }
        catch (Exception)
        {
            // best effort: the bar keeps its previous colours
        }
#endif
    }

    protected static T Resolve<T>() where T : notnull => App.Services.GetRequiredService<T>();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        try
        {
            await LoadAsync(e.Parameter);
        }
        catch (Exception ex)
        {
            CrashReporter.Report($"loading {GetType().Name}", ex);
        }
    }

    /// <summary>The header's back button.</summary>
    protected void OnBack(object sender, RoutedEventArgs e) => _ = Resolve<INavigator>().GoBackAsync();

    /// <summary>Asks before something that cannot be undone (deleting a round or a hole). True when the golfer confirmed.</summary>
    private static bool _dialogOpen;

    protected async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        // Only one dialog at a time (a second tap while one is open would throw from ShowAsync).
        if (_dialogOpen)
            return false;
        _dialogOpen = true;
        try
        {
            return await ShowConfirmAsync(title, message, confirmText);
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private async Task<bool> ShowConfirmAsync(string title, string message, string confirmText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = confirmText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.None, // no system-accent button: both choices read as equals
            XamlRoot = XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>Loads the view model for this navigation. <paramref name="parameter"/> is the record the navigator was given.</summary>
    protected virtual Task LoadAsync(object? parameter) => Task.CompletedTask;
}
