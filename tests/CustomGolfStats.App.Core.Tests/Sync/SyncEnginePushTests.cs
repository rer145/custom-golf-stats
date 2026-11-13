using System.Net;
using System.Text.Json;
using CustomGolfStats.Api.Client;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Storage.Repositories;
using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.App.Core.Sync;
using CustomGolfStats.App.Core.Tests.Support;

namespace CustomGolfStats.App.Core.Tests.Sync;

public class SyncEnginePushTests
{
    [Fact]
    public async Task A_dirty_round_is_PUT_with_its_shots_and_stored_clean_from_the_200()
    {
        await using var rig = await SyncRig.CreateAsync();
        var round = Samples.FullShotRound();
        await rig.Temp.Db.Rounds.SaveAsync(round);

        var result = await rig.SyncAsync();

        var put = Assert.Single(rig.Server.To("PUT", "/rounds/"));
        Assert.Equal($"/rounds/{round.Round.Id}", put.Path);
        using var body = JsonDocument.Parse(put.Body!);
        Assert.Equal(rig.Temp.Clock.UtcNow, body.RootElement.GetProperty("clientUpdatedAt").GetDateTimeOffset());
        Assert.Equal(2, body.RootElement.GetProperty("holes").GetArrayLength());
        Assert.Equal(4, body.RootElement.GetProperty("holes")[0].GetProperty("shots").GetArrayLength());
        Assert.Equal(1, result.PushedRounds);
        var stored = (await rig.Temp.Db.Rounds.GetAsync(round.Round.Id))!.Round;
        Assert.False(stored.Dirty);
        Assert.Equal(1, stored.Version);
        Assert.Equal("pushed", stored.Notes); // the server copy was stored
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task A_failed_push_leaves_the_round_dirty_across_a_restart_and_the_rest_still_sync()
    {
        await using var rig = await SyncRig.CreateAsync();
        var bad = Samples.FullShotRound();
        var good = Samples.FullShotRound();
        await rig.Temp.Db.Rounds.SaveAsync(bad);
        rig.Temp.Clock.Advance(TimeSpan.FromSeconds(1));
        await rig.Temp.Db.Rounds.SaveAsync(good);
        rig.PutRound = r => SyncRig.IdOf(r) == bad.Round.Id ? StubHandler.Status(HttpStatusCode.InternalServerError) : StubHandler.Json(HttpStatusCode.OK, Payloads.Round(SyncRig.IdOf(r), 1, "pushed"));

        var result = await rig.SyncAsync();

        Assert.Equal(1, result.PushedRounds);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("500", failure);
        await rig.Temp.ReopenAsync();
        var pending = await rig.Temp.Db.Rounds.GetDirtyAsync();
        Assert.Equal([bad.Round.Id], pending.Select(p => p.Round.Id));
        Assert.Equal(8, pending[0].Holes.Sum(h => h.Shots.Count)); // nothing was dropped
    }

    [Fact]
    public async Task When_the_network_is_down_nothing_is_lost_and_no_pull_is_attempted()
    {
        await using var rig = await SyncRig.CreateAsync();
        var round = Samples.FullShotRound();
        await rig.Temp.Db.Rounds.SaveAsync(round);
        rig.PutRound = _ => throw new HttpRequestException("offline");

        var result = await rig.SyncAsync();

        Assert.True(result.Offline);
        Assert.False(result.PullCompleted);
        Assert.Empty(rig.Server.To("GET", "/sync/changes"));
        Assert.Equal(1, await rig.Temp.Db.Rounds.CountDirtyAsync());
        Assert.Null(await rig.Temp.Db.SyncState.GetNextSinceAsync());
    }

    [Fact]
    public async Task A_409_replaces_the_local_round_with_the_server_copy_and_raises_a_notice()
    {
        await using var rig = await SyncRig.CreateAsync();
        var round = Samples.FullShotRound();
        await rig.Temp.Db.Rounds.SaveAsync(round);
        rig.PutRound = r => StubHandler.Json(HttpStatusCode.Conflict, Payloads.Round(SyncRig.IdOf(r), 5, "edited elsewhere"));

        var result = await rig.SyncAsync();

        Assert.Equal(1, result.Conflicts);
        var notice = Assert.Single(result.Notices);
        Assert.Equal(SyncNoticeKind.UpdatedFromAnotherDevice, notice.Kind);
        Assert.Equal(round.Round.Id, notice.RoundId);
        Assert.Contains("another device", notice.Message);
        var loaded = (await rig.Temp.Db.Rounds.GetAsync(round.Round.Id))!;
        Assert.Equal("edited elsewhere", loaded.Round.Notes);
        Assert.Equal(5, loaded.Round.Version);
        Assert.False(loaded.Round.Dirty);
        Assert.Single(loaded.Holes); // the server's holes, not the local ones
        Assert.Equal(0, await rig.Temp.Db.Rounds.CountDirtyAsync());
    }

    [Fact]
    public async Task An_edit_made_while_the_push_is_in_flight_stays_dirty()
    {
        await using var rig = await SyncRig.CreateAsync();
        var round = Samples.FullShotRound();
        await rig.Temp.Db.Rounds.SaveAsync(round);
        rig.PutRound = r =>
        {
            rig.Temp.Clock.Advance(TimeSpan.FromSeconds(30));
            rig.Temp.Db.Rounds.SaveHoleAsync(round.Round.Id, Samples.QuickHole(round.Round.Id, 3, 3, 3, 2)).GetAwaiter().GetResult();
            return StubHandler.Json(HttpStatusCode.OK, Payloads.Round(SyncRig.IdOf(r), 1, "pushed"));
        };

        await rig.SyncAsync();

        var loaded = (await rig.Temp.Db.Rounds.GetAsync(round.Round.Id))!;
        Assert.True(loaded.Round.Dirty);
        Assert.Equal(3, loaded.Holes.Count);
        Assert.Equal("windy", loaded.Round.Notes);
    }

    [Fact]
    public async Task A_deleted_round_pushes_DELETE_and_the_tombstone_is_purged()
    {
        await using var rig = await SyncRig.CreateAsync();
        var round = Samples.Synced(Samples.FullShotRound());
        await rig.Temp.Db.Rounds.SaveFromServerAsync(round, ServerWriteMode.Pull);
        await rig.Temp.Db.Rounds.MarkDeletedAsync(round.Round.Id);

        var result = await rig.SyncAsync();

        Assert.Equal($"/rounds/{round.Round.Id}", Assert.Single(rig.Server.To("DELETE", "/rounds/")).Path);
        Assert.Empty(rig.Server.To("PUT", "/rounds/"));
        Assert.Equal(1, result.PushedDeletes);
        Assert.Null(await rig.Temp.Db.Rounds.GetAsync(round.Round.Id, includeDeleted: true));
    }

    [Fact]
    public async Task A_404_on_DELETE_counts_as_success_even_for_a_never_synced_round()
    {
        await using var rig = await SyncRig.CreateAsync();
        var round = Samples.FullShotRound();
        await rig.Temp.Db.Rounds.SaveAsync(round); // never synced
        await rig.Temp.Db.Rounds.MarkDeletedAsync(round.Round.Id);
        rig.DeleteRound = _ => StubHandler.Status(HttpStatusCode.NotFound);

        var result = await rig.SyncAsync();

        Assert.Equal(1, result.PushedDeletes);
        Assert.Empty(result.Failures);
        Assert.Null(await rig.Temp.Db.Rounds.GetAsync(round.Round.Id, includeDeleted: true));
    }

    [Fact]
    public async Task A_failed_DELETE_keeps_the_tombstone_dirty()
    {
        await using var rig = await SyncRig.CreateAsync();
        var round = Samples.Synced(Samples.FullShotRound());
        await rig.Temp.Db.Rounds.SaveFromServerAsync(round, ServerWriteMode.Pull);
        await rig.Temp.Db.Rounds.MarkDeletedAsync(round.Round.Id);
        rig.DeleteRound = _ => StubHandler.Status(HttpStatusCode.BadGateway);

        var result = await rig.SyncAsync();

        Assert.Single(result.Failures);
        var tomb = (await rig.Temp.Db.Rounds.GetAsync(round.Round.Id, includeDeleted: true))!;
        Assert.True(tomb.Round.Dirty);
        Assert.NotNull(tomb.Round.DeletedAt);
    }

    [Fact]
    public async Task A_round_deleted_while_its_PUT_is_in_flight_is_not_resurrected_and_its_DELETE_goes_out_next_sync()
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
            return StubHandler.Json(HttpStatusCode.OK, Payloads.Round(SyncRig.IdOf(r), 1, "pushed"));
        };

        await rig.SyncAsync();
        Assert.Null(await rig.Temp.Db.Rounds.GetAsync(round.Round.Id));
        Assert.Equal(1, await rig.Temp.Db.Rounds.CountDirtyAsync());

        var second = await rig.SyncAsync();

        Assert.Equal(1, second.PushedDeletes);
        Assert.Null(await rig.Temp.Db.Rounds.GetAsync(round.Round.Id, includeDeleted: true));
    }

    [Fact]
    public async Task Pending_local_courses_are_created_before_the_rounds_that_use_them()
    {
        await using var rig = await SyncRig.CreateAsync();
        var manual = Samples.Course("My Track");
        manual.Course.Id = Guid.NewGuid();
        manual.Course.Source = CourseSource.Manual;
        manual.Tees[0].Tee.Id = Guid.NewGuid();
        manual.Tees[0].Versions[0].Version.Id = Guid.NewGuid();
        await rig.Temp.Db.Courses.SaveLocalAsync(manual);
        var round = Samples.FullShotRound();
        round.Round.CourseId = manual.Course.Id;
        round.Round.TeeId = manual.Tees[0].Tee.Id;
        await rig.Temp.Db.Rounds.SaveAsync(round);

        var result = await rig.SyncAsync();

        var order = rig.Server.Requests.Select(r => $"{r.Method.Method} {r.Path}").ToList();
        Assert.True(order.IndexOf("POST /courses") < order.IndexOf($"PUT /rounds/{round.Round.Id}"), string.Join(", ", order));
        var post = Assert.Single(rig.Server.To("POST", "/courses"));
        Assert.Contains(manual.Course.Id.ToString(), post.Body);      // client-generated id
        Assert.Contains(manual.Tees[0].Tee.Id.ToString(), post.Body);
        Assert.Equal(1, result.PushedCourses);
        Assert.Empty(await rig.Temp.Db.Courses.GetPendingAsync());
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]       // a retry of the same id by the same creator
    [InlineData(HttpStatusCode.Created)]
    public async Task Both_200_and_201_from_course_create_count_as_success(HttpStatusCode status)
    {
        await using var rig = await SyncRig.CreateAsync();
        await rig.Temp.Db.Courses.SaveLocalAsync(Samples.Course("My Track"));
        rig.PostCourse = _ => StubHandler.Json(status, new CourseDetailDto { Id = Samples.CourseId, Name = "My Track" });

        var result = await rig.SyncAsync();

        Assert.Equal(1, result.PushedCourses);
        Assert.Empty(await rig.Temp.Db.Courses.GetPendingAsync());
    }

    [Fact]
    public async Task A_course_that_fails_to_create_defers_its_rounds_and_keeps_them_dirty()
    {
        await using var rig = await SyncRig.CreateAsync();
        await rig.Temp.Db.Courses.SaveLocalAsync(Samples.Course("My Track")); // same id the sample rounds use
        var round = Samples.FullShotRound();
        await rig.Temp.Db.Rounds.SaveAsync(round);
        rig.PostCourse = _ => StubHandler.Status(HttpStatusCode.ServiceUnavailable);

        var result = await rig.SyncAsync();

        Assert.Empty(rig.Server.To("PUT", "/rounds/"));
        Assert.Equal(1, result.DeferredRounds);
        Assert.Single(result.Failures);
        Assert.Equal(1, await rig.Temp.Db.Rounds.CountDirtyAsync());
        Assert.Single(await rig.Temp.Db.Courses.GetPendingAsync());
    }

    [Fact]
    public async Task A_401_that_survives_the_auth_handler_reports_signed_out_and_loses_nothing()
    {
        await using var rig = await SyncRig.CreateAsync();
        await rig.Temp.Db.Rounds.SaveAsync(Samples.FullShotRound());
        rig.PutRound = _ => StubHandler.Status(HttpStatusCode.Unauthorized);

        var result = await rig.SyncAsync();

        Assert.True(result.SignedOut);
        Assert.False(result.Succeeded);
        Assert.Equal(1, await rig.Temp.Db.Rounds.CountDirtyAsync());
        Assert.Empty(rig.Server.To("GET", "/sync/changes"));
    }
}
