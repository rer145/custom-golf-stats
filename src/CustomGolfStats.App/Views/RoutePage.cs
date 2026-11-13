using CustomGolfStats.App.Diagnostics;
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
        Loaded += (_, _) => Focus(FocusState.Programmatic);
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

    /// <summary>Loads the view model for this navigation. <paramref name="parameter"/> is the record the navigator was given.</summary>
    protected virtual Task LoadAsync(object? parameter) => Task.CompletedTask;
}
