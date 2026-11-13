using CustomGolfStats.App.Views;
using Microsoft.UI.Dispatching;

namespace CustomGolfStats.App.Navigation;

/// <summary>Which page shows each <see cref="Route"/>, and how arriving at a route trims the back stack.</summary>
public static class RouteMap
{
    private static readonly Dictionary<Route, Type> Pages = new()
    {
        [Route.SignIn] = typeof(SignInPage),
        [Route.Home] = typeof(HomePage),
        [Route.CoursePicker] = typeof(CoursePickerPage),
        [Route.ManualCourse] = typeof(ManualCoursePage),
        [Route.StartRound] = typeof(StartRoundPage),
        [Route.Round] = typeof(RoundPage),
        [Route.QuickHole] = typeof(QuickHolePage),
        [Route.ShotEntry] = typeof(ShotEntryPage),
        [Route.RoundSummary] = typeof(RoundSummaryPage),
        [Route.Scorecard] = typeof(ScorecardPage),
        [Route.OverallStats] = typeof(OverallStatsPage),
        [Route.Profile] = typeof(ProfilePage),
    };

    /// <summary>Pages removed from the back stack on arrival, so Back never returns into a finished flow.</summary>
    private static readonly Dictionary<Route, Type[]> Trims = new()
    {
        // The round is started: Back from it goes home, not to the picker or the start form.
        [Route.Round] = [typeof(CoursePickerPage), typeof(ManualCoursePage), typeof(StartRoundPage)],
        // A manual course was saved: Back from the start form goes to the picker, not the (submitted) form.
        [Route.StartRound] = [typeof(ManualCoursePage)],
        // The round is finished: Back from the summary goes home.
        [Route.RoundSummary] = [typeof(RoundPage), typeof(QuickHolePage), typeof(ShotEntryPage)],
    };

    public static Type PageFor(Route route) => Pages[route];

    /// <summary>Sign-in and home start a fresh stack.</summary>
    public static bool IsRoot(Route route) => route is Route.SignIn or Route.Home;

    public static IReadOnlyCollection<Type> TrimmedOnArrival(Route route) => Trims.TryGetValue(route, out var t) ? t : [];

    public static IEnumerable<Route> All => Pages.Keys;
}

/// <summary>
/// <see cref="INavigator"/> over a WinUI <see cref="Frame"/>. Callable from any thread (a view model continuation or the
/// session-expired event): the work is marshalled to the UI thread. The page's view model is resolved by the page and receives the
/// parameter (one of <see cref="CourseArgs"/>, <see cref="RoundArgs"/>, <see cref="HoleArgs"/> or null) in its load method.
/// </summary>
public sealed class FrameNavigator(Frame frame, DispatcherQueue dispatcher) : INavigator
{
    public Task NavigateAsync(Route route, object? parameter = null) => OnUiThreadAsync(() =>
    {
        var page = RouteMap.PageFor(route);
        if (!frame.Navigate(page, parameter))
            throw new InvalidOperationException($"Navigation to {route} failed.");

        if (RouteMap.IsRoot(route))
        {
            frame.BackStack.Clear();
            return;
        }
        var trimmed = RouteMap.TrimmedOnArrival(route);
        for (var i = frame.BackStack.Count - 1; i >= 0; i--)
            if (trimmed.Contains(frame.BackStack[i].SourcePageType))
                frame.BackStack.RemoveAt(i);
    });

    public Task GoBackAsync() => OnUiThreadAsync(() =>
    {
        if (frame.CanGoBack)
            frame.GoBack();
    });

    private Task OnUiThreadAsync(Action action)
    {
        if (dispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = dispatcher.TryEnqueue(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        if (!queued)
            tcs.SetException(new InvalidOperationException("The UI thread is not running."));
        return tcs.Task;
    }
}
