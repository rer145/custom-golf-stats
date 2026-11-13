using System.Globalization;
using CustomGolfStats.Api.Client;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Storage.Repositories;
using CustomGolfStats.App.Core.Storage.Rows;

namespace CustomGolfStats.App.Core.Sync;

/// <summary>
/// Push then pull. Order: pending local courses (POST), dirty rounds (PUT) and tombstones (DELETE), then
/// <c>GET /sync/changes</c>. One sync at a time: a call made while one is running joins that run and gets its result. The run
/// uses an engine-owned cancellation token (cancelled only by <see cref="Dispose"/>), so a caller's token only stops that caller's
/// wait, never the run another caller joined.
/// Nothing here discards unsent local work: server copies are written through <see cref="ServerWriteMode"/>, a failed push leaves
/// the round dirty, and the pull cursor is stored only after the whole pull was applied.
/// </summary>
public sealed class SyncEngine(LocalDb db, ICustomGolfStatsApiClient api) : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Task<SyncResult>? _running;
    private CancellationTokenSource? _runCancellation;

    /// <param name="ct">Stops only this caller's wait; the run itself carries on for any other caller.</param>
    public Task<SyncResult> SyncAsync(CancellationToken ct = default)
    {
        Task<SyncResult> run;
        lock (_gate)
        {
            if (_running is not { IsCompleted: false })
            {
                _runCancellation?.Dispose();
                _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                _running = RunAsync(_runCancellation.Token);
            }
            run = _running;
        }
        return run.WaitAsync(ct);
    }

    /// <summary>Stops a run in progress and waits until it has ended (its result says SignedOut). Call before the stored session
    /// changes (signing out, or a new sign-in saving tokens) so a request in flight cannot carry one account's data to another.
    /// Unsent work stays dirty; a later <see cref="SyncAsync"/> starts a fresh run.</summary>
    public async Task StopAsync()
    {
        Task<SyncResult>? run;
        lock (_gate)
        {
            run = _running;
            if (run is not { IsCompleted: false })
                return; // nothing running; a finished run's outcome (even a fault) belongs to its own callers
            _runCancellation?.Cancel();
        }
        try
        {
            await run.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The run's callers see its outcome; stopping only needs it to have ended.
        }
    }

    /// <summary>Cancels a run in progress (app shutdown). Unsent work stays dirty.</summary>
    public void Dispose() => _lifetime.Cancel();

    private async Task<SyncResult> RunAsync(CancellationToken ct)
    {
        var run = new Run();
        try
        {
            // Nothing is sent or received unless the signed-in account was verified for the stored session: not before the first
            // verified sign-in, not while a sign-in is between saving its tokens and checking the account, and not while a different
            // account's sign-in waits on the golfer's decision about this device's unsynced data.
            if (!await AccountSwitchService.IsSyncAllowedAsync(db).ConfigureAwait(false))
            {
                run.SignedOut = true;
                return run.ToResult();
            }
            // The account this run works for. Before every request (and before storing what a pull returned) the session is checked
            // again: if a sign-in or sign-out started meanwhile, the run stops as SignedOut and nothing more is sent or stored.
            run.Account = await db.SyncState.GetAsync(AccountSwitchService.AccountKey).ConfigureAwait(false);

            var pendingCourses = await PushCoursesAsync(run, ct).ConfigureAwait(false);
            if (run.Stop)
                return run.ToResult();
            await PushRoundsAsync(run, pendingCourses, ct).ConfigureAwait(false);
            if (run.Stop)
                return run.ToResult();
            await PullAsync(run, ct).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.StatusCode == 401)
        {
            run.SignedOut = true;
        }
        catch (Exception ex) when (IsNetworkFailure(ex, ct))
        {
            run.Offline = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            run.SignedOut = true; // stopped because the session is changing (or the app is closing)
        }
        return run.ToResult();
    }

    /// <summary>False (and the run marked SignedOut) when the run was stopped or the session it started with is no longer the
    /// verified one: a sign-in has begun, a switch question is open, or the account changed.</summary>
    private async Task<bool> SameSessionAsync(Run run, CancellationToken ct)
    {
        if (!ct.IsCancellationRequested
            && await AccountSwitchService.IsSyncAllowedAsync(db).ConfigureAwait(false)
            && await db.SyncState.GetAsync(AccountSwitchService.AccountKey).ConfigureAwait(false) == run.Account)
            return true;
        run.SignedOut = true;
        return false;
    }

    // ---- push ----

    /// <summary>Creates pending local courses with their client ids. 200 and 201 both mean the course exists (a retry of the same
    /// id by the same creator returns 200). Returns the ids still pending.</summary>
    private async Task<HashSet<Guid>> PushCoursesAsync(Run run, CancellationToken ct)
    {
        var stillPending = new HashSet<Guid>();
        foreach (var course in await db.Courses.GetPendingAsync())
        {
            if (run.Stop || !await SameSessionAsync(run, ct).ConfigureAwait(false))
            {
                stillPending.Add(course.Course.Id);
                continue;
            }
            try
            {
                await api.CoursesCreateAsync(ApiMapper.ToCreateRequest(course), ct).ConfigureAwait(false);
                await db.Courses.MarkSyncedAsync(course.Course.Id).ConfigureAwait(false);
                run.PushedCourses++;
            }
            catch (ApiException ex) when (ex.StatusCode == 401)
            {
                run.SignedOut = true;
                stillPending.Add(course.Course.Id);
            }
            catch (ApiException ex)
            {
                run.Failures.Add($"POST /courses {course.Course.Id}: HTTP {ex.StatusCode}");
                if (IsPermanentRejection(ex.StatusCode))
                    run.Notices.Add(new SyncNotice(SyncNoticeKind.CouldNotSync, null,
                        $"The course \"{course.Course.Name}\" could not be saved to your account (the server rejected it, HTTP {ex.StatusCode}).") { CourseId = course.Course.Id });
                stillPending.Add(course.Course.Id);
            }
            catch (Exception ex) when (IsNetworkFailure(ex, ct))
            {
                run.Offline = true;
                stillPending.Add(course.Course.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad item must not stop the rest from syncing: report it and carry on.
                run.Failures.Add($"POST /courses {course.Course.Id}: unexpected error");
                run.Notices.Add(new SyncNotice(SyncNoticeKind.CouldNotSync, null,
                    $"The course \"{course.Course.Name}\" could not be saved to your account because of an unexpected error. It is still saved on this device.") { CourseId = course.Course.Id });
                stillPending.Add(course.Course.Id);
            }
        }
        return stillPending;
    }

    private async Task PushRoundsAsync(Run run, HashSet<Guid> pendingCourses, CancellationToken ct)
    {
        foreach (var round in await db.Rounds.GetDirtyAsync())
        {
            if (run.Stop || !await SameSessionAsync(run, ct).ConfigureAwait(false))
                return;
            var id = round.Round.Id;
            try
            {
                if (round.Round.DeletedAt is not null)
                    await PushDeleteAsync(run, id, ct).ConfigureAwait(false);
                else if (pendingCourses.Contains(round.Round.CourseId))
                    run.DeferredRounds++; // the server does not know the course yet
                else
                    await PushUpsertAsync(run, round, ct).ConfigureAwait(false);
            }
            catch (ApiException ex) when (ex.StatusCode == 401)
            {
                run.SignedOut = true;
            }
            catch (ApiException ex)
            {
                run.Failures.Add($"round {id}: HTTP {ex.StatusCode}");
                if (IsPermanentRejection(ex.StatusCode))
                    run.Notices.Add(new SyncNotice(SyncNoticeKind.CouldNotSync, id,
                        $"The round at {round.Round.CourseName} on {round.Round.PlayedOn:yyyy-MM-dd} could not be synced (the server rejected it, HTTP {ex.StatusCode}). It is still saved on this device."));
            }
            catch (Exception ex) when (IsNetworkFailure(ex, ct))
            {
                run.Offline = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Includes a server copy that cannot be mapped or stored. The round stays dirty; the others still sync.
                run.Failures.Add($"round {id}: unexpected error");
                run.Notices.Add(new SyncNotice(SyncNoticeKind.CouldNotSync, id,
                    $"The round at {round.Round.CourseName} on {round.Round.PlayedOn:yyyy-MM-dd} could not be synced because of an unexpected error. It is still saved on this device."));
            }
        }
    }

    private async Task PushDeleteAsync(Run run, Guid id, CancellationToken ct)
    {
        try
        {
            await api.RoundsDeleteAsync(id, ct).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            // already gone on the server: the goal is met
        }
        await db.Rounds.PurgeTombstoneAsync(id).ConfigureAwait(false);
        run.PushedDeletes++;
    }

    private async Task PushUpsertAsync(Run run, RoundRecord round, CancellationToken ct)
    {
        var pushed = round.Round.ClientUpdatedAt;
        try
        {
            var saved = await api.RoundsUpsertAsync(round.Round.Id, ApiMapper.ToUpsertRequest(round), ct).ConfigureAwait(false);
            await db.Rounds.SaveFromServerAsync(ApiMapper.ToRecord(saved), ServerWriteMode.PushAccepted(pushed)).ConfigureAwait(false);
            run.PushedRounds++;
        }
        catch (ApiException<RoundDto> conflict) when (conflict.StatusCode == 409)
        {
            // The server has a newer version of this round (another device): its copy wins, unless the golfer edited or
            // deleted the round while the PUT was in flight, in which case the local change stays and is pushed next time.
            var serverCopy = conflict.Result;
            if (await db.Rounds.SaveFromServerAsync(ApiMapper.ToRecord(serverCopy), ServerWriteMode.ServerWins(pushed)))
            {
                run.Conflicts++;
                var what = serverCopy.DeletedAt is not null ? "was deleted on another device" : "was updated from another device";
                run.Notices.Add(new SyncNotice(SyncNoticeKind.UpdatedFromAnotherDevice, round.Round.Id,
                    $"The round at {round.Round.CourseName} on {round.Round.PlayedOn:yyyy-MM-dd} {what}."));
            }
        }
    }

    // ---- pull ----

    private async Task PullAsync(Run run, CancellationToken ct)
    {
        try
        {
            if (!await SameSessionAsync(run, ct).ConfigureAwait(false))
                return;
            var since = await db.SyncState.GetNextSinceAsync().ConfigureAwait(false);
            var changes = await api.SyncChangesAsync(since?.ToString("O", CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
            if (!await SameSessionAsync(run, ct).ConfigureAwait(false))
                return; // the answer may belong to another account's session: store none of it

            // A record that cannot be mapped or stored is skipped and reported; one bad record must not block sync forever.
            // Courses first so rounds always find their course.
            foreach (var dto in changes.Courses ?? [])
            {
                if (TryMap(run, $"course {dto.Id}", () => ApiMapper.ToRecord(dto), out var course)
                    && await TryStoreAsync(run, $"course {dto.Id}", () => db.Courses.SaveFromServerAsync(course)).ConfigureAwait(false))
                    run.PulledCourses++;
            }

            var handicap = new List<HandicapRow>();
            foreach (var dto in changes.HandicapEntries ?? [])
                if (TryMap(run, $"handicap entry {dto.Id}", () => ApiMapper.ToRow(dto), out var row))
                    handicap.Add(row);
            foreach (var row in handicap)
                if (await TryStoreAsync(run, $"handicap entry {row.Id}", () => db.Handicap.UpsertAsync([row])).ConfigureAwait(false))
                    run.PulledHandicap++;

            // The server is the source of truth for entitlements. After an offline trial round that has not been pushed yet, this
            // can restore a higher trial count than the local decrement left; spec section 2 tolerates that.
            if (changes.Entitlements is { Tier: not null, Role: not null } entitlements
                && TryMap(run, "entitlements", () => ApiMapper.ToRow(entitlements), out var entitlementsRow))
                await TryStoreAsync(run, "entitlements", () => db.Entitlements.SaveAsync(entitlementsRow)).ConfigureAwait(false);

            foreach (var dto in changes.Rounds ?? [])
            {
                if (!TryMap(run, $"round {dto.Id}", () => ApiMapper.ToRecord(dto), out var record))
                    continue;
                var outcome = PullOutcome.Failed;
                await TryStoreAsync(run, $"round {dto.Id}", async () =>
                {
                    var local = await db.Connection.FindAsync<RoundRow>(dto.Id).ConfigureAwait(false);
                    if (local is not null && local.Version > dto.Version)
                        outcome = PullOutcome.Skipped; // an older copy of what we already hold
                    else
                        outcome = await db.Rounds.SaveFromServerAsync(record, ServerWriteMode.Pull).ConfigureAwait(false) ? PullOutcome.Saved : PullOutcome.Skipped;
                }).ConfigureAwait(false);
                if (outcome == PullOutcome.Saved)
                    run.PulledRounds++;
                else if (outcome == PullOutcome.Skipped)
                    run.SkippedPulled++;
            }

            // Only now, with everything applied (or reported), move the cursor.
            await db.SyncState.SetNextSinceAsync(changes.NextSince).ConfigureAwait(false);
            run.PullCompleted = true;
        }
        catch (ApiException ex) when (ex.StatusCode == 401)
        {
            run.SignedOut = true;
        }
        catch (ApiException ex)
        {
            run.Failures.Add($"GET /sync/changes: HTTP {ex.StatusCode}");
        }
    }

    private static bool TryMap<T>(Run run, string what, Func<T> map, out T value)
    {
        try
        {
            value = map();
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Failures.Add($"pulled {what} could not be read and was skipped");
            value = default!;
            return false;
        }
    }

    private enum PullOutcome { Failed, Skipped, Saved }

    /// <summary>Stores one pulled record. A storage failure (a constraint the record breaks, for example) is reported and skipped like
    /// a record that cannot be mapped; cancellation still propagates.</summary>
    private static async Task<bool> TryStoreAsync(Run run, string what, Func<Task> store)
    {
        try
        {
            await store().ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Failures.Add($"pulled {what} could not be stored and was skipped");
            return false;
        }
    }

    /// <summary>A 4xx that retrying will not fix (not auth, timeout or rate limiting).</summary>
    private static bool IsPermanentRejection(int status) => status is >= 400 and < 500 and not (401 or 408 or 429);

    private static bool IsNetworkFailure(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or IOException || (ex is TaskCanceledException && !ct.IsCancellationRequested);

    private sealed class Run
    {
        public int PushedRounds, PushedDeletes, PushedCourses, Conflicts, PulledRounds, SkippedPulled, PulledCourses, PulledHandicap, DeferredRounds;
        public bool PullCompleted, Offline, SignedOut;
        public string? Account;
        public List<SyncNotice> Notices { get; } = [];
        public List<string> Failures { get; } = [];

        public bool Stop => Offline || SignedOut;

        public SyncResult ToResult() => new()
        {
            PushedRounds = PushedRounds, PushedDeletes = PushedDeletes, PushedCourses = PushedCourses, Conflicts = Conflicts,
            PulledRounds = PulledRounds, SkippedPulledRounds = SkippedPulled, PulledCourses = PulledCourses,
            PulledHandicapEntries = PulledHandicap, DeferredRounds = DeferredRounds, PullCompleted = PullCompleted,
            Offline = Offline, SignedOut = SignedOut, Notices = Notices, Failures = Failures,
        };
    }
}
