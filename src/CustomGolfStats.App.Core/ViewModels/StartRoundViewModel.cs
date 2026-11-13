using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ICustomGolfStatsApiClient = CustomGolfStats.Api.Client.ICustomGolfStatsApiClient;
using RoundType = CustomGolfStats.Api.Client.RoundType;
using CustomGolfStats.App.Core.Platform;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.Domain;
using DomEntryMode = CustomGolfStats.Domain.EntryMode;

namespace CustomGolfStats.App.Core.ViewModels;

public sealed record TeeOption(Guid TeeId, string Name, string Description);

/// <summary>
/// Choose tee, date, round type and entry mode, then create the round. Shot-by-shot is gated by the cached entitlements
/// (<see cref="Entitlements"/> from the domain): offline it uses the cached trial count, decrements it locally when the round is
/// started, and when it is not allowed the option is disabled with a reason. Weather is fetched afterwards and never delays or
/// blocks the round.
/// </summary>
public sealed partial class StartRoundViewModel(
    LocalDb db, IClock clock, INavigator navigator, IConnectivity connectivity, ILocationOnce location, ICustomGolfStatsApiClient api) : ViewModelBase
{
    private CourseRecord? _course;

    public string CourseName => _course?.Course.Name ?? "";
    public ObservableCollection<TeeOption> Tees { get; } = [];
    public IReadOnlyList<RoundType> RoundTypes { get; } = Enum.GetValues<RoundType>();

    [ObservableProperty] private TeeOption? _selectedTee;
    [ObservableProperty] private DateTime _playedOn = DateTime.Today;
    [ObservableProperty] private RoundType _roundType = RoundType.Casual;
    [ObservableProperty] private DomEntryMode _mode = DomEntryMode.Quick;

    /// <summary>Shot-by-shot may be chosen.</summary>
    [ObservableProperty] private bool _shotByShotAllowed;

    /// <summary>Why shot-by-shot is disabled (null when it is allowed).</summary>
    [ObservableProperty] private string? _shotByShotDisabledReason;

    /// <summary>"2 free shot-by-shot rounds left." for a free golfer with a trial running, otherwise null.</summary>
    [ObservableProperty] private string? _trialRoundsText;

    /// <summary>How long weather lookups (including the one location reading) may take.</summary>
    public TimeSpan WeatherTimeout { get; set; } = TimeSpan.FromSeconds(4);

    /// <summary>The background weather fill-in started by the last <see cref="StartAsync"/>; completes without throwing.</summary>
    public Task WeatherTask { get; private set; } = Task.CompletedTask;

    public async Task LoadAsync(CourseArgs args)
    {
        PlayedOn = clock.UtcNow.LocalDateTime.Date;
        _course = await db.Courses.GetAsync(args.CourseId);
        OnPropertyChanged(nameof(CourseName));
        Tees.Clear();
        if (_course is null)
        {
            ErrorMessage = UiText.CourseNotFound;
            return;
        }

        foreach (var tee in _course.Tees.OrderBy(t => t.Tee.Name, StringComparer.OrdinalIgnoreCase))
        {
            var version = tee.Versions.OrderByDescending(v => v.Version.EffectiveFrom).FirstOrDefault()?.Version;
            var description = version is null ? tee.Tee.Name : UiText.TeeDescription(version.Par, version.Rating, version.Slope);
            Tees.Add(new TeeOption(tee.Tee.Id, tee.Tee.Name, description));
        }
        SelectedTee = Tees.Count == 1 ? Tees[0] : null;
        if (Tees.Count == 0)
            ErrorMessage = UiText.CourseHasNoTees;

        await RefreshEntitlementsAsync();
    }

    /// <summary>Re-reads the cached entitlements and sets the shot-by-shot gate. The cache holds the trial rounds remaining, so the domain
    /// is asked with "none used of what is left": paid golfers get null (unlimited), free golfers the count, never negative.</summary>
    public async Task RefreshEntitlementsAsync()
    {
        var row = await db.Entitlements.GetAsync();
        if (row is null)
        {
            ShotByShotAllowed = false;
            ShotByShotDisabledReason = UiText.ShotByShotNeedsAccount;
            TrialRoundsText = null;
        }
        else
        {
            var allowance = row.TrialShotRoundsRemaining ?? 0;
            var remaining = Entitlements.TrialRoundsRemaining(row.Tier, 0, allowance);
            var allowed = row.ShotByShotEntry && Entitlements.CanUse(Feature.ShotByShotEntry, row.Tier, 0, allowance);
            ShotByShotAllowed = allowed;
            ShotByShotDisabledReason = allowed ? null : remaining == 0 ? UiText.ShotByShotTrialUsedUp : UiText.ShotByShotNotAvailable;
            TrialRoundsText = remaining is > 0 and var left ? UiText.TrialRoundsLeft(left) : null;
        }

        if (!ShotByShotAllowed && Mode == DomEntryMode.ShotByShot)
            Mode = DomEntryMode.Quick;
    }

    partial void OnModeChanged(DomEntryMode value)
    {
        // A disabled option cannot be selected.
        if (value == DomEntryMode.ShotByShot && !ShotByShotAllowed)
            Mode = DomEntryMode.Quick;
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsBusy)
            return;
        ErrorMessage = null;
        if (_course is null)
        {
            ErrorMessage = UiText.CourseNotFound;
            return;
        }
        if (SelectedTee is null)
        {
            ErrorMessage = UiText.ChooseTee;
            return;
        }

        IsBusy = true;
        var trialUsed = false;
        try
        {
            // Everything that can fail for a reason of the golfer's choices is resolved before a trial round is spent.
            var tee = _course.Tees.First(t => t.Tee.Id == SelectedTee.TeeId);
            var version = await db.Courses.GetVersionInEffectAsync(tee.Tee.Id, PlayedOn.Date);
            if (version is null)
            {
                ErrorMessage = UiText.CourseHasNoTees;
                return;
            }

            if (Mode == DomEntryMode.ShotByShot)
            {
                await RefreshEntitlementsAsync();
                var use = ShotByShotAllowed ? await db.Entitlements.TryUseShotByShotAsync() : default;
                if (!use.Allowed)
                {
                    await RefreshEntitlementsAsync();
                    ErrorMessage = ShotByShotDisabledReason ?? UiText.ShotByShotNotAvailable;
                    return;
                }
                trialUsed = use.CountDropped;
            }

            var now = clock.UtcNow;
            var isToday = PlayedOn.Date == now.LocalDateTime.Date;
            var round = new RoundRow
            {
                Id = Guid.NewGuid(), CourseId = _course.Course.Id, CourseName = _course.Course.Name, TeeId = tee.Tee.Id, TeeName = tee.Tee.Name,
                TeeVersionId = version.Id, PlayedOn = PlayedOn.Date, StartedAt = isToday ? now : null, EntryMode = Mode, RoundType = RoundType,
            };
            try
            {
                await db.Rounds.SaveAsync(new RoundRecord(round, []));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Only a failed save needs the trial round back. Show the problem instead of throwing at the UI.
                if (trialUsed)
                    await db.Entitlements.RefundTrialRoundAsync();
                ErrorMessage = UiText.CouldNotStartRound;
                return;
            }

            try
            {
                await navigator.NavigateAsync(Route.Round, new RoundArgs(round.Id));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The round is saved (and the trial, if any, rightly spent): it is on the home screen, so say that rather than "not started".
                ErrorMessage = UiText.CouldNotOpenRound;
            }
            if (round.StartedAt is { } startedAt && connectivity.IsOnline)
            {
                var course = _course.Course;
                WeatherTask = Task.Run(() => ApplyWeatherAsync(round.Id, course, startedAt)); // off the caller so nothing here can hold up the round
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = UiText.CouldNotStartRound;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Weather for the course (or one coarse location reading) at the start time. Every failure is swallowed.</summary>
    private async Task ApplyWeatherAsync(Guid roundId, CourseRow course, DateTimeOffset startedAt)
    {
        try
        {
            double? lat = course.Latitude, lon = course.Longitude;
            if (lat is null || lon is null)
            {
                // The permission prompt waits for the golfer; only the reading and the request below are time limited.
                if (!await location.RequestAccessAsync(CancellationToken.None))
                    return;
            }

            using var cts = new CancellationTokenSource(WeatherTimeout);
            if (lat is null || lon is null)
            {
                var point = await location.GetCoarseLocationAsync(cts.Token).WaitAsync(WeatherTimeout);
                if (point is null)
                    return;
                (lat, lon) = (point.Latitude, point.Longitude);
            }

            var weather = await api.WeatherGetAsync(lat, lon, startedAt, cts.Token).WaitAsync(WeatherTimeout);
            await db.Rounds.UpdateHeaderAsync(roundId, r =>
            {
                r.WeatherConditions ??= weather.Conditions;
                r.TemperatureF ??= weather.TemperatureF;
                r.WindSpeedMph ??= weather.WindSpeedMph;
                r.WindDirectionDegrees ??= weather.WindDirectionDegrees;
            });
        }
        catch (Exception)
        {
            // Weather is optional: the API fills it in when the round syncs if it is missing.
        }
    }
}
