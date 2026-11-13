using System.Net;
using CustomGolfStats.Api.Client;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Storage.Repositories;
using CustomGolfStats.App.Core.Tests.Support;
using Dom = CustomGolfStats.Domain;

namespace CustomGolfStats.App.Core.Tests.Sync;

public class SyncEnginePullTests
{
    private static SyncChangesResponse Everything(Guid roundId)
    {
        var changes = Payloads.Changes(SyncRig.Cursor1, Payloads.Round(roundId, 2));
        changes.Courses = [Payloads.Course()];
        changes.HandicapEntries = [new SyncHandicapEntryDto { Id = Guid.Parse("55555555-5555-5555-5555-555555555555"), EffectiveOn = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), Index = 12.4 }];
        return changes;
    }

    [Fact]
    public async Task Pull_stores_rounds_courses_handicap_and_entitlements_and_the_cursor()
    {
        await using var rig = await SyncRig.CreateAsync();
        var roundId = Guid.NewGuid();
        rig.Changes = _ => StubHandler.Json(HttpStatusCode.OK, Everything(roundId));

        var result = await rig.SyncAsync();

        Assert.True(result.PullCompleted);
        Assert.Equal((1, 1, 1), (result.PulledRounds, result.PulledCourses, result.PulledHandicapEntries));
        var round = (await rig.Temp.Db.Rounds.GetAsync(roundId))!;
        Assert.Equal(2, round.Round.Version);
        Assert.False(round.Round.Dirty);
        Assert.NotNull(await rig.Temp.Db.Courses.GetAsync(Samples.CourseId));
        Assert.Equal(12.4, (await rig.Temp.Db.Handicap.LatestAsync())!.Index);
        var e = (await rig.Temp.Db.Entitlements.GetAsync())!;
        Assert.Equal((Dom.Tier.Free, 3, true), (e.Tier, e.TrialShotRoundsRemaining, e.ShotByShotEntry));
        Assert.Equal(SyncRig.Cursor1, await rig.Temp.Db.SyncState.GetNextSinceAsync());
    }

    [Fact]
    public async Task The_first_pull_sends_no_since_and_the_next_sends_the_stored_cursor_back()
    {
        await using var rig = await SyncRig.CreateAsync();

        await rig.SyncAsync();
        await rig.SyncAsync();

        var gets = rig.Server.To("GET", "/sync/changes").ToList();
        Assert.Equal(2, gets.Count);
        Assert.DoesNotContain("since", gets[0].PathAndQuery);
        Assert.Contains("since=", gets[1].PathAndQuery);
        Assert.Contains("2026-06-10T08:00:00", Uri.UnescapeDataString(gets[1].PathAndQuery));
    }

    [Fact]
    public async Task A_failed_pull_does_not_move_the_cursor()
    {
        await using var rig = await SyncRig.CreateAsync();
        await rig.Temp.Db.SyncState.SetNextSinceAsync(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        rig.Changes = _ => StubHandler.Status(HttpStatusCode.InternalServerError);

        var result = await rig.SyncAsync();

        Assert.False(result.PullCompleted);
        Assert.Single(result.Failures);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), await rig.Temp.Db.SyncState.GetNextSinceAsync());
    }

    [Fact]
    public async Task A_garbled_pull_response_does_not_move_the_cursor_or_store_anything()
    {
        await using var rig = await SyncRig.CreateAsync();
        rig.Changes = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json") };

        var result = await rig.SyncAsync();

        Assert.False(result.PullCompleted);
        Assert.Null(await rig.Temp.Db.SyncState.GetNextSinceAsync());
        Assert.Empty(await rig.Temp.Db.Rounds.ListAsync());
    }

    [Fact]
    public async Task A_network_failure_during_the_pull_is_reported_offline_without_a_cursor()
    {
        await using var rig = await SyncRig.CreateAsync();
        rig.Changes = _ => throw new HttpRequestException("offline");

        var result = await rig.SyncAsync();

        Assert.True(result.Offline);
        Assert.Null(await rig.Temp.Db.SyncState.GetNextSinceAsync());
    }

    [Fact]
    public async Task Pulling_the_same_changes_twice_is_harmless()
    {
        await using var rig = await SyncRig.CreateAsync();
        var roundId = Guid.NewGuid();
        rig.Changes = _ => StubHandler.Json(HttpStatusCode.OK, Everything(roundId));

        await rig.SyncAsync();
        await rig.SyncAsync();

        Assert.Single(await rig.Temp.Db.Rounds.ListAsync());
        Assert.Single(await rig.Temp.Db.Handicap.ListAsync());
        Assert.Equal(1, await rig.Temp.Db.Connection.Table<Storage.Rows.CourseRow>().CountAsync());
    }

    [Fact]
    public async Task A_pulled_copy_never_overwrites_a_round_with_unsent_edits()
    {
        await using var rig = await SyncRig.CreateAsync();
        var round = Samples.FullShotRound();
        await rig.Temp.Db.Rounds.SaveAsync(round);
        rig.PutRound = _ => StubHandler.Status(HttpStatusCode.InternalServerError); // the push fails, round stays dirty
        rig.Changes = _ => StubHandler.Json(HttpStatusCode.OK, Payloads.Changes(SyncRig.Cursor1, Payloads.Round(round.Round.Id, 9, "server copy")));

        var result = await rig.SyncAsync();

        Assert.Equal(1, result.SkippedPulledRounds);
        var loaded = (await rig.Temp.Db.Rounds.GetAsync(round.Round.Id))!;
        Assert.True(loaded.Round.Dirty);
        Assert.Equal("windy", loaded.Round.Notes);
        Assert.Equal(2, loaded.Holes.Count);
    }

    [Fact]
    public async Task A_pulled_copy_does_not_resurrect_a_round_with_a_pending_delete()
    {
        await using var rig = await SyncRig.CreateAsync();
        var round = Samples.Synced(Samples.FullShotRound());
        await rig.Temp.Db.Rounds.SaveFromServerAsync(round, ServerWriteMode.Pull);
        await rig.Temp.Db.Rounds.MarkDeletedAsync(round.Round.Id);
        rig.DeleteRound = _ => StubHandler.Status(HttpStatusCode.InternalServerError);
        rig.Changes = _ => StubHandler.Json(HttpStatusCode.OK, Payloads.Changes(SyncRig.Cursor1, Payloads.Round(round.Round.Id, 9)));

        await rig.SyncAsync();

        Assert.Null(await rig.Temp.Db.Rounds.GetAsync(round.Round.Id));
        Assert.True((await rig.Temp.Db.Rounds.GetAsync(round.Round.Id, includeDeleted: true))!.Round.Dirty);
    }

    [Fact]
    public async Task An_older_copy_than_the_stored_one_is_skipped()
    {
        await using var rig = await SyncRig.CreateAsync();
        var round = Samples.Synced(Samples.FullShotRound());
        round.Round.Version = 7;
        round.Round.Notes = "newer";
        await rig.Temp.Db.Rounds.SaveFromServerAsync(round, ServerWriteMode.Pull);
        rig.Changes = _ => StubHandler.Json(HttpStatusCode.OK, Payloads.Changes(SyncRig.Cursor1, Payloads.Round(round.Round.Id, 3, "stale")));

        var result = await rig.SyncAsync();

        Assert.Equal(1, result.SkippedPulledRounds);
        Assert.Equal("newer", (await rig.Temp.Db.Rounds.GetAsync(round.Round.Id))!.Round.Notes);
    }

    [Fact]
    public async Task A_round_deleted_on_another_device_arrives_as_a_clean_tombstone()
    {
        await using var rig = await SyncRig.CreateAsync();
        var round = Samples.Synced(Samples.FullShotRound());
        await rig.Temp.Db.Rounds.SaveFromServerAsync(round, ServerWriteMode.Pull);
        rig.Changes = _ => StubHandler.Json(HttpStatusCode.OK, Payloads.Changes(SyncRig.Cursor1,
            Payloads.Round(round.Round.Id, 8, deletedAt: new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero))));

        await rig.SyncAsync();

        Assert.Empty(await rig.Temp.Db.Rounds.ListAsync());
        Assert.Equal(0, await rig.Temp.Db.Rounds.CountDirtyAsync());
        Assert.Equal(0, await rig.Temp.Db.Connection.Table<Storage.Rows.HoleRow>().CountAsync());
    }

    // ---- Review focus 3: the offline trial ----

    [Fact]
    public async Task Offline_trial_use_is_overwritten_by_the_servers_count_on_the_next_sync()
    {
        await using var rig = await SyncRig.CreateAsync();
        await rig.Temp.Db.Entitlements.SaveAsync(new Storage.Rows.EntitlementsRow { Tier = Dom.Tier.Free, Role = Dom.Role.User, ShotByShotEntry = true, TrialShotRoundsRemaining = 2 });
        Assert.True(await rig.Temp.Db.Entitlements.TryConsumeTrialRoundAsync());
        Assert.Equal(1, (await rig.Temp.Db.Entitlements.GetAsync())!.TrialShotRoundsRemaining);

        // The server, having received the round, says 1 remaining: it is the source of truth.
        rig.Changes = _ =>
        {
            var changes = Payloads.Changes(SyncRig.Cursor1);
            changes.Entitlements.TrialShotRoundsRemaining = 1;
            return StubHandler.Json(HttpStatusCode.OK, changes);
        };
        await rig.SyncAsync();
        Assert.Equal(1, (await rig.Temp.Db.Entitlements.GetAsync())!.TrialShotRoundsRemaining);

        // And if the server still says 2 (the offline round was never counted), the cache follows the server.
        rig.Changes = _ =>
        {
            var changes = Payloads.Changes(SyncRig.Cursor1);
            changes.Entitlements.TrialShotRoundsRemaining = 2;
            return StubHandler.Json(HttpStatusCode.OK, changes);
        };
        await rig.SyncAsync();
        Assert.Equal(2, (await rig.Temp.Db.Entitlements.GetAsync())!.TrialShotRoundsRemaining);
    }

    [Fact]
    public async Task Entitlements_are_cached_so_shot_by_shot_works_offline_and_closes_when_the_trial_is_spent()
    {
        await using var rig = await SyncRig.CreateAsync();
        var changes = Payloads.Changes(SyncRig.Cursor1);
        changes.Entitlements.TrialShotRoundsRemaining = 1;
        rig.Changes = _ => StubHandler.Json(HttpStatusCode.OK, changes);
        await rig.SyncAsync();

        rig.Changes = _ => throw new HttpRequestException("offline");
        var offline = await rig.SyncAsync();

        Assert.True(offline.Offline);
        Assert.True(await rig.Temp.Db.Entitlements.TryConsumeTrialRoundAsync());   // the last trial round, offline
        Assert.False(await rig.Temp.Db.Entitlements.TryConsumeTrialRoundAsync());  // now disabled
        Assert.False((await rig.Temp.Db.Entitlements.GetAsync())!.ShotByShotEntry);
    }

    [Fact]
    public async Task A_response_without_entitlements_leaves_the_cache_alone()
    {
        await using var rig = await SyncRig.CreateAsync();
        await rig.Temp.Db.Entitlements.SaveAsync(new Storage.Rows.EntitlementsRow { Tier = Dom.Tier.Paid, ShotByShotEntry = true });
        rig.Changes = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"serverTime":"2026-06-10T08:00:00Z","nextSince":"2026-06-10T08:00:00Z","rounds":[],"courses":[],"handicapEntries":[]}""", System.Text.Encoding.UTF8, "application/json"),
        };

        var result = await rig.SyncAsync();

        Assert.True(result.PullCompleted);
        Assert.Equal(Dom.Tier.Paid, (await rig.Temp.Db.Entitlements.GetAsync())!.Tier);
    }

    // ---- one at a time ----

    [Fact]
    public async Task Overlapping_syncs_share_one_run()
    {
        await using var rig = await SyncRig.CreateAsync();
        using var release = new SemaphoreSlim(0);
        rig.Changes = _ =>
        {
            release.Wait(TimeSpan.FromSeconds(10));
            return StubHandler.Json(HttpStatusCode.OK, Payloads.Changes(SyncRig.Cursor1));
        };

        var first = rig.SyncAsync();
        while (!rig.Server.Requests.Any(r => r.Path == "/sync/changes"))
            await Task.Delay(10, TestContext.Current.CancellationToken);
        var second = rig.SyncAsync();
        release.Release();

        var results = await Task.WhenAll(first, second);

        Assert.Same(results[0], results[1]);
        Assert.Single(rig.Server.To("GET", "/sync/changes"));
        var third = await rig.SyncAsync(); // a later call starts a fresh run
        Assert.Equal(2, rig.Server.To("GET", "/sync/changes").Count());
        Assert.NotSame(results[0], third);
    }
}
