using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomGolfStats.App.Core.Platform;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Storage.Rows;
using ApiException = CustomGolfStats.Api.Client.ApiException;
using CourseSummaryDto = CustomGolfStats.Api.Client.CourseSummaryDto;
using ICustomGolfStatsApiClient = CustomGolfStats.Api.Client.ICustomGolfStatsApiClient;
using ProviderSearchResultDto = CustomGolfStats.Api.Client.ProviderSearchResultDto;

namespace CustomGolfStats.App.Core.ViewModels;

/// <summary>
/// Pick a course. Saved courses on this device are searched immediately and work offline; when online the shared database and the
/// course provider are searched too (the provider's results are imported one at a time, only when the golfer picks one). A
/// course can also be added by hand, which works offline.
/// </summary>
public sealed partial class CoursePickerViewModel(LocalDb db, ICustomGolfStatsApiClient api, IConnectivity connectivity, IClock clock, INavigator navigator) : ViewModelBase
{
    public ObservableCollection<CourseRow> LocalResults { get; } = [];

    /// <summary>Courses in the shared database that are not necessarily on this device yet.</summary>
    public ObservableCollection<CourseSummaryDto> OnlineResults { get; } = [];

    /// <summary>Provider courses that can be imported.</summary>
    public ObservableCollection<ProviderSearchResultDto> ProviderResults { get; } = [];

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private bool _isSearchingOnline;

    /// <summary>A sentence about the online search (offline, unavailable), or null.</summary>
    [ObservableProperty] private string? _message;

    public async Task LoadAsync() => await SearchAsync();

    private CancellationTokenSource? _searchCts;
    private int _searchGeneration;

    /// <summary>Searches saved courses at once and, online, the shared database and provider. The latest query wins: starting a new
    /// search cancels the one in flight, and a late answer to an older query never replaces newer results.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchAsync()
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        var generation = ++_searchGeneration;
        bool Current() => generation == _searchGeneration;

        ErrorMessage = null;
        Message = null;
        var term = Query.Trim();

        var local = await db.Courses.SearchAsync(term);
        if (!Current())
            return;
        LocalResults.Clear();
        foreach (var course in local)
            LocalResults.Add(course);
        OnlineResults.Clear();
        ProviderResults.Clear();
        IsSearchingOnline = false;
        if (term.Length < 2)
            return;
        if (!connectivity.IsOnline)
        {
            Message = UiText.ProviderSearchOffline;
            return;
        }

        IsSearchingOnline = true;
        try
        {
            var shared = await api.CoursesSearchAsync(term, null, cts.Token);
            var provider = await api.CoursesProviderSearchAsync(term, cts.Token);
            if (!Current())
                return;
            foreach (var found in shared)
                if (LocalResults.All(l => l.Id != found.Id))
                    OnlineResults.Add(found);
            foreach (var found in provider)
                ProviderResults.Add(found);
        }
        catch (OperationCanceledException) when (!Current())
        {
            // superseded by a newer search
        }
        catch (Exception ex) when (Current() && ex is ApiException or HttpRequestException or IOException or TaskCanceledException)
        {
            Message = UiText.ProviderSearchFailed;
        }
        finally
        {
            if (Current())
                IsSearchingOnline = false;
        }
    }

    [RelayCommand]
    private Task SelectLocalAsync(CourseRow course) => navigator.NavigateAsync(Route.StartRound, new CourseArgs(course.Id));

    /// <summary>Downloads a course from the shared database to this device, then picks it.</summary>
    [RelayCommand]
    private async Task SelectOnlineAsync(CourseSummaryDto course) => await FetchAndSelectAsync(course.Id);

    /// <summary>Imports a provider course (or, if it was already imported by someone, fetches it), then picks it.</summary>
    [RelayCommand]
    private async Task ImportAsync(ProviderSearchResultDto result)
    {
        if (result.AlreadyImported && result.CourseId is { } existing)
        {
            await FetchAndSelectAsync(existing);
            return;
        }
        await RunOnlineAsync(async () =>
        {
            var detail = await api.CoursesImportAsync(new CustomGolfStats.Api.Client.ImportCourseRequest { ProviderCourseId = result.ProviderCourseId });
            await db.Courses.SaveFromServerAsync(ApiMapper.ToRecord(detail, clock.UtcNow));
            await navigator.NavigateAsync(Route.StartRound, new CourseArgs(detail.Id));
        });
    }

    private Task FetchAndSelectAsync(Guid courseId) => RunOnlineAsync(async () =>
    {
        var detail = await api.CoursesGetAsync(courseId);
        await db.Courses.SaveFromServerAsync(ApiMapper.ToRecord(detail, clock.UtcNow));
        await navigator.NavigateAsync(Route.StartRound, new CourseArgs(detail.Id));
    });

    private async Task RunOnlineAsync(Func<Task> action)
    {
        ErrorMessage = null;
        if (!connectivity.IsOnline)
        {
            ErrorMessage = UiText.NeedConnection;
            return;
        }
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or IOException or TaskCanceledException)
        {
            ErrorMessage = UiText.ImportFailed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = UiText.SomethingWentWrong; // e.g. the local save failed
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(CourseRow course)
    {
        await db.Courses.SetFavoriteAsync(course.Id, !course.IsFavorite);
        await SearchAsync();
    }

    [RelayCommand]
    private Task AddManualCourseAsync() => navigator.NavigateAsync(Route.ManualCourse);
}
