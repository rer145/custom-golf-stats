using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Storage.Repositories;
using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.App.Core.Tests.Support;
using CustomGolfStats.Domain;
using CustomGolfStats.Domain.Holes;
using CustomGolfStats.Domain.Shots;

namespace CustomGolfStats.App.Core.Tests;

public class RoundRepositoryTests
{
    [Fact]
    public async Task Full_shot_by_shot_round_round_trips_through_the_database()
    {
        await using var t = await TempDb.CreateAsync();
        var original = Samples.FullShotRound();
        await t.Db.Rounds.SaveAsync(original);

        await t.ReopenAsync(); // survive an app restart
        var loaded = await t.Db.Rounds.GetAsync(original.Round.Id);

        Assert.NotNull(loaded);
        var r = loaded.Round;
        Assert.Equal(original.Round.CourseName, r.CourseName);
        Assert.Equal(Samples.TeeVersionId, r.TeeVersionId);
        Assert.Equal(new DateTime(2026, 6, 1), r.PlayedOn);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), r.StartedAt);
        Assert.Equal(EntryMode.ShotByShot, r.EntryMode);
        Assert.Equal(Api.Client.RoundType.Casual, r.RoundType);
        Assert.Equal("windy", r.Notes);
        Assert.Equal(72.5, r.TemperatureF);
        Assert.Equal(270, r.WindDirectionDegrees);

        Assert.Equal([1, 2], loaded.Holes.Select(h => h.Hole.HoleNumber));
        Assert.All(loaded.Holes, h => Assert.True(h.Hole.IsDerived));

        // Shots come back in order and equal to what went in.
        var shots1 = loaded.Holes[0].Shots.Select(s => s.ToShot()).ToList();
        Assert.Equal(Samples.Par4Shots(), shots1);
        var shots2 = loaded.Holes[1].Shots.Select(s => s.ToShot()).ToList();
        Assert.Equal(Samples.Par5Shots(), shots2);
        Assert.Null(shots1[^1].EndDistance);   // holed: no end distance
        Assert.Equal(Lie.Green, shots1[2].StartLie);
        Assert.Equal(18, shots1[2].StartDistance); // feet on the green
        Assert.Equal(Shape.Hook, shots2[0].Shape);
        Assert.Equal(1, shots2[0].PenaltyStrokes);
    }

    [Fact]
    public async Task Stored_summaries_equal_the_domain_derivation_and_feed_RoundData()
    {
        await using var t = await TempDb.CreateAsync();
        var original = Samples.FullShotRound();
        await t.Db.Rounds.SaveAsync(original);
        var loaded = (await t.Db.Rounds.GetAsync(original.Round.Id))!;

        var expected1 = HoleSummaryDeriver.Derive(new HoleInfo(1, 4, 380, 7), 1, Samples.Par4Shots());
        var expected2 = HoleSummaryDeriver.Derive(new HoleInfo(2, 5, 500, 1), 2, Samples.Par5Shots());
        Assert.Equal(expected1, loaded.Holes[0].ToSummary());
        Assert.Equal(expected2, loaded.Holes[1].ToSummary());

        var data = loaded.ToRoundData();
        Assert.Equal(original.Round.Id, data.Id);
        Assert.Equal(new DateOnly(2026, 6, 1), data.PlayedOn);
        Assert.Equal(Samples.CourseId, data.CourseId);
        Assert.Equal("Blue", data.TeeName);
        Assert.Equal(expected1.Score + expected2.Score, data.Score);
        Assert.Equal(2, data.Holes.Count);
    }

    [Fact]
    public async Task Saving_marks_the_round_dirty_with_ClientUpdatedAt_now()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();

        await t.Db.Rounds.SaveAsync(round);

        var row = (await t.Db.Rounds.GetAsync(round.Round.Id))!.Round;
        Assert.True(row.Dirty);
        Assert.Equal(t.Clock.UtcNow, row.ClientUpdatedAt);
        Assert.Equal(1, await t.Db.Rounds.CountDirtyAsync());
    }

    [Fact]
    public async Task Each_local_edit_moves_ClientUpdatedAt_forward()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        await t.Db.Rounds.SaveAsync(round);
        t.Clock.Advance(TimeSpan.FromMinutes(5));

        await t.Db.Rounds.SaveHoleAsync(round.Round.Id, Samples.QuickHole(round.Round.Id, 3, 3, 3, 2));

        var loaded = (await t.Db.Rounds.GetAsync(round.Round.Id))!;
        Assert.Equal(t.Clock.UtcNow, loaded.Round.ClientUpdatedAt);
        Assert.True(loaded.Round.Dirty);
        Assert.Equal([1, 2, 3], loaded.Holes.Select(h => h.Hole.HoleNumber));
    }

    [Fact]
    public async Task Replacing_a_hole_replaces_its_shots()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        await t.Db.Rounds.SaveAsync(round);

        var redone = HoleRecord.FromShots(round.Round.Id, new HoleInfo(1, 4, 380, 7), 1, Samples.Par5Shots());
        await t.Db.Rounds.SaveHoleAsync(round.Round.Id, redone);

        var loaded = (await t.Db.Rounds.GetAsync(round.Round.Id))!;
        Assert.Equal(2, loaded.Holes.Count);
        Assert.Equal(4, loaded.Holes[0].Shots.Count);
        Assert.Equal(Samples.Par5Shots(), loaded.Holes[0].Shots.Select(s => s.ToShot()));
        Assert.Equal(8, await t.Db.Connection.Table<ShotRow>().CountAsync()); // no orphans
    }

    [Fact]
    public async Task Removing_a_hole_removes_its_shots_and_marks_dirty()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        await t.Db.Rounds.SaveAsync(round);
        await t.Db.Rounds.MarkCleanAsync(round.Round.Id);

        await t.Db.Rounds.DeleteHoleAsync(round.Round.Id, 2);

        var loaded = (await t.Db.Rounds.GetAsync(round.Round.Id))!;
        Assert.Single(loaded.Holes);
        Assert.True(loaded.Round.Dirty);
        Assert.Equal(4, await t.Db.Connection.Table<ShotRow>().CountAsync());
    }

    [Fact]
    public async Task Dirty_rounds_survive_a_restart_and_clean_rounds_do_not_appear_dirty()
    {
        await using var t = await TempDb.CreateAsync();
        var dirty = Samples.FullShotRound();
        var clean = Samples.FullShotRound();
        await t.Db.Rounds.SaveAsync(dirty);
        await t.Db.Rounds.SaveAsync(clean);
        await t.Db.Rounds.MarkCleanAsync(clean.Round.Id);

        await t.ReopenAsync();
        var pending = await t.Db.Rounds.GetDirtyAsync();

        Assert.Equal([dirty.Round.Id], pending.Select(p => p.Round.Id));
        Assert.Equal(2, pending[0].Holes.Count); // pending rounds carry their holes and shots
        Assert.Equal(8, pending[0].Holes.Sum(h => h.Shots.Count));
    }

    [Fact]
    public async Task Saving_a_server_copy_stores_it_clean_and_keeps_server_fields()
    {
        await using var t = await TempDb.CreateAsync();
        var server = Samples.FullShotRound();
        server.Round.Version = 7;
        server.Round.ModifiedAt = new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero);
        server.Round.ClientUpdatedAt = new DateTimeOffset(2026, 6, 1, 18, 0, 0, TimeSpan.Zero);

        await t.Db.Rounds.SaveFromServerAsync(server, ServerWriteMode.Pull);

        var row = (await t.Db.Rounds.GetAsync(server.Round.Id))!.Round;
        Assert.False(row.Dirty);
        Assert.Equal(7, row.Version);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 18, 0, 0, TimeSpan.Zero), row.ClientUpdatedAt); // untouched by the local clock
        Assert.Equal(0, await t.Db.Rounds.CountDirtyAsync());
    }

    [Fact]
    public async Task Server_copy_replaces_local_holes_and_shots()
    {
        await using var t = await TempDb.CreateAsync();
        var local = Samples.FullShotRound();
        await t.Db.Rounds.SaveAsync(local);

        var server = new RoundRecord(Samples.Round(local.Round.Id, EntryMode.Quick), [Samples.QuickHole(local.Round.Id, 1, 4, 5, 2)]);
        await t.Db.Rounds.SaveFromServerAsync(server, ServerWriteMode.ServerWins);

        var loaded = (await t.Db.Rounds.GetAsync(local.Round.Id))!;
        Assert.Single(loaded.Holes);
        Assert.Empty(loaded.Holes[0].Shots);
        Assert.Equal(0, await t.Db.Connection.Table<ShotRow>().CountAsync());
    }

    [Fact]
    public async Task Deleting_a_round_leaves_a_dirty_tombstone_without_children()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        await t.Db.Rounds.SaveFromServerAsync(Samples.Synced(round), ServerWriteMode.Pull);
        t.Clock.Advance(TimeSpan.FromHours(1));

        await t.Db.Rounds.MarkDeletedAsync(round.Round.Id);

        Assert.Null(await t.Db.Rounds.GetAsync(round.Round.Id));
        Assert.Empty(await t.Db.Rounds.ListAsync());
        var tomb = await t.Db.Rounds.GetAsync(round.Round.Id, includeDeleted: true);
        Assert.NotNull(tomb);
        Assert.True(tomb.Round.Dirty);
        Assert.Equal(t.Clock.UtcNow, tomb.Round.DeletedAt);
        Assert.Equal(t.Clock.UtcNow, tomb.Round.ClientUpdatedAt);
        Assert.Empty(tomb.Holes);
        Assert.Equal(0, await t.Db.Connection.Table<HoleRow>().CountAsync());
        Assert.Equal(0, await t.Db.Connection.Table<ShotRow>().CountAsync());

        // The pending DELETE is discoverable after a restart.
        await t.ReopenAsync();
        var pending = await t.Db.Rounds.GetDirtyAsync();
        Assert.Single(pending);
        Assert.NotNull(pending[0].Round.DeletedAt);
    }

    [Fact]
    public async Task After_the_delete_is_pushed_the_tombstone_stays_clean_and_hidden()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        await t.Db.Rounds.SaveFromServerAsync(Samples.Synced(round), ServerWriteMode.Pull);
        await t.Db.Rounds.MarkDeletedAsync(round.Round.Id);

        await t.Db.Rounds.MarkCleanAsync(round.Round.Id);

        Assert.Equal(0, await t.Db.Rounds.CountDirtyAsync());
        Assert.Empty(await t.Db.Rounds.ListAsync());
        Assert.NotNull(await t.Db.Rounds.GetAsync(round.Round.Id, includeDeleted: true));
    }

    [Fact]
    public async Task A_pulled_deleted_round_is_stored_as_a_clean_tombstone()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        await t.Db.Rounds.SaveFromServerAsync(round, ServerWriteMode.Pull);
        var tomb = new RoundRecord(Samples.Round(round.Round.Id), []);
        tomb.Round.DeletedAt = new DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero);

        await t.Db.Rounds.SaveFromServerAsync(tomb, ServerWriteMode.Pull);

        Assert.Empty(await t.Db.Rounds.ListAsync());
        Assert.Equal(0, await t.Db.Rounds.CountDirtyAsync());
        Assert.Equal(0, await t.Db.Connection.Table<HoleRow>().CountAsync());
    }

    private static RoundRecord ServerCopy(RoundRecord of, int version, DateTimeOffset clientUpdatedAt)
    {
        var copy = new RoundRecord(of.Round.Clone(), of.Holes);
        copy.Round.Version = version;
        copy.Round.ModifiedAt = new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero);
        copy.Round.ClientUpdatedAt = clientUpdatedAt;
        copy.Round.Notes = "from server";
        return copy;
    }

    [Fact]
    public async Task Pull_does_not_overwrite_a_dirty_round()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        await t.Db.Rounds.SaveAsync(round);

        var stored = await t.Db.Rounds.SaveFromServerAsync(ServerCopy(round, 4, t.Clock.UtcNow.AddDays(1)), ServerWriteMode.Pull);

        Assert.False(stored);
        var row = (await t.Db.Rounds.GetAsync(round.Round.Id))!.Round;
        Assert.Equal("windy", row.Notes);
        Assert.True(row.Dirty);
        Assert.Equal(0, row.Version);
    }

    [Fact]
    public async Task Pull_does_not_resurrect_a_round_with_a_pending_delete()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        await t.Db.Rounds.SaveFromServerAsync(Samples.Synced(round), ServerWriteMode.Pull);
        await t.Db.Rounds.MarkDeletedAsync(round.Round.Id);

        var stored = await t.Db.Rounds.SaveFromServerAsync(ServerCopy(round, 4, t.Clock.UtcNow.AddDays(1)), ServerWriteMode.Pull);

        Assert.False(stored);
        Assert.Null(await t.Db.Rounds.GetAsync(round.Round.Id));
        var tomb = (await t.Db.Rounds.GetAsync(round.Round.Id, includeDeleted: true))!;
        Assert.True(tomb.Round.Dirty);
        Assert.NotNull(tomb.Round.DeletedAt);
    }

    [Fact]
    public async Task Pull_over_a_clean_round_stores_the_server_copy()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        await t.Db.Rounds.SaveFromServerAsync(Samples.Synced(round), ServerWriteMode.Pull);

        var stored = await t.Db.Rounds.SaveFromServerAsync(ServerCopy(round, 4, t.Clock.UtcNow), ServerWriteMode.Pull);

        Assert.True(stored);
        Assert.Equal("from server", (await t.Db.Rounds.GetAsync(round.Round.Id))!.Round.Notes);
    }

    [Fact]
    public async Task Push_accepted_stores_the_server_copy_when_unchanged_since_the_push()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        await t.Db.Rounds.SaveAsync(round);
        var pushed = t.Clock.UtcNow;

        var stored = await t.Db.Rounds.SaveFromServerAsync(ServerCopy(round, 1, pushed), ServerWriteMode.PushAccepted(pushed));

        Assert.True(stored);
        var row = (await t.Db.Rounds.GetAsync(round.Round.Id))!.Round;
        Assert.False(row.Dirty);
        Assert.Equal(1, row.Version);
    }

    [Fact]
    public async Task Push_accepted_with_an_edit_made_during_the_push_keeps_the_local_edit_dirty()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        await t.Db.Rounds.SaveAsync(round);
        var pushed = t.Clock.UtcNow;
        t.Clock.Advance(TimeSpan.FromSeconds(20));
        await t.Db.Rounds.SaveHoleAsync(round.Round.Id, Samples.QuickHole(round.Round.Id, 3, 3, 3, 2)); // edit during the push

        var stored = await t.Db.Rounds.SaveFromServerAsync(ServerCopy(round, 1, pushed), ServerWriteMode.PushAccepted(pushed));

        Assert.False(stored);
        var loaded = (await t.Db.Rounds.GetAsync(round.Round.Id))!;
        Assert.True(loaded.Round.Dirty);
        Assert.Equal("windy", loaded.Round.Notes);          // local content kept
        Assert.Equal(3, loaded.Holes.Count);                // including the new hole
        Assert.Equal(1, loaded.Round.Version);              // server-owned fields are recorded
        Assert.Equal(t.Clock.UtcNow, loaded.Round.ClientUpdatedAt);
    }

    [Fact]
    public async Task Server_wins_overwrites_a_dirty_round_and_a_pending_delete()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        await t.Db.Rounds.SaveAsync(round);

        Assert.True(await t.Db.Rounds.SaveFromServerAsync(ServerCopy(round, 5, t.Clock.UtcNow.AddDays(1)), ServerWriteMode.ServerWins));

        var row = (await t.Db.Rounds.GetAsync(round.Round.Id))!.Round;
        Assert.False(row.Dirty);
        Assert.Equal("from server", row.Notes);
        Assert.Equal(5, row.Version);

        await t.Db.Rounds.MarkDeletedAsync(round.Round.Id); // synced now, so this is a tombstone
        Assert.True(await t.Db.Rounds.SaveFromServerAsync(ServerCopy(round, 6, t.Clock.UtcNow.AddDays(2)), ServerWriteMode.ServerWins));
        Assert.NotNull(await t.Db.Rounds.GetAsync(round.Round.Id));
        Assert.Equal(0, await t.Db.Rounds.CountDirtyAsync());
    }

    [Fact]
    public async Task Local_save_keeps_server_owned_fields_and_does_not_mutate_the_callers_objects()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        var synced = Samples.Synced(round);
        synced.Round.TrialCounted = true;
        await t.Db.Rounds.SaveFromServerAsync(synced, ServerWriteMode.Pull);

        var edit = Samples.FullShotRound(round.Round.Id);   // caller's copy knows nothing of server fields
        edit.Round.Version = 99;
        edit.Round.TrialCounted = false;
        var before = edit.Round.ClientUpdatedAt;
        t.Clock.Advance(TimeSpan.FromMinutes(1));
        await t.Db.Rounds.SaveAsync(edit);

        var row = (await t.Db.Rounds.GetAsync(round.Round.Id))!.Round;
        Assert.Equal(1, row.Version);
        Assert.NotNull(row.ModifiedAt);
        Assert.True(row.TrialCounted);
        Assert.True(row.Dirty);
        Assert.Equal(99, edit.Round.Version);               // untouched
        Assert.Equal(before, edit.Round.ClientUpdatedAt);
        Assert.False(edit.Round.Dirty);
    }

    [Fact]
    public async Task A_failing_save_leaves_the_stored_round_intact()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        await t.Db.Rounds.SaveAsync(round);
        var savedAt = t.Clock.UtcNow;
        t.Clock.Advance(TimeSpan.FromMinutes(1));

        var dup = new RoundRecord(Samples.Round(round.Round.Id), [Samples.QuickHole(round.Round.Id, 1, 4, 4, 2), Samples.QuickHole(round.Round.Id, 1, 4, 5, 2)]);
        await Assert.ThrowsAnyAsync<Exception>(() => t.Db.Rounds.SaveAsync(dup)); // duplicate hole number

        var loaded = (await t.Db.Rounds.GetAsync(round.Round.Id))!;
        Assert.Equal(2, loaded.Holes.Count);
        Assert.Equal(4, loaded.Holes[0].Shots.Count);
        Assert.Equal(savedAt, loaded.Round.ClientUpdatedAt);
        Assert.Equal(8, await t.Db.Connection.Table<ShotRow>().CountAsync());
    }

    [Fact]
    public async Task Deleting_a_never_synced_round_still_leaves_a_dirty_tombstone()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        await t.Db.Rounds.SaveAsync(round); // never synced: Version 0, no ModifiedAt

        await t.Db.Rounds.MarkDeletedAsync(round.Round.Id);

        var tomb = (await t.Db.Rounds.GetAsync(round.Round.Id, includeDeleted: true))!;
        Assert.True(tomb.Round.Dirty);
        Assert.NotNull(tomb.Round.DeletedAt);
        Assert.Empty(tomb.Holes);
        Assert.Null(await t.Db.Rounds.GetAsync(round.Round.Id));
    }

    [Fact]
    public async Task A_push_that_lands_after_a_delete_does_not_resurrect_the_round()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        await t.Db.Rounds.SaveAsync(round);
        var pushed = t.Clock.UtcNow;
        t.Clock.Advance(TimeSpan.FromSeconds(5));
        await t.Db.Rounds.MarkDeletedAsync(round.Round.Id); // deleted while the push is in flight

        var stored = await t.Db.Rounds.SaveFromServerAsync(ServerCopy(round, 1, pushed), ServerWriteMode.PushAccepted(pushed));

        Assert.False(stored);
        Assert.Null(await t.Db.Rounds.GetAsync(round.Round.Id));
        var tomb = (await t.Db.Rounds.GetAsync(round.Round.Id, includeDeleted: true))!;
        Assert.True(tomb.Round.Dirty);          // the DELETE is still to be sent
        Assert.NotNull(tomb.Round.DeletedAt);
    }

    [Fact]
    public async Task Push_accepted_with_no_local_row_is_a_no_op()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();

        var stored = await t.Db.Rounds.SaveFromServerAsync(ServerCopy(round, 1, t.Clock.UtcNow), ServerWriteMode.PushAccepted(t.Clock.UtcNow));

        Assert.False(stored);
        Assert.Null(await t.Db.Rounds.GetAsync(round.Round.Id, includeDeleted: true));
    }

    [Fact]
    public async Task Pull_with_no_local_row_stores_the_round()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();

        var stored = await t.Db.Rounds.SaveFromServerAsync(ServerCopy(round, 2, t.Clock.UtcNow), ServerWriteMode.Pull);

        Assert.True(stored);
        var loaded = (await t.Db.Rounds.GetAsync(round.Round.Id))!;
        Assert.False(loaded.Round.Dirty);
        Assert.Equal(2, loaded.Holes.Count);
    }

    [Fact]
    public async Task Purging_removes_only_tombstones()
    {
        await using var t = await TempDb.CreateAsync();
        var live = Samples.FullShotRound();
        var gone = Samples.FullShotRound();
        await t.Db.Rounds.SaveAsync(live);
        await t.Db.Rounds.SaveAsync(gone);
        await t.Db.Rounds.MarkDeletedAsync(gone.Round.Id);

        Assert.False(await t.Db.Rounds.PurgeTombstoneAsync(live.Round.Id));
        Assert.False(await t.Db.Rounds.PurgeTombstoneAsync(Guid.NewGuid()));
        Assert.True(await t.Db.Rounds.PurgeTombstoneAsync(gone.Round.Id));

        Assert.NotNull(await t.Db.Rounds.GetAsync(live.Round.Id));
        Assert.Null(await t.Db.Rounds.GetAsync(gone.Round.Id, includeDeleted: true));
        Assert.Equal(1, await t.Db.Connection.Table<RoundRow>().CountAsync());
    }

    [Fact]
    public async Task A_local_save_over_a_tombstone_throws_and_leaves_it_deleted()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        await t.Db.Rounds.SaveAsync(round);
        await t.Db.Rounds.MarkDeletedAsync(round.Round.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => t.Db.Rounds.SaveAsync(round));

        Assert.Null(await t.Db.Rounds.GetAsync(round.Round.Id));
        var tomb = (await t.Db.Rounds.GetAsync(round.Round.Id, includeDeleted: true))!;
        Assert.NotNull(tomb.Round.DeletedAt);
        Assert.Empty(tomb.Holes);
    }

    [Fact]
    public async Task A_failing_hole_save_leaves_the_round_intact()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        await t.Db.Rounds.SaveAsync(round);
        var savedAt = t.Clock.UtcNow;
        t.Clock.Advance(TimeSpan.FromMinutes(1));

        // Replacing hole 1 deletes the old rows first; the broken record (null shots) then throws mid-write.
        var broken = new HoleRecord(Samples.QuickHole(round.Round.Id, 1, 4, 4, 2).Hole, null!);
        await Assert.ThrowsAnyAsync<Exception>(() => t.Db.Rounds.SaveHoleAsync(round.Round.Id, broken));

        var loaded = (await t.Db.Rounds.GetAsync(round.Round.Id))!;
        Assert.Equal(2, loaded.Holes.Count);
        Assert.Equal(4, loaded.Holes[0].Shots.Count);
        Assert.Equal(savedAt, loaded.Round.ClientUpdatedAt);
        Assert.Equal(8, await t.Db.Connection.Table<ShotRow>().CountAsync());
    }

    [Fact]
    public async Task List_is_newest_first_and_excludes_deleted()
    {
        await using var t = await TempDb.CreateAsync();
        var older = Samples.Round(); older.PlayedOn = new DateTime(2026, 5, 1);
        var newer = Samples.Round(); newer.PlayedOn = new DateTime(2026, 6, 9);
        var gone = Samples.Round(); gone.PlayedOn = new DateTime(2026, 7, 1);
        await t.Db.Rounds.SaveAsync(new RoundRecord(older, []));
        await t.Db.Rounds.SaveAsync(new RoundRecord(newer, []));
        await t.Db.Rounds.SaveAsync(new RoundRecord(gone, []));
        await t.Db.Rounds.MarkDeletedAsync(gone.Id);

        var list = await t.Db.Rounds.ListAsync();

        Assert.Equal([newer.Id, older.Id], list.Select(r => r.Id));
    }

    [Fact]
    public async Task Clearing_dirty_after_a_push_keeps_the_round_dirty_if_it_was_edited_meanwhile()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        await t.Db.Rounds.SaveAsync(round);
        var pushed = t.Clock.UtcNow;                       // the version that went to the server
        t.Clock.Advance(TimeSpan.FromSeconds(30));
        await t.Db.Rounds.SaveHoleAsync(round.Round.Id, Samples.QuickHole(round.Round.Id, 3, 3, 3, 2)); // edited during the push

        var cleared = await t.Db.Rounds.MarkCleanAsync(round.Round.Id, pushed);

        Assert.False(cleared);
        Assert.Equal(1, await t.Db.Rounds.CountDirtyAsync());
        Assert.True(await t.Db.Rounds.MarkCleanAsync(round.Round.Id, t.Clock.UtcNow));
        Assert.Equal(0, await t.Db.Rounds.CountDirtyAsync());
    }

    [Fact]
    public async Task Editing_a_missing_or_deleted_round_throws()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        await t.Db.Rounds.SaveAsync(round);
        await t.Db.Rounds.MarkDeletedAsync(round.Round.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => t.Db.Rounds.SaveHoleAsync(round.Round.Id, Samples.QuickHole(round.Round.Id, 1, 4, 4, 2)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => t.Db.Rounds.DeleteHoleAsync(Guid.NewGuid(), 1));
    }

    [Fact]
    public async Task Unknown_round_returns_null_and_deleting_it_is_harmless()
    {
        await using var t = await TempDb.CreateAsync();

        Assert.Null(await t.Db.Rounds.GetAsync(Guid.NewGuid()));
        await t.Db.Rounds.MarkDeletedAsync(Guid.NewGuid());
        Assert.Equal(0, await t.Db.Rounds.CountDirtyAsync());
    }
}
