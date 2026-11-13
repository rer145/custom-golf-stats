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
            var description = version is null ? tee.Tee.Name : $"Par {version.Par} · {version.Rating:0.0}/{version.Slope}";
            Tees.Add(new TeeOption(tee.Tee.Id, tee.Tee.Name, description));
        }
        SelectedTee = Tees.Count == 1 ? Tees[0] : null;
        if (Tees.Count == 0)
            ErrorMessage = UiText.CourseHasNoTees;

        await RefreshEntitlementsAsync();
    }

    /// <summary>Re-reads the cached entitlements and sets the shot-by-shot gate.</summary>
    public async Task RefreshEntitlementsAsync()
    {
        var row = await db.Entitlements.GetAsync();
        TrialRoundsText = row is { Tier: Tier.Free, TrialShotRoundsRemaining: > 0 and var left } ? UiText.TrialRoundsLeft(left) : null;

        if (row is null)
        {
            ShotByShotAllowed = false;
            ShotByShotDisabledReason = UiText.ShotByShotNeedsAccount;
        }
        else
        {
            // The cache holds the remaining count, so ask the domain with "none used of what is left".
            var allowed = row.ShotByShotEntry && Entitlements.CanUse(Feature.ShotByShotEntry, row.Tier, 0, row.TrialShotRoundsRemaining ?? 0);
            ShotByShotAllowed = allowed;
            ShotByShotDisabledReason = allowed ? null
                : row.Tier == Tier.Free && row.TrialShotRoundsRemaining is 0 ? UiText.ShotByShotTrialUsedUp
                : UiText.ShotByShotNotAvailable;
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
        var consumedTrial = false;
        try
        {
            if (Mode == DomEntryMode.ShotByShot)
            {
                await RefreshEntitlementsAsync();
                if (!ShotByShotAllowed || !await db.Entitlements.TryConsumeTrialRoundAsync())
            {
                    ErrorMessage = ShotByShotDisabledReason ?? UiText.ShotByShotNotAvailable;
                    return;
                }
                consumedTrial = (await db.Entitlements.GetAsync()) is { Tier: Tier.Free };
            }

            var tee = _course.Tees.First(t => t.Tee.Id == SelectedTee.TeeId);
            var version = await db.Courses.GetVersionInEffectAsync(tee.Tee.Id, PlayedOn.Date)
                ?? tee.Versions.OrderBy(v => v.Version.EffectiveFrom).FirstOrDefault()?.Version; // played before the first recorded version
            if (version is null)
            {
                ErrorMessage = UiText.CourseHasNoTees;
                if (consumedTrial)
                    await db.Entitlements.RefundTrialRoundAsync();
                return;
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
            catch
            {
                if (consumedTrial)
                    await db.Entitlements.RefundTrialRoundAsync();
                throw;
            }

            await navigator.NavigateAsync(Route.Round, new RoundArgs(round.Id));
            if (round.StartedAt is { } startedAt && connectivity.IsOnline)
            {
                var course = _course.Course;
                WeatherTask = Task.Run(() => ApplyWeatherAsync(round.Id, course, startedAt)); // off the caller so nothing here can hold up the round
            }
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
            using var cts = new CancellationTokenSource(WeatherTimeout);
            double? lat = course.Latitude, lon = course.Longitude;
            if (lat is null || lon is null)
            {
                var point = await location.GetCoarseLocationAsync(cts.Token);
                if (point is null)
                    return;
                (lat, lon) = (point.Latitude, point.Longitude);
            }

            var weather = await api.WeatherGetAsync(lat, lon, startedAt, cts.Token);
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
