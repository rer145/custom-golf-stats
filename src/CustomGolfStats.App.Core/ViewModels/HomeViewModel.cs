using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Sync;

namespace CustomGolfStats.App.Core.ViewModels;

public sealed record RoundListItem(Guid Id, string CourseName, DateTime PlayedOn, int Holes, int Score, int ToPar, bool IsFinished, bool IsWaitingToSync)
{
    public string ToParText => UiText.ToPar(ToPar);
}

/// <summary>Recent rounds, sync status and notices, and the way into everything else.</summary>
public sealed partial class HomeViewModel(LocalDb db, SyncEngine sync, INavigator navigator) : ViewModelBase
{
    // Notices already shown whose condition has not cleared: (kind, round, course).
    private readonly HashSet<(SyncNoticeKind, Guid?, Guid?)> _shown = [];

    public ObservableCollection<RoundListItem> Rounds { get; } = [];

    /// <summary>What the golfer should be told about, once each until the condition clears or they dismiss it.</summary>
    public ObservableCollection<SyncNotice> Notices { get; } = [];

    [ObservableProperty] private string _syncStatus = UiText.StatusNeverSynced;
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private int _pendingCount;

    /// <summary>The session ended; show a way back to the sign-in screen.</summary>
    [ObservableProperty] private bool _needsSignIn;

    public async Task LoadAsync()
    {
        Rounds.Clear();
        foreach (var row in await db.Rounds.ListAsync())
        {
            var record = await db.Rounds.GetAsync(row.Id);
            if (record is null)
                continue;
            var data = record.ToRoundData();
            Rounds.Add(new RoundListItem(row.Id, row.CourseName, row.PlayedOn, data.Holes.Count, data.Score, data.ToPar, row.FinishedAt is not null, row.Dirty));
        }
        PendingCount = await db.Rounds.CountDirtyAsync();
        if (!IsSyncing && !NeedsSignIn && SyncStatus is UiText.StatusNeverSynced or UiText.StatusSynced)
            SyncStatus = PendingCount > 0 ? UiText.StatusWaiting(PendingCount) : SyncStatus;
    }

    [RelayCommand]
    private async Task SyncAsync()
    {
        if (IsSyncing)
            return;
        IsSyncing = true;
        SyncStatus = UiText.StatusSyncing;
        try
        {
            var result = await sync.SyncAsync();
            Apply(result);
        }
        catch (OperationCanceledException)
        {
            SyncStatus = UiText.StatusOffline;
        }
        finally
        {
            IsSyncing = false;
        }
        await LoadAsync();
    }

    /// <summary>Shows a sync result: the status line and any new notices.</summary>
    public void Apply(SyncResult result)
    {
        NeedsSignIn = result.SignedOut;
        SyncStatus = result.SignedOut ? UiText.StatusSignedOut
            : result.Offline ? UiText.StatusOffline
            : result.Failures.Count > 0 ? UiText.StatusProblems(result.Failures.Count)
            : result.PullCompleted ? UiText.StatusSynced
            : UiText.StatusOffline;

        var keys = new HashSet<(SyncNoticeKind, Guid?, Guid?)>();
        foreach (var notice in result.Notices)
        {
            var key = (notice.Kind, notice.RoundId, notice.CourseId);
            keys.Add(key);
            if (_shown.Add(key))
                Notices.Add(notice);
        }

        // A sync that stopped early (offline, signed out) says nothing about whether a problem has cleared.
        if (!result.Offline && !result.SignedOut)
            _shown.IntersectWith(keys);
    }

    /// <summary>Hides a notice. It stays suppressed while the same condition keeps being reported.</summary>
    [RelayCommand]
    private void DismissNotice(SyncNotice notice) => Notices.Remove(notice);

    [RelayCommand]
    private Task StartRoundAsync() => navigator.NavigateAsync(Route.CoursePicker);

    [RelayCommand]
    private Task OpenRoundAsync(RoundListItem round) =>
        navigator.NavigateAsync(round.IsFinished ? Route.RoundSummary : Route.Round, new RoundArgs(round.Id));

    [RelayCommand]
    private Task OpenStatsAsync() => navigator.NavigateAsync(Route.OverallStats);

    [RelayCommand]
    private Task OpenProfileAsync() => navigator.NavigateAsync(Route.Profile);

    [RelayCommand]
    private Task SignInAgainAsync() => navigator.NavigateAsync(Route.SignIn);
}
