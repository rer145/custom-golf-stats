using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.App.Core.Tests.Support;
using CustomGolfStats.Domain;

namespace CustomGolfStats.App.Core.Tests;

public class SmallRepositoryTests
{
    [Fact]
    public async Task Handicap_entries_upsert_by_id_and_list_newest_first()
    {
        await using var t = await TempDb.CreateAsync();
        var id = Guid.NewGuid();
        await t.Db.Handicap.UpsertAsync([
            new HandicapRow { Id = id, EffectiveOn = new DateTime(2026, 1, 1), Index = 15.2 },
            new HandicapRow { Id = Guid.NewGuid(), EffectiveOn = new DateTime(2026, 5, 1), Index = 13.9 },
        ]);
        await t.Db.Handicap.UpsertAsync([new HandicapRow { Id = id, EffectiveOn = new DateTime(2026, 1, 1), Index = 15.0 }]); // duplicate is harmless

        var list = await t.Db.Handicap.ListAsync();

        Assert.Equal([13.9, 15.0], list.Select(h => h.Index));
        Assert.Equal(13.9, (await t.Db.Handicap.LatestAsync())!.Index);
    }

    [Fact]
    public async Task Entitlements_are_cached_and_survive_a_restart()
    {
        await using var t = await TempDb.CreateAsync();
        Assert.Null(await t.Db.Entitlements.GetAsync());

        await t.Db.Entitlements.SaveAsync(new EntitlementsRow { Tier = Tier.Free, Role = Role.User, ShotByShotEntry = true, TrialShotRoundsRemaining = 2 });
        await t.ReopenAsync();

        var e = (await t.Db.Entitlements.GetAsync())!;
        Assert.Equal(Tier.Free, e.Tier);
        Assert.Equal(Role.User, e.Role);
        Assert.True(e.ShotByShotEntry);
        Assert.Equal(2, e.TrialShotRoundsRemaining);
        Assert.Equal(t.Clock.UtcNow, e.UpdatedAt);
    }

    [Fact]
    public async Task Saving_entitlements_replaces_the_single_cached_row()
    {
        await using var t = await TempDb.CreateAsync();
        await t.Db.Entitlements.SaveAsync(new EntitlementsRow { Tier = Tier.Free, TrialShotRoundsRemaining = 1 });
        await t.Db.Entitlements.SaveAsync(new EntitlementsRow { Tier = Tier.Paid, ShotByShotEntry = true });

        Assert.Equal(1, await t.Db.Connection.Table<EntitlementsRow>().CountAsync());
        Assert.Equal(Tier.Paid, (await t.Db.Entitlements.GetAsync())!.Tier);
    }

    [Fact]
    public async Task Offline_trial_round_decrements_the_cached_count_and_closes_the_gate_at_zero()
    {
        await using var t = await TempDb.CreateAsync();
        await t.Db.Entitlements.SaveAsync(new EntitlementsRow { Tier = Tier.Free, ShotByShotEntry = true, TrialShotRoundsRemaining = 2 });

        Assert.True(await t.Db.Entitlements.TryConsumeTrialRoundAsync());
        Assert.Equal(1, (await t.Db.Entitlements.GetAsync())!.TrialShotRoundsRemaining);
        Assert.True(await t.Db.Entitlements.TryConsumeTrialRoundAsync());

        var spent = (await t.Db.Entitlements.GetAsync())!;
        Assert.Equal(0, spent.TrialShotRoundsRemaining);
        Assert.False(spent.ShotByShotEntry);
        Assert.False(await t.Db.Entitlements.TryConsumeTrialRoundAsync());
        Assert.Equal(0, (await t.Db.Entitlements.GetAsync())!.TrialShotRoundsRemaining); // never negative
    }

    [Fact]
    public async Task Paid_users_consume_nothing_and_unknown_entitlements_refuse()
    {
        await using var t = await TempDb.CreateAsync();
        Assert.False(await t.Db.Entitlements.TryConsumeTrialRoundAsync()); // nothing cached yet

        await t.Db.Entitlements.SaveAsync(new EntitlementsRow { Tier = Tier.Paid, ShotByShotEntry = true, TrialShotRoundsRemaining = null });

        Assert.True(await t.Db.Entitlements.TryConsumeTrialRoundAsync());
        Assert.Null((await t.Db.Entitlements.GetAsync())!.TrialShotRoundsRemaining);
    }

    [Fact]
    public async Task Sync_cursor_is_stored_exactly_and_survives_a_restart()
    {
        await using var t = await TempDb.CreateAsync();
        Assert.Null(await t.Db.SyncState.GetNextSinceAsync());

        var since = new DateTimeOffset(2026, 6, 1, 12, 30, 45, 123, TimeSpan.Zero).AddTicks(4567);
        await t.Db.SyncState.SetNextSinceAsync(since);
        await t.ReopenAsync();

        Assert.Equal(since, await t.Db.SyncState.GetNextSinceAsync());
    }

    [Fact]
    public async Task Sync_state_stores_arbitrary_keys()
    {
        await using var t = await TempDb.CreateAsync();

        await t.Db.SyncState.SetAsync("lastNotice", "updated from another device");
        await t.Db.SyncState.SetAsync("lastNotice", "again");

        Assert.Equal("again", await t.Db.SyncState.GetAsync("lastNotice"));
        Assert.Null(await t.Db.SyncState.GetAsync("missing"));
    }
}
