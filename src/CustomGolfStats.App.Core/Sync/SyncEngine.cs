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

    /// <param name="ct">Stops only this caller's wait; the run itself carries on for any other caller.</param>
    public Task<SyncResult> SyncAsync(CancellationToken ct = default)
    {
        Task<SyncResult> run;
        lock (_gate)
        {
            if (_running is not { IsCompleted: false })
                _running = RunAsync(_lifetime.Token);
            run = _running;
        }
        return run.WaitAsync(ct);
    }

    /// <summary>Cancels a run in progress (app shutdown). Unsent work stays dirty.</summary>
    public void Dispose() => _lifetime.Cancel();

    private async Task<SyncResult> RunAsync(CancellationToken ct)
    {
        var run = new Run();
        try
        {
            var pendingCourses = await PushCoursesAsync(run, ct);
            if (run.Stop)
                return run.ToResult();
            await PushRoundsAsync(run, pendingCourses, ct);
            if (run.Stop)
                return run.ToResult();
            await PullAsync(run, ct);
        }
        catch (ApiException ex) when (ex.StatusCode == 401)
        {
            run.SignedOut = true;
        }
        catch (Exception ex) when (IsNetworkFailure(ex, ct))
        {
            run.Offline = true;
        }
        return run.ToResult();
    }

    // ---- push ----

    /// <summary>Creates pending local courses with their client ids. 200 and 201 both mean the course exists (a retry of the same
    /// id by the same creator returns 200). Returns the ids still pending.</summary>
    private async Task<HashSet<Guid>> PushCoursesAsync(Run run, CancellationToken ct)
    {
        var stillPending = new HashSet<Guid>();
        foreach (var course in await db.Courses.GetPendingAsync())
        {
            if (run.Stop)
            {
                stillPending.Add(course.Course.Id);
                continue;
            }
            try
            {
                await api.CoursesCreateAsync(ApiMapper.ToCreateRequest(course), ct);
                await db.Courses.MarkSyncedAsync(course.Course.Id);
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
        }
        return stillPending;
    }

    private async Task PushRoundsAsync(Run run, HashSet<Guid> pendingCourses, CancellationToken ct)
    {
        foreach (var round in await db.Rounds.GetDirtyAsync())
        {
            if (run.Stop)
                return;
            var id = round.Round.Id;
            try
            {
                if (round.Round.DeletedAt is not null)
                    await PushDeleteAsync(run, id, ct);
                else if (pendingCourses.Contains(round.Round.CourseId))
                    run.DeferredRounds++; // the server does not know the course yet
                else
                    await PushUpsertAsync(run, round, ct);
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
        }
    }

    private async Task PushDeleteAsync(Run run, Guid id, CancellationToken ct)
    {
        try
        {
            await api.RoundsDeleteAsync(id, ct);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            // already gone on the server: the goal is met
        }
        await db.Rounds.PurgeTombstoneAsync(id);
        run.PushedDeletes++;
    }

    private async Task PushUpsertAsync(Run run, RoundRecord round, CancellationToken ct)
    {
        var pushed = round.Round.ClientUpdatedAt;
        try
        {
            var saved = await api.RoundsUpsertAsync(round.Round.Id, ApiMapper.ToUpsertRequest(round), ct);
            await db.Rounds.SaveFromServerAsync(ApiMapper.ToRecord(saved), ServerWriteMode.PushAccepted(pushed));
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
            var since = await db.SyncState.GetNextSinceAsync();
            var changes = await api.SyncChangesAsync(since?.ToString("O", CultureInfo.InvariantCulture), ct);

            // A record that cannot be mapped is skipped and reported; one bad record must not block sync forever.
            // Courses first so rounds always find their course.
            foreach (var dto in changes.Courses ?? [])
            {
                if (!TryMap(run, $"course {dto.Id}", () => ApiMapper.ToRecord(dto), out var course))
                    continue;
                await db.Courses.SaveFromServerAsync(course);
                run.PulledCourses++;
            }

            var handicap = new List<HandicapRow>();
            foreach (var dto in changes.HandicapEntries ?? [])
                if (TryMap(run, $"handicap entry {dto.Id}", () => ApiMapper.ToRow(dto), out var row))
                    handicap.Add(row);
            await db.Handicap.UpsertAsync(handicap);
            run.PulledHandicap += handicap.Count;

            // The server is the source of truth for entitlements. After an offline trial round that has not been pushed yet, this
            // can restore a higher trial count than the local decrement left; spec section 2 tolerates that.
            if (changes.Entitlements is { Tier: not null, Role: not null } entitlements
                && TryMap(run, "entitlements", () => ApiMapper.ToRow(entitlements), out var entitlementsRow))
                await db.Entitlements.SaveAsync(entitlementsRow);

            foreach (var dto in changes.Rounds ?? [])
            {
                if (!TryMap(run, $"round {dto.Id}", () => ApiMapper.ToRecord(dto), out var record))
                    continue;
                var local = await db.Connection.FindAsync<RoundRow>(dto.Id);
                if (local is not null && local.Version > dto.Version)
                {
                    run.SkippedPulled++; // an older copy of what we already hold
                    continue;
                }
                if (await db.Rounds.SaveFromServerAsync(record, ServerWriteMode.Pull))
                    run.PulledRounds++;
                else
                    run.SkippedPulled++;
            }

            // Only now, with everything applied (or reported), move the cursor.
            await db.SyncState.SetNextSinceAsync(changes.NextSince);
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

    /// <summary>A 4xx that retrying will not fix (not auth, timeout or rate limiting).</summary>
    private static bool IsPermanentRejection(int status) => status is >= 400 and < 500 and not (401 or 408 or 429);

    private static bool IsNetworkFailure(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or IOException || (ex is TaskCanceledException && !ct.IsCancellationRequested);

    private sealed class Run
    {
        public int PushedRounds, PushedDeletes, PushedCourses, Conflicts, PulledRounds, SkippedPulled, PulledCourses, PulledHandicap, DeferredRounds;
        public bool PullCompleted, Offline, SignedOut;
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
