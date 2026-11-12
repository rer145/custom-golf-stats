using System.Net;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Features.Rounds;
using CustomGolfStats.Api.Tests.Infrastructure;
using CustomGolfStats.Domain;
using static CustomGolfStats.Api.Tests.Rounds.RoundRequests;

namespace CustomGolfStats.Api.Tests.Rounds;

public sealed class SyncChangesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => factory.Time.GetUtcNow();

    [Fact]
    public async Task Without_since_returns_everything_for_the_golfer()
    {
        var client = await TestAuth.RegisterAsync(factory, "everything@example.com");
        var other = await TestAuth.RegisterAsync(factory, "someone-else@example.com");
        var accountId = await TestAuth.GetAccountIdAsync(factory, "everything@example.com");
        var used = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "Used Course"));
        await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "Unused Course"));
        var newer = await factory.WithDbAsync(db => TestData.AddTeeVersionAsync(db, db.Tees.Single(t => t.Id == used.Tee.Id), new DateOnly(2027, 1, 1)));
        await factory.WithDbAsync(async db =>
        {
            db.HandicapEntries.Add(new HandicapEntry { AccountId = accountId, EffectiveOn = new DateOnly(2026, 1, 1), Index = 12.3m });
            await db.SaveChangesAsync(Ct);
        });
        var mine = Guid.NewGuid();
        await PutAsync(client, mine, Quick(used.Tee.Id, Now, QuickHole(1, 4)));
        await PutAsync(other, Guid.NewGuid(), Quick(used.Tee.Id, Now, QuickHole(1, 4)));

        var response = await client.GetAsync("/sync/changes", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var changes = await ReadAsync<SyncChangesResponse>(response);
        Assert.Equal(Now, changes.ServerTime);
        var round = Assert.Single(changes.Rounds);
        Assert.Equal(mine, round.Id);
        Assert.Equal(4, Assert.Single(round.Holes).Score);

        var course = Assert.Single(changes.Courses);
        Assert.Equal("Used Course", course.Name);
        var tee = Assert.Single(course.Tees);
        Assert.Equal(used.Tee.Id, tee.Id);
        Assert.Equal([used.Version.Id, newer.Id], tee.Versions.Select(v => v.Id));
        Assert.All(tee.Versions, v => Assert.Equal(18, v.Holes.Count));
        Assert.Equal(TestData.DefaultPars, tee.Versions[0].Holes.Select(h => h.Par));

        var handicap = Assert.Single(changes.HandicapEntries);
        Assert.Equal(12.3m, handicap.Index);

        Assert.Equal(Tier.Free, changes.Entitlements.Tier);
        Assert.Equal(Role.User, changes.Entitlements.Role);
        Assert.True(changes.Entitlements.Features.ShotByShotEntry);
        Assert.Equal(Entitlements.DefaultTrialShotRounds, changes.Entitlements.TrialShotRoundsRemaining);
    }

    [Fact]
    public async Task Since_returns_only_rounds_changed_after_the_cursor_including_tombstones()
    {
        var client = await TestAuth.RegisterAsync(factory, "cursor@example.com");
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db));
        var kept = Guid.NewGuid();
        var edited = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        await PutAsync(client, kept, Quick(seeded.Tee.Id, Now, QuickHole(1, 4)));
        await PutAsync(client, edited, Quick(seeded.Tee.Id, Now, QuickHole(1, 4)));
        await PutAsync(client, deleted, Quick(seeded.Tee.Id, Now, QuickHole(1, 4)));
        factory.Time.Advance(TimeSpan.FromSeconds(1));
        var first = await ReadAsync<SyncChangesResponse>(await client.GetAsync("/sync/changes", Ct));
        Assert.Equal(3, first.Rounds.Count);

        factory.Time.Advance(TimeSpan.FromMinutes(5));
        await PutAsync(client, edited, Quick(seeded.Tee.Id, Now, QuickHole(1, 6)));
        await client.DeleteAsync($"/rounds/{deleted}", Ct);

        var since = Uri.EscapeDataString(first.ServerTime.ToString("O"));
        var second = await ReadAsync<SyncChangesResponse>(await client.GetAsync($"/sync/changes?since={since}", Ct));

        Assert.Equal(Now, second.ServerTime);
        Assert.Equal(2, second.Rounds.Count);
        Assert.DoesNotContain(second.Rounds, r => r.Id == kept);
        Assert.Equal(6, Assert.Single(second.Rounds.Single(r => r.Id == edited).Holes).Score);
        var tombstone = second.Rounds.Single(r => r.Id == deleted);
        Assert.Equal(Now, tombstone.DeletedAt);
        Assert.Single(second.Courses);

        var third = await ReadAsync<SyncChangesResponse>(await client.GetAsync(
            $"/sync/changes?since={Uri.EscapeDataString(Now.AddSeconds(1).ToString("O"))}", Ct));
        Assert.Empty(third.Rounds);
        Assert.Empty(third.Courses);
    }

    [Fact]
    public async Task Entitlements_reflect_the_account_now()
    {
        var client = await TestAuth.RegisterAsync(factory, "ent@example.com");
        await TestAuth.SetAsync(factory, "ent@example.com", a => a.TrialShotRoundsUsed = 5);
        await factory.WithDbAsync(async db =>
        {
            var setting = db.AppSettings.SingleOrDefault(s => s.Key == AppSettingKeys.TrialShotRounds);
            if (setting is null)
            {
                db.AppSettings.Add(new AppSetting { Key = AppSettingKeys.TrialShotRounds, Value = "3" });
            }

            await db.SaveChangesAsync(Ct);
        });

        var free = await ReadAsync<SyncChangesResponse>(await client.GetAsync("/sync/changes", Ct));
        Assert.False(free.Entitlements.Features.ShotByShotEntry);
        Assert.Equal(0, free.Entitlements.TrialShotRoundsRemaining);

        await TestAuth.SetAsync(factory, "ent@example.com", a => a.Tier = Tier.Paid);
        var paid = await ReadAsync<SyncChangesResponse>(await client.GetAsync("/sync/changes", Ct));
        Assert.Equal(Tier.Paid, paid.Entitlements.Tier);
        Assert.True(paid.Entitlements.Features.ShotByShotEntry);
        Assert.Null(paid.Entitlements.TrialShotRoundsRemaining);
    }

    [Fact]
    public async Task Invalid_since_is_rejected()
    {
        var client = await TestAuth.RegisterAsync(factory, "badsince@example.com");

        var response = await client.GetAsync("/sync/changes?since=yesterday", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
