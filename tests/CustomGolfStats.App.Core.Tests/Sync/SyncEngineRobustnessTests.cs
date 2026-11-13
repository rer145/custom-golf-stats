using System.Net;
using CustomGolfStats.Api.Client;
using CustomGolfStats.App.Core.Storage.Repositories;
using CustomGolfStats.App.Core.Sync;
using CustomGolfStats.App.Core.Tests.Support;

namespace CustomGolfStats.App.Core.Tests.Sync;

public class SyncEngineRobustnessTests
{
    // ---- a 409 must not clobber what the golfer did while the PUT was in flight ----

    [Fact]
    public async Task An_edit_made_during_a_409_PUT_stays_dirty_and_takes_only_the_server_owned_fields()
    {
        await using var rig = await SyncRig.CreateAsync();
        var round = Samples.FullShotRound();
        await rig.Temp.Db.Rounds.SaveAsync(round);
        rig.PutRound = r =>
        {
            rig.Temp.Clock.Advance(TimeSpan.FromSeconds(30));
            rig.Temp.Db.Rounds.SaveHoleAsync(round.Round.Id, Samples.QuickHole(round.Round.Id, 3, 3, 3, 2)).GetAwaiter().GetResult();
            return StubHandler.Json(HttpStatusCode.Conflict, Payloads.Round(SyncRig.IdOf(r), 6, "edited elsewhere"));
        };

        var result = await rig.SyncAsync();

        Assert.Equal(0, result.Conflicts);
        Assert.Empty(result.Notices);
        var loaded = (await rig.Temp.Db.Rounds.GetAsync(round.Round.Id))!;
        Assert.True(loaded.Round.Dirty);
        Assert.Equal("windy", loaded.Round.Notes);   // local content kept
        Assert.Equal(3, loaded.Holes.Count);         // including the hole added during the push
        Assert.Equal(6, loaded.Round.Version);       // server-owned fields recorded
    }

    [Fact]
    public async Task A_delete_made_during_a_409_PUT_stays_a_pending_delete_and_goes_out_next_sync()
    {
        await using var rig = await SyncRig.CreateAsync();
        var round = Samples.FullShotRound();
        await rig.Temp.Db.Rounds.SaveAsync(round);
        var first = true;
        rig.PutRound = r =>
        {
            if (first)
            {
                first = false;
                rig.Temp.Clock.Advance(TimeSpan.FromSeconds(5));
                rig.Temp.Db.Rounds.MarkDeletedAsync(round.Round.Id).GetAwaiter().GetResult();
            }
            return StubHandler.Json(HttpStatusCode.Conflict, Payloads.Round(SyncRig.IdOf(r), 6, "edited elsewhere"));
        };

        var result = await rig.SyncAsync();

        Assert.Empty(result.Notices);
        Assert.Null(await rig.Temp.Db.Rounds.GetAsync(round.Round.Id));
        Assert.True((await rig.Temp.Db.Rounds.GetAsync(round.Round.Id, includeDeleted: true))!.Round.Dirty);

        var second = await rig.SyncAsync();
        Assert.Equal(1, second.PushedDeletes);
        Assert.Null(await rig.Temp.Db.Rounds.GetAsync(round.Round.Id, includeDeleted: true));
    }

    [Fact]
    public async Task A_409_whose_copy_is_deleted_says_so()
    {
        await using var rig = await SyncRig.CreateAsync();
        var round = Samples.FullShotRound();
        await rig.Temp.Db.Rounds.SaveAsync(round);
        rig.PutRound = r => StubHandler.Json(HttpStatusCode.Conflict,
            Payloads.Round(SyncRig.IdOf(r), 7, deletedAt: new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero)));

        var result = await rig.SyncAsync();

        var notice = Assert.Single(result.Notices);
        Assert.Contains("was deleted on another device", notice.Message);
        Assert.Null(await rig.Temp.Db.Rounds.GetAsync(round.Round.Id));
        Assert.Equal(0, await rig.Temp.Db.Rounds.CountDirtyAsync());
    }

    [Fact]
    public async Task A_guarded_ServerWins_does_not_write_over_an_edited_or_missing_round()
    {
        await using var rig = await SyncRig.CreateAsync();
        var round = Samples.FullShotRound();
        await rig.Temp.Db.Rounds.SaveAsync(round);
        var pushed = rig.Temp.Clock.UtcNow;
        var copy = new Storage.RoundRecord(round.Round.Clone(), []);
        copy.Round.Version = 4;

        Assert.False(await rig.Temp.Db.Rounds.SaveFromServerAsync(copy, ServerWriteMode.ServerWins(pushed.AddSeconds(-1)))); // edited since
        Assert.Equal(2, (await rig.Temp.Db.Rounds.GetAsync(round.Round.Id))!.Holes.Count);
        Assert.False(await rig.Temp.Db.Rounds.SaveFromServerAsync(new Storage.RoundRecord(Samples.Round(), []), ServerWriteMode.ServerWins(pushed)));
        Assert.True(await rig.Temp.Db.Rounds.SaveFromServerAsync(copy, ServerWriteMode.ServerWins(pushed)));
        Assert.Empty((await rig.Temp.Db.Rounds.GetAsync(round.Round.Id))!.Holes);
    }

    // ---- permanent rejections reach the UI ----

    [Fact]
    public async Task A_permanently_rejected_round_raises_a_could_not_sync_notice_and_stays_dirty()
    {
        await using var rig = await SyncRig.CreateAsync();
        var round = Samples.FullShotRound();
        await rig.Temp.Db.Rounds.SaveAsync(round);
        rig.PutRound = _ => StubHandler.Status(HttpStatusCode.BadRequest);

        var result = await rig.SyncAsync();

        var notice = Assert.Single(result.Notices);
        Assert.Equal(SyncNoticeKind.CouldNotSync, notice.Kind);
        Assert.Equal(round.Round.Id, notice.RoundId);
        Assert.Contains("400", notice.Message);
        Assert.Equal(1, await rig.Temp.Db.Rounds.CountDirtyAsync());
    }

    [Fact]
    public async Task A_server_error_is_a_failure_but_not_a_notice_because_a_retry_may_work()
    {
        await using var rig = await SyncRig.CreateAsync();
        await rig.Temp.Db.Rounds.SaveAsync(Samples.FullShotRound());
        rig.PutRound = _ => StubHandler.Status(HttpStatusCode.InternalServerError);

        var result = await rig.SyncAsync();

        Assert.Single(result.Failures);
        Assert.Empty(result.Notices);
    }

    [Fact]
    public async Task A_course_the_server_rejects_raises_a_could_not_sync_notice_with_the_course_id()
    {
        await using var rig = await SyncRig.CreateAsync();
        await rig.Temp.Db.Courses.SaveLocalAsync(Samples.Course("My Track"));
        await rig.Temp.Db.Rounds.SaveAsync(Samples.FullShotRound());
        rig.PostCourse = _ => StubHandler.Status(HttpStatusCode.Conflict);

        var result = await rig.SyncAsync();

        var notice = Assert.Single(result.Notices);
        Assert.Equal(SyncNoticeKind.CouldNotSync, notice.Kind);
        Assert.Equal(Samples.CourseId, notice.CourseId);
        Assert.Contains("My Track", notice.Message);
        Assert.Equal(1, result.DeferredRounds);
        Assert.Single(await rig.Temp.Db.Courses.GetPendingAsync());
    }

    // ---- one run, many callers ----

    [Fact]
    public async Task A_callers_cancellation_stops_only_its_own_wait_not_the_run_another_caller_joined()
    {
        await using var rig = await SyncRig.CreateAsync();
        await rig.Temp.Db.Rounds.SaveAsync(Samples.FullShotRound());
        using var release = new SemaphoreSlim(0);
        rig.PutRound = r =>
        {
            release.Wait(TimeSpan.FromSeconds(10));
            return StubHandler.Json(HttpStatusCode.OK, Payloads.Round(SyncRig.IdOf(r), 1, "pushed"));
        };
        using var cancelFirst = new CancellationTokenSource();

        var first = Task.Run(() => rig.Engine.SyncAsync(cancelFirst.Token));
        while (!rig.Server.Requests.Any(r => r.Method == HttpMethod.Put))
            await Task.Delay(10, TestContext.Current.CancellationToken);
        var second = rig.Engine.SyncAsync(TestContext.Current.CancellationToken);
        await cancelFirst.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        release.Release();

        var result = await second;

        Assert.Equal(1, result.PushedRounds);
        Assert.True(result.PullCompleted);
        Assert.Equal(0, await rig.Temp.Db.Rounds.CountDirtyAsync());
    }

    // ---- a bad record must not block sync forever ----

    [Fact]
    public async Task A_pulled_record_that_cannot_be_mapped_is_skipped_reported_and_the_cursor_still_advances()
    {
        await using var rig = await SyncRig.CreateAsync();
        var goodId = Guid.NewGuid();
        var changes = Payloads.Changes(SyncRig.Cursor1, Payloads.Round(goodId, 2));
        changes.Entitlements.Tier = "Platinum"; // not a known tier: mapping throws
        rig.Changes = _ => StubHandler.Json(HttpStatusCode.OK, changes);

        var result = await rig.SyncAsync();

        Assert.True(result.PullCompleted);
        Assert.False(result.Succeeded);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("entitlements", failure);
        Assert.Equal(1, result.PulledRounds);
        Assert.NotNull(await rig.Temp.Db.Rounds.GetAsync(goodId));
        Assert.Equal(SyncRig.Cursor1, await rig.Temp.Db.SyncState.GetNextSinceAsync());
    }
}
