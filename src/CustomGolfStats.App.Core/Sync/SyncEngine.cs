using System.Globalization;
using CustomGolfStats.Api.Client;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Storage.Repositories;
using CustomGolfStats.App.Core.Storage.Rows;

namespace CustomGolfStats.App.Core.Sync;

/// <summary>
/// Push then pull. Order: pending local courses (POST), dirty rounds (PUT) and tombstones (DELETE), then
/// <c>GET /sync/changes</c>. One sync at a time: a call made while one is running returns that run's result.
/// Nothing here discards unsent local work: server copies are written through <see cref="ServerWriteMode"/>, a failed push leaves
/// the round dirty, and the pull cursor is stored only after the whole pull was applied.
/// </summary>
public sealed class SyncEngine(LocalDb db, ICustomGolfStatsApiClient api)
{
    private readonly object _gate = new();
    private Task<SyncResult>? _running;

    public Task<SyncResult> SyncAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_running is { IsCompleted: false } running)
                return running;
            return _running = RunAsync(ct);
        }
    }

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
            // The server has a newer version of this round (another device): its copy wins.
            await db.Rounds.SaveFromServerAsync(ApiMapper.ToRecord(conflict.Result), ServerWriteMode.ServerWins);
            run.Conflicts++;
            run.Notices.Add(new SyncNotice(SyncNoticeKind.UpdatedFromAnotherDevice, round.Round.Id,
                $"The round at {round.Round.CourseName} on {round.Round.PlayedOn:yyyy-MM-dd} was updated from another device."));
        }
    }

    // ---- pull ----

    private async Task PullAsync(Run run, CancellationToken ct)
    {
        try
        {
            var since = await db.SyncState.GetNextSinceAsync();
            var changes = await api.SyncChangesAsync(since?.ToString("O", CultureInfo.InvariantCulture), ct);

            // Courses first so rounds always find their course.
            foreach (var course in changes.Courses ?? [])
            {
                await db.Courses.SaveFromServerAsync(ApiMapper.ToRecord(course));
                run.PulledCourses++;
            }

            var handicap = (changes.HandicapEntries ?? []).Select(ApiMapper.ToRow).ToList();
            await db.Handicap.UpsertAsync(handicap);
            run.PulledHandicap += handicap.Count;

            if (changes.Entitlements?.Tier is not null && changes.Entitlements.Role is not null)
                await db.Entitlements.SaveAsync(ApiMapper.ToRow(changes.Entitlements));

            foreach (var dto in changes.Rounds ?? [])
            {
                var local = await db.Connection.FindAsync<RoundRow>(dto.Id);
                if (local is not null && local.Version > dto.Version)
                {
                    run.SkippedPulled++; // an older copy of what we already hold
                    continue;
                }
                if (await db.Rounds.SaveFromServerAsync(ApiMapper.ToRecord(dto), ServerWriteMode.Pull))
                    run.PulledRounds++;
                else
                    run.SkippedPulled++;
            }

            // Only now, with everything applied, move the cursor.
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
