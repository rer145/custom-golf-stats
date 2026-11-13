using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomGolfStats.App.Core.Platform;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.Domain;
using CustomGolfStats.Domain.Holes;

namespace CustomGolfStats.App.Core.ViewModels;

/// <summary>One playing of a hole in the round, in entry order.</summary>
public sealed record HoleListItem(int EntrySequence, int HoleNumber, int Par, int Score, int ToPar, int Putts, bool Gir, bool FairwayHit,
    bool IsDerived, int ShotCount)
{
    public string ToParText => UiText.ToPar(ToPar);
}

/// <summary>
/// The round in progress: holes in the order they were played (any number of holes, any order, the same hole more than once),
/// the running score, and finishing with the optional extras (duration, steps, distance). Finishing never needs a connection and a
/// missing sensor never stops it; a round with no holes cannot be finished.
/// </summary>
public sealed partial class RoundViewModel(LocalDb db, IClock clock, INavigator navigator, IPedometer pedometer) : ViewModelBase
{
    private Guid _roundId;

    public ObservableCollection<HoleListItem> Holes { get; } = [];

    /// <summary>The holes of the tee, for choosing which hole to enter next (any hole, in any order).</summary>
    public IReadOnlyList<HoleInfo> HoleOptions { get; private set; } = [];

    /// <summary>How long the pedometer may take when finishing.</summary>
    public TimeSpan PedometerTimeout { get; set; } = TimeSpan.FromSeconds(3);

    [ObservableProperty] private string _courseName = "";
    [ObservableProperty] private string _teeName = "";
    [ObservableProperty] private DateTime _playedOn;
    [ObservableProperty] private EntryMode _entryMode;
    [ObservableProperty] private bool _isShotByShot;
    [ObservableProperty] private int _holesPlayed;
    [ObservableProperty] private int _totalScore;
    [ObservableProperty] private int _totalPar;
    [ObservableProperty] private int _toPar;
    [ObservableProperty] private string _toParText = UiText.ToPar(0);
    [ObservableProperty] private int _totalPutts;
    [ObservableProperty] private bool _isFinished;

    /// <summary>The hole the next "add hole" would open (the one after the last entered), or null if the tee has no holes.</summary>
    [ObservableProperty] private int? _suggestedHoleNumber;

    [ObservableProperty] private bool _canFinish;

    public async Task LoadAsync(RoundArgs args)
    {
        _roundId = args.RoundId;
        await ReloadAsync();
    }

    /// <summary>Reads the round again (call when returning from a hole screen).</summary>
    public async Task ReloadAsync()
    {
        var ctx = await RoundContext.LoadAsync(db, _roundId);
        if (ctx is null)
        {
            ErrorMessage = UiText.RoundNotFound;
            return;
        }

        var round = ctx.Record.Round;
        CourseName = round.CourseName;
        TeeName = round.TeeName;
        PlayedOn = round.PlayedOn;
        EntryMode = round.EntryMode;
        IsShotByShot = round.EntryMode == EntryMode.ShotByShot;
        IsFinished = round.FinishedAt is not null;
        HoleOptions = ctx.Layout;
        OnPropertyChanged(nameof(HoleOptions));

        Holes.Clear();
        foreach (var hole in ctx.Record.Holes.OrderBy(h => h.Hole.EntrySequence))
        {
            var s = hole.ToSummary();
            Holes.Add(new HoleListItem(s.EntrySequence, s.HoleNumber, s.Par, s.Score, s.ToPar, s.Putts, s.Gir, s.FairwayHit, hole.Hole.IsDerived, hole.Shots.Count));
        }

        var data = ctx.Record.ToRoundData();
        HolesPlayed = Holes.Count;
        TotalScore = data.Score;
        TotalPar = data.Par;
        ToPar = data.ToPar;
        ToParText = UiText.ToPar(data.ToPar);
        TotalPutts = data.Putts;
        SuggestedHoleNumber = ctx.SuggestNextHole();
        CanFinish = HolesPlayed > 0 && !IsFinished;
        FinishRoundCommand.NotifyCanExecuteChanged();
        ErrorMessage = null;
    }

    /// <summary>Opens a new playing of <paramref name="holeNumber"/> (null: the suggested next hole) in the entry screen for this round's mode.</summary>
    [RelayCommand]
    private Task AddHoleAsync(int? holeNumber) =>
        navigator.NavigateAsync(IsShotByShot ? Route.ShotEntry : Route.QuickHole, new HoleArgs(_roundId, HoleNumber: holeNumber ?? SuggestedHoleNumber));

    [RelayCommand]
    private Task EditHoleAsync(HoleListItem hole) =>
        navigator.NavigateAsync(IsShotByShot ? Route.ShotEntry : Route.QuickHole, new HoleArgs(_roundId, EntrySequence: hole.EntrySequence));

    [RelayCommand]
    private async Task DeleteHoleAsync(HoleListItem hole)
    {
        await db.Rounds.DeleteHoleAsync(_roundId, hole.EntrySequence);
        await ReloadAsync();
    }

    /// <summary>Finishes the round: end time, duration from start to finish, and steps and distance from the pedometer. Anything
    /// already entered by hand is kept; a sensor that is missing, denied or slow is skipped.</summary>
    [RelayCommand(CanExecute = nameof(CanFinish))]
    private async Task FinishRoundAsync()
    {
        if (HolesPlayed == 0)
        {
            ErrorMessage = UiText.CannotFinishEmptyRound;
            return;
        }
        var finishedAt = clock.UtcNow;
        var current = await db.Rounds.GetAsync(_roundId);
        if (current is null)
        {
            ErrorMessage = UiText.RoundNotFound;
            return;
        }

        PedometerReading? reading = null;
        if (current.Round.StartedAt is { } startedAt)
        {
            try
            {
                using var cts = new CancellationTokenSource(PedometerTimeout);
                reading = await pedometer.ReadAsync(startedAt, finishedAt, cts.Token);
            }
            catch (Exception)
            {
                // Optional: a round never fails because a sensor is unavailable.
            }
        }

        await db.Rounds.UpdateHeaderAsync(_roundId, r =>
        {
            r.FinishedAt ??= finishedAt;
            if (r.DurationMinutes is null && r.StartedAt is { } start)
                r.DurationMinutes = Math.Max(0, (int)Math.Round((finishedAt - start).TotalMinutes));
            if (reading is not null)
            {
                r.StepCount ??= reading.Steps;
                r.DistanceMiles ??= reading.DistanceMiles;
            }
        });
        await ReloadAsync();
        await navigator.NavigateAsync(Route.RoundSummary, new RoundArgs(_roundId));
    }

    [RelayCommand]
    private Task OpenSummaryAsync() => navigator.NavigateAsync(Route.RoundSummary, new RoundArgs(_roundId));
}
