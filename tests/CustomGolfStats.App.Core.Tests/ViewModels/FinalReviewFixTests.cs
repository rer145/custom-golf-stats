using System.Diagnostics;
using System.Net;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Storage.Repositories;
using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.App.Core.Sync;
using CustomGolfStats.App.Core.Tests.Support;
using CustomGolfStats.App.Core.Tests.Sync;
using CustomGolfStats.App.Core.ViewModels;
using CustomGolfStats.Domain;
using CustomGolfStats.Domain.Courses;
using CustomGolfStats.Domain.Shots;
using Client = CustomGolfStats.Api.Client;

namespace CustomGolfStats.App.Core.Tests.ViewModels;

/// <summary>A manual course the server re-created gets its own tee version ids, so a round's local version id may not exist any more.</summary>
public class ServerAssignedVersionTests
{
    private static readonly Guid ServerVersionId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    /// <summary>The device holds the course as the server now has it (version id differs from the one the round was created with).</summary>
    private static async Task<(VmRig Rig, Guid RoundId)> RigWithServerVersionAsync(EntryMode mode)
    {
        var rig = await VmRig.CreateAsync();
        var roundId = await rig.CreateRoundAsync(mode); // points at Samples.TeeVersionId
        var course = Samples.Course();
        course.Tees[0].Versions[0].Version.Id = ServerVersionId;
        course.Tees[0].Versions[0].Holes.ForEach(h => h.TeeVersionId = ServerVersionId);
        await rig.Db.Courses.SaveFromServerAsync(course);
        Assert.Null(await rig.Db.Connection.FindAsync<TeeVersionRow>(Samples.TeeVersionId)); // the round's version id is gone
        return (rig, roundId);
    }

    [Fact]
    public async Task The_round_screen_and_hole_entry_still_find_the_layout()
    {
        var (rig, roundId) = await RigWithServerVersionAsync(EntryMode.Quick);
        await using var _r = rig;

        var round = new RoundViewModel(rig.Db, rig.Temp.Clock, rig.Nav, new FakePedometer());
        await round.LoadAsync(new RoundArgs(roundId));
        var quick = new QuickHoleViewModel(rig.Db, rig.Nav);
        await quick.LoadAsync(new HoleArgs(roundId, HoleNumber: 2));

        Assert.Equal(18, round.HoleOptions.Count);
        Assert.Equal(1, round.SuggestedHoleNumber);
        Assert.Null(quick.ErrorMessage);
        Assert.Equal((2, 5, 140), (quick.HoleNumber, quick.Par, quick.Yardage));
    }

    [Fact]
    public async Task Shot_entry_and_the_scorecard_find_the_layout_too()
    {
        var (rig, roundId) = await RigWithServerVersionAsync(EntryMode.ShotByShot);
        await using var _r = rig;
        var shots = new ShotEntryViewModel(rig.Db, rig.Nav);
        await shots.LoadAsync(new HoleArgs(roundId, HoleNumber: 1));
        await shots.AddShotCommand.ExecuteAsync(null); // missing end distance: refused, but the hole itself loaded
        Assert.Equal((Lie.Tee, 120), (shots.StartLie, shots.StartDistance));

        var card = new ScorecardViewModel(rig.Db);
        await card.LoadAsync(new RoundArgs(roundId));
        Assert.Equal((71.2, 130), (card.Rating, card.Slope));
    }

    [Fact]
    public async Task A_server_copy_arriving_while_the_golfer_edits_records_the_servers_version_id()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        await t.Db.Rounds.SaveAsync(round);
        var pushed = t.Clock.UtcNow;
        t.Clock.Advance(TimeSpan.FromSeconds(5));
        await t.Db.Rounds.SaveHoleAsync(round.Round.Id, Samples.QuickHole(round.Round.Id, 3, 3, 3, 2)); // edit during the push
        var copy = new RoundRecord(round.Round.Clone(), []);
        copy.Round.TeeVersionId = ServerVersionId;
        copy.Round.Version = 1;
        copy.Round.ModifiedAt = pushed;

        await t.Db.Rounds.SaveFromServerAsync(copy, ServerWriteMode.PushAccepted(pushed));

        var row = (await t.Db.Rounds.GetAsync(round.Round.Id))!.Round;
        Assert.True(row.Dirty);
        Assert.Equal(ServerVersionId, row.TeeVersionId);
    }

    [Fact]
    public async Task A_later_local_edit_does_not_put_the_old_local_version_id_back()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        var synced = Samples.Synced(new RoundRecord(round.Round.Clone(), round.Holes));
        synced.Round.TeeVersionId = ServerVersionId;
        await t.Db.Rounds.SaveFromServerAsync(synced, ServerWriteMode.Pull);

        await t.Db.Rounds.SaveAsync(round); // the caller still holds the version id it created the round with (Samples.TeeVersionId)

        Assert.Equal(ServerVersionId, (await t.Db.Rounds.GetAsync(round.Round.Id))!.Round.TeeVersionId);
    }
}

public class SyncRobustnessTests
{
    /// <summary>A sample course with ids of its own so two can be saved side by side.</summary>
    private static CourseRecord FreshCourse(string name)
    {
        var course = Samples.Course(name);
        course.Course.Id = Guid.NewGuid();
        var tee = course.Tees[0];
        tee.Tee.Id = Guid.NewGuid();
        tee.Tee.CourseId = course.Course.Id;
        var version = tee.Versions[0];
        version.Version.Id = Guid.NewGuid();
        version.Version.TeeId = tee.Tee.Id;
        version.Holes.ForEach(h => h.TeeVersionId = version.Version.Id);
        return course;
    }

    [Fact]
    public async Task One_round_that_blows_up_does_not_stop_the_others_syncing()
    {
        await using var rig = await SyncRig.CreateAsync();
        var bad = Samples.FullShotRound();
        var good = Samples.FullShotRound();
        await rig.Temp.Db.Rounds.SaveAsync(bad);
        rig.Temp.Clock.Advance(TimeSpan.FromSeconds(1));
        await rig.Temp.Db.Rounds.SaveAsync(good);
        rig.PutRound = r => SyncRig.IdOf(r) == bad.Round.Id
            ? throw new InvalidOperationException("unexpected")
            : StubHandler.Json(HttpStatusCode.OK, Payloads.Round(SyncRig.IdOf(r), 1, "pushed"));

        var result = await rig.SyncAsync();

        Assert.Equal(1, result.PushedRounds);
        Assert.True(result.PullCompleted);
        Assert.Contains(result.Failures, f => f.Contains(bad.Round.Id.ToString()));
        var notice = Assert.Single(result.Notices);
        Assert.Equal((SyncNoticeKind.CouldNotSync, bad.Round.Id), (notice.Kind, notice.RoundId));
        Assert.Equal([bad.Round.Id], (await rig.Temp.Db.Rounds.GetDirtyAsync()).Select(r => r.Round.Id));
    }

    [Fact]
    public async Task One_course_that_blows_up_does_not_stop_the_others()
    {
        await using var rig = await SyncRig.CreateAsync();
        var first = FreshCourse("First");
        var second = FreshCourse("Second");
        await rig.Temp.Db.Courses.SaveLocalAsync(first);
        await rig.Temp.Db.Courses.SaveLocalAsync(second);
        rig.PostCourse = r => r.Body!.Contains(first.Course.Id.ToString())
            ? throw new InvalidOperationException("unexpected")
            : StubHandler.Json(HttpStatusCode.Created, new Client.CourseDetailDto { Id = second.Course.Id, Name = "x" });

        var result = await rig.SyncAsync();

        Assert.Equal(1, result.PushedCourses);
        Assert.Equal(first.Course.Id, Assert.Single(result.Notices).CourseId);
        Assert.Equal([first.Course.Id], (await rig.Temp.Db.Courses.GetPendingAsync()).Select(c => c.Course.Id));
    }
}

public class NonFiniteNumberTests
{
    [Fact]
    public async Task A_cleared_number_box_is_refused_in_the_round_extras()
    {
        await using var rig = await VmRig.CreateAsync();
        var id = await rig.CreateRoundAsync(EntryMode.Quick);
        var vm = new RoundSummaryViewModel(rig.Db, rig.Nav);
        await vm.LoadAsync(new RoundArgs(id));
        vm.TemperatureF = double.NaN;
        vm.WindSpeedMph = double.PositiveInfinity;

        await vm.SaveExtrasCommand.ExecuteAsync(null);

        Assert.Contains(UiText.NotANumber, vm.ExtrasErrors);
        Assert.Equal(72.5, (await rig.Db.Rounds.GetAsync(id))!.Round.TemperatureF); // nothing saved
    }

    [Fact]
    public async Task A_cleared_rating_box_is_treated_as_missing_not_a_crash()
    {
        await using var rig = await VmRig.CreateAsync();
        var vm = new ManualCourseViewModel(rig.Db, rig.Temp.Clock, rig.Nav);
        vm.Initialize(9);
        vm.Name = "X";
        vm.Slope = 120;
        vm.Rating = double.NaN;

        var errors = vm.Validate();

        Assert.Contains(UiText.Describe(CourseError.RatingMissing), errors);
        vm.Rating = double.PositiveInfinity;
        Assert.Contains(UiText.Describe(CourseError.RatingMissing), vm.Validate());
    }
}

public class CommandsDoNotThrowTests
{
    private static async Task<(VmRig Rig, Guid RoundId)> RoundThenDeletedElsewhereAsync(EntryMode mode)
    {
        var rig = await VmRig.CreateAsync();
        var id = await rig.CreateRoundAsync(mode);
        await rig.Db.Rounds.SaveFromServerAsync(Samples.Synced(new RoundRecord((await rig.Db.Rounds.GetAsync(id))!.Round, [])), ServerWriteMode.Pull);
        return (rig, id);
    }

    [Fact]
    public async Task Saving_a_quick_hole_into_a_round_that_became_a_tombstone_shows_a_message()
    {
        var (rig, id) = await RoundThenDeletedElsewhereAsync(EntryMode.Quick);
        await using var _r = rig;
        var vm = new QuickHoleViewModel(rig.Db, rig.Nav);
        await vm.LoadAsync(new HoleArgs(id, HoleNumber: 1));
        await rig.Db.Rounds.MarkDeletedAsync(id); // pulled as a tombstone while the golfer was on this screen

        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(UiText.RoundNotFound, vm.ErrorMessage);

        Assert.Equal(0, rig.Nav.Backs);
    }

    [Fact]
    public async Task Saving_shots_into_a_round_that_became_a_tombstone_shows_a_message()
    {
        var (rig, id) = await RoundThenDeletedElsewhereAsync(EntryMode.ShotByShot);
        await using var _r = rig;
        var vm = new ShotEntryViewModel(rig.Db, rig.Nav);
        await vm.LoadAsync(new HoleArgs(id, HoleNumber: 3));
        vm.EndLie = Lie.Holed;
        await vm.AddShotCommand.ExecuteAsync(null);
        await rig.Db.Rounds.MarkDeletedAsync(id);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(UiText.RoundNotFound, vm.ErrorMessage);
        Assert.Equal(0, rig.Nav.Backs);
    }

    [Fact]
    public async Task Deleting_a_hole_from_a_round_that_became_a_tombstone_shows_a_message()
    {
        var (rig, id) = await RoundThenDeletedElsewhereAsync(EntryMode.Quick);
        await using var _r = rig;
        var quick = new QuickHoleViewModel(rig.Db, rig.Nav);
        await quick.LoadAsync(new HoleArgs(id, HoleNumber: 1));
        await quick.SaveCommand.ExecuteAsync(null);
        var vm = new RoundViewModel(rig.Db, rig.Temp.Clock, rig.Nav, new FakePedometer());
        await vm.LoadAsync(new RoundArgs(id));
        await rig.Db.Rounds.MarkDeletedAsync(id);

        await vm.DeleteHoleCommand.ExecuteAsync(vm.Holes[0]);

        Assert.Equal(UiText.RoundNotFound, vm.ErrorMessage);
    }
}

public class ShotDraftTests
{
    private static async Task<ShotEntryViewModel> OpenAsync(VmRig rig, Guid roundId, int? hole = 1)
    {
        var vm = new ShotEntryViewModel(rig.Db, rig.Nav);
        await vm.LoadAsync(new HoleArgs(roundId, HoleNumber: hole));
        return vm;
    }

    private static async Task ShootAsync(ShotEntryViewModel vm, Lie endLie, int? endDistance = null)
    {
        vm.EndLie = endLie;
        vm.EndDistance = endDistance;
        await vm.AddShotCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Unsaved_shots_come_back_after_the_app_is_closed_and_reopened()
    {
        await using var rig = await VmRig.CreateAsync();
        var roundId = await rig.CreateRoundAsync(EntryMode.ShotByShot);
        var vm = await OpenAsync(rig, roundId);
        await ShootAsync(vm, Lie.Fairway, 150);
        await ShootAsync(vm, Lie.Green, 30);

        await rig.Temp.ReopenAsync(); // the app is killed mid-hole
        var back = await OpenAsync(rig, roundId);

        Assert.True(back.IsRestoredDraft);
        Assert.Equal(2, back.Shots.Count);
        Assert.Equal(vm.Shots.Select(s => s.Shot), back.Shots.Select(s => s.Shot));
        Assert.Equal((Lie.Green, 30, "ft"), (back.StartLie, back.StartDistance, back.StartUnit)); // and the next shot is prefilled from them
        Assert.Equal(1, (await rig.Db.Rounds.GetAsync(roundId))!.Holes.Count + 1); // nothing was saved to the round
    }

    [Fact]
    public async Task Saving_the_hole_clears_its_draft()
    {
        await using var rig = await VmRig.CreateAsync();
        var roundId = await rig.CreateRoundAsync(EntryMode.ShotByShot);
        var vm = await OpenAsync(rig, roundId);
        await ShootAsync(vm, Lie.Holed);
        Assert.NotNull(await rig.Db.Drafts.GetAsync(roundId, 1));

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Null(await rig.Db.Drafts.GetAsync(roundId, 1));
        var reopened = await OpenAsync(rig, roundId, hole: 2);
        Assert.False(reopened.IsRestoredDraft);
        Assert.Empty(reopened.Shots);
    }

    [Fact]
    public async Task Deleting_shots_updates_the_draft_and_deleting_the_hole_clears_it()
    {
        await using var rig = await VmRig.CreateAsync();
        var roundId = await rig.CreateRoundAsync(EntryMode.ShotByShot);
        var vm = await OpenAsync(rig, roundId);
        await ShootAsync(vm, Lie.Fairway, 150);
        await ShootAsync(vm, Lie.Green, 30);

        await vm.UndoLastShotCommand.ExecuteAsync(null);
        Assert.Single((await rig.Db.Drafts.GetAsync(roundId, 1))!.Shots);

        await vm.UndoLastShotCommand.ExecuteAsync(null);
        Assert.Null(await rig.Db.Drafts.GetAsync(roundId, 1)); // nothing left to keep

        await ShootAsync(vm, Lie.Fairway, 150);
        await vm.DeleteHoleCommand.ExecuteAsync(null);
        Assert.Null(await rig.Db.Drafts.GetAsync(roundId, 1));
    }

    [Fact]
    public async Task A_draft_for_a_different_hole_is_not_restored_into_this_one()
    {
        await using var rig = await VmRig.CreateAsync();
        var roundId = await rig.CreateRoundAsync(EntryMode.ShotByShot);
        var vm = await OpenAsync(rig, roundId, hole: 1);
        await ShootAsync(vm, Lie.Fairway, 150);

        var other = await OpenAsync(rig, roundId, hole: 5);

        Assert.False(other.IsRestoredDraft);
        Assert.Empty(other.Shots);
    }

    [Fact]
    public async Task An_edit_to_an_existing_hole_is_restored_over_the_saved_shots()
    {
        await using var rig = await VmRig.CreateAsync();
        var roundId = await rig.CreateRoundAsync(EntryMode.ShotByShot);
        var vm = await OpenAsync(rig, roundId);
        await ShootAsync(vm, Lie.Holed);
        await vm.SaveCommand.ExecuteAsync(null);

        var edit = new ShotEntryViewModel(rig.Db, rig.Nav);
        await edit.LoadAsync(new HoleArgs(roundId, EntrySequence: 1));
        edit.EditShotCommand.Execute(edit.Shots[0]);
        edit.EndLie = Lie.Fairway;
        edit.EndDistance = 90;
        await edit.AddShotCommand.ExecuteAsync(null);      // not saved to the round yet
        await rig.Temp.ReopenAsync();

        var back = new ShotEntryViewModel(rig.Db, rig.Nav);
        await back.LoadAsync(new HoleArgs(roundId, EntrySequence: 1));

        Assert.True(back.IsRestoredDraft);
        Assert.Equal(Lie.Fairway, back.Shots[0].Shot.EndLie);
        Assert.Equal(Lie.Holed, (await rig.Db.Rounds.GetAsync(roundId))!.Holes[0].Shots[0].EndLie); // the saved hole is untouched
    }

    [Fact]
    public async Task Sync_ignores_drafts_and_a_deleted_round_loses_its_draft()
    {
        await using var rig = await SyncRig.CreateAsync();
        var round = Samples.FullShotRound();
        await rig.Temp.Db.Rounds.SaveFromServerAsync(Samples.Synced(round), ServerWriteMode.Pull);
        await rig.Temp.Db.Drafts.SaveAsync(round.Round.Id, 9, 3, Samples.Par4Shots());

        var result = await rig.SyncAsync();

        Assert.Equal(0, result.PushedRounds);                               // a draft makes nothing dirty and nothing is pushed
        Assert.NotNull(await rig.Temp.Db.Drafts.GetAsync(round.Round.Id, 9));
        await rig.Temp.Db.Rounds.MarkDeletedAsync(round.Round.Id);
        Assert.Null(await rig.Temp.Db.Drafts.GetAsync(round.Round.Id, 9));
    }

    [Fact]
    public async Task The_first_real_migration_adds_the_draft_table_and_keeps_existing_data()
    {
        await using var t = await TempDb.CreateAsync([LocalDb.DefaultMigrations[0]]); // a device still on schema version 1
        Assert.Equal(1, await t.Db.GetSchemaVersionAsync());
        await t.Db.Rounds.SaveAsync(Samples.FullShotRound());

        await t.ReopenAsync(); // the new app version opens the old file

        Assert.Equal(LocalDb.LatestSchemaVersion, await t.Db.GetSchemaVersionAsync());
        Assert.True(LocalDb.LatestSchemaVersion >= 2);
        Assert.Single(await t.Db.Rounds.ListAsync());
        await t.Db.Drafts.SaveAsync(Guid.NewGuid(), 1, 1, Samples.Par4Shots());
    }
}

public class ListPerformanceTests
{
    private static async Task SeedAsync(LocalDb db, int rounds)
    {
        var roundRows = new List<RoundRow>();
        var holeRows = new List<HoleRow>();
        for (var i = 0; i < rounds; i++)
        {
            var round = Samples.Round(mode: EntryMode.Quick);
            round.PlayedOn = new DateTime(2020, 1, 1).AddDays(i % 2000);
            round.ClientUpdatedAt = DateTimeOffset.UnixEpoch.AddMinutes(i);
            roundRows.Add(round);
            for (var h = 1; h <= 18; h++)
                holeRows.Add(new HoleRow { RoundId = round.Id, HoleNumber = h, EntrySequence = h, Par = 4, Score = 5, Putts = 2 });
        }
        await db.Connection.InsertAllAsync(roundRows);
        await db.Connection.InsertAllAsync(holeRows);
    }

    [Fact]
    public async Task Home_and_overall_stats_stay_fast_with_1500_rounds()
    {
        await using var rig = await VmRig.CreateAsync();
        await SeedAsync(rig.Db, 1500);
        var home = new HomeViewModel(rig.Db, new SyncEngine(rig.Db, new StubHandler(_ => StubHandler.Status(HttpStatusCode.OK)).Client()), rig.Nav);
        var stats = new OverallStatsViewModel(rig.Db, rig.Temp.Clock);

        var watch = Stopwatch.StartNew();
        await home.LoadAsync();
        var homeTime = watch.Elapsed;
        watch.Restart();
        await stats.LoadAsync();
        var statsTime = watch.Elapsed;

        Assert.Equal(50, home.Rounds.Count);
        Assert.True(home.HasMoreRounds);
        Assert.Equal((18, 90, 18), (home.Rounds[0].Holes, home.Rounds[0].Score, home.Rounds[0].ToPar));
        Assert.True(homeTime < TimeSpan.FromSeconds(2), $"Home took {homeTime}");
        Assert.True(statsTime < TimeSpan.FromSeconds(5), $"Overall stats took {statsTime}");
        Assert.Equal(1500, (await rig.Db.Rounds.ListRoundDataAsync()).Count);
    }

    [Fact]
    public async Task See_all_shows_every_round_and_a_small_list_has_no_see_all()
    {
        await using var rig = await VmRig.CreateAsync();
        await SeedAsync(rig.Db, 60);
        var home = new HomeViewModel(rig.Db, new SyncEngine(rig.Db, new StubHandler(_ => StubHandler.Status(HttpStatusCode.OK)).Client()), rig.Nav);

        await home.LoadAsync();
        Assert.Equal((50, true), (home.Rounds.Count, home.HasMoreRounds));
        await home.SeeAllCommand.ExecuteAsync(null);
        Assert.Equal((60, false), (home.Rounds.Count, home.HasMoreRounds));

        home.RecentLimit = 100;
        var small = new HomeViewModel(rig.Db, new SyncEngine(rig.Db, new StubHandler(_ => StubHandler.Status(HttpStatusCode.OK)).Client()), rig.Nav) { RecentLimit = 100 };
        await small.LoadAsync();
        Assert.False(small.HasMoreRounds);
    }

    [Fact]
    public async Task Overlapping_loads_never_duplicate_rows()
    {
        await using var rig = await VmRig.CreateAsync();
        await SeedAsync(rig.Db, 30);
        var home = new HomeViewModel(rig.Db, new SyncEngine(rig.Db, new StubHandler(_ => StubHandler.Status(HttpStatusCode.OK)).Client()), rig.Nav);

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => home.LoadAsync()));

        Assert.Equal(30, home.Rounds.Count);
        Assert.Equal(30, home.Rounds.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public async Task Totals_come_from_one_query_and_match_the_domains_round_data()
    {
        await using var rig = await VmRig.CreateAsync();
        var round = Samples.FullShotRound();
        await rig.Db.Rounds.SaveAsync(round);

        var totals = Assert.Single(await rig.Db.Rounds.ListTotalsAsync());
        var data = (await rig.Db.Rounds.GetAsync(round.Round.Id))!.ToRoundData();

        Assert.Equal((data.Holes.Count, data.Score, data.Par), (totals.Holes, totals.Score, totals.Par));
    }
}

public class MinorFixTests
{
    [Fact]
    public async Task Manual_course_text_lengths_follow_the_API_limits_and_the_first_version_is_in_effect_for_every_date()
    {
        await using var rig = await VmRig.CreateAsync();
        var vm = new ManualCourseViewModel(rig.Db, rig.Temp.Clock, rig.Nav);
        vm.Initialize(9);
        vm.Name = new string('a', CourseLimits.MaxNameLength + 1);
        vm.City = new string('c', CourseLimits.MaxCityLength + 1);
        vm.TeeName = "Blue";
        vm.Rating = 70; vm.Slope = 120;

        var errors = vm.Validate();
        Assert.Contains(UiText.TextTooLong(CourseLimits.MaxNameLength), errors);
        Assert.Contains(UiText.TextTooLong(CourseLimits.MaxCityLength), errors);

        vm.Name = new string('a', CourseLimits.MaxNameLength);
        vm.City = new string('c', CourseLimits.MaxCityLength);
        Assert.Empty(vm.Validate());
        await vm.SaveCommand.ExecuteAsync(null);

        var version = (await rig.Db.Courses.GetPendingAsync()).Single().Tees[0].Versions[0].Version;
        Assert.Equal(new DateTime(1900, 1, 1), version.EffectiveFrom);
        Assert.NotNull(await rig.Db.Courses.GetVersionInEffectAsync(version.TeeId, new DateTime(2010, 1, 1)));
    }

    [Fact]
    public async Task A_pedometer_that_ignores_its_cancellation_token_still_cannot_hold_up_finishing()
    {
        await using var rig = await VmRig.CreateAsync();
        var id = await rig.CreateRoundAsync(EntryMode.Quick);
        var quick = new QuickHoleViewModel(rig.Db, rig.Nav);
        await quick.LoadAsync(new HoleArgs(id, HoleNumber: 1));
        await quick.SaveCommand.ExecuteAsync(null);
        var vm = new RoundViewModel(rig.Db, rig.Temp.Clock, rig.Nav, new HangingPedometer()) { PedometerTimeout = TimeSpan.FromMilliseconds(100) };
        await vm.LoadAsync(new RoundArgs(id));

        var watch = Stopwatch.StartNew();
        await vm.FinishRoundCommand.ExecuteAsync(null);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
        Assert.NotNull((await rig.Db.Rounds.GetAsync(id))!.Round.FinishedAt);
    }

    private sealed class HangingPedometer : Platform.IPedometer
    {
        public Task<Platform.PedometerReading?> ReadAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
            new TaskCompletionSource<Platform.PedometerReading?>().Task; // never completes, ignores ct
    }

    [Fact]
    public async Task The_latest_search_wins_and_a_slow_older_answer_does_not_replace_it()
    {
        await using var rig = await VmRig.CreateAsync();
        var slow = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var slowStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stub = new StubHandler(async r =>
        {
            if (r.Path == "/courses" && r.PathAndQuery.Contains("old"))
            {
                slowStarted.TrySetResult();
                return await slow.Task;
            }
            if (r.Path == "/courses")
                return StubHandler.Json(HttpStatusCode.OK, new List<Client.CourseSummaryDto> { new() { Id = Guid.NewGuid(), Name = "New Result" } });
            return StubHandler.Json(HttpStatusCode.OK, new List<Client.ProviderSearchResultDto>());
        });
        var vm = new CoursePickerViewModel(rig.Db, stub.Client(), new FakeConnectivity(), rig.Temp.Clock, rig.Nav);

        vm.Query = "old query";
        var first = vm.SearchCommand.ExecuteAsync(null);
        await slowStarted.Task;
        vm.Query = "new query";
        await vm.SearchCommand.ExecuteAsync(null);
        slow.SetResult(StubHandler.Json(HttpStatusCode.OK, new List<Client.CourseSummaryDto> { new() { Id = Guid.NewGuid(), Name = "Old Result" } }));
        await first;

        Assert.Equal(["New Result"], vm.OnlineResults.Select(c => c.Name));
        Assert.False(vm.IsSearchingOnline);
    }
}
