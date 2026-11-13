using System.Net;
using System.Net.Http.Json;
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

        Assert.Equal(nameof(Tier.Free), changes.Entitlements.Tier);
        Assert.Equal(nameof(Role.User), changes.Entitlements.Role);
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
        // Past the cursor margin, so the next pull no longer repeats these.
        factory.Time.Advance(TimeSpan.FromMinutes(3));
        var first = await ReadAsync<SyncChangesResponse>(await client.GetAsync("/sync/changes", Ct));
        Assert.Equal(3, first.Rounds.Count);
        Assert.Equal(first.ServerTime - TimeSpan.FromMinutes(2), first.NextSince);

        factory.Time.Advance(TimeSpan.FromMinutes(5));
        await PutAsync(client, edited, Quick(seeded.Tee.Id, Now, QuickHole(1, 6)));
        await client.DeleteAsync($"/rounds/{deleted}", Ct);
        var writtenAt = Now;
        factory.Time.Advance(TimeSpan.FromMinutes(3));

        var since = Uri.EscapeDataString(first.NextSince.ToString("O"));
        var second = await ReadAsync<SyncChangesResponse>(await client.GetAsync($"/sync/changes?since={since}", Ct));

        Assert.Equal(Now, second.ServerTime);
        Assert.Equal(2, second.Rounds.Count);
        Assert.DoesNotContain(second.Rounds, r => r.Id == kept);
        Assert.Equal(6, Assert.Single(second.Rounds.Single(r => r.Id == edited).Holes).Score);
        var tombstone = second.Rounds.Single(r => r.Id == deleted);
        Assert.Equal(writtenAt, tombstone.DeletedAt);
        Assert.Single(second.Courses);

        var third = await ReadAsync<SyncChangesResponse>(await client.GetAsync(
            $"/sync/changes?since={Uri.EscapeDataString(second.NextSince.ToString("O"))}", Ct));
        Assert.Empty(third.Rounds);
        Assert.Empty(third.Courses);
    }

    [Fact]
    public async Task A_write_stamped_just_before_the_pull_but_committed_after_it_comes_on_the_next_pull()
    {
        var client = await TestAuth.RegisterAsync(factory, "late@example.com");
        var accountId = await TestAuth.GetAccountIdAsync(factory, "late@example.com");
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db));
        var pull = await ReadAsync<SyncChangesResponse>(await client.GetAsync("/sync/changes", Ct));
        Assert.Empty(pull.Rounds);

        // A write whose ModifiedAt was stamped a second before the pull's server time but committed after the pull read.
        var late = await factory.WithDbAsync(db =>
            TestData.SeedQuickRoundAsync(db, accountId, seeded.Version, RoundRequests.PlayedOn, [4], pull.ServerTime.AddSeconds(-1)));
        factory.Time.Advance(TimeSpan.FromMinutes(1));

        var next = await ReadAsync<SyncChangesResponse>(await client.GetAsync(
            $"/sync/changes?since={Uri.EscapeDataString(pull.NextSince.ToString("O"))}", Ct));

        Assert.Equal(late.Id, Assert.Single(next.Rounds).Id);
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
        Assert.Equal(nameof(Tier.Paid), paid.Entitlements.Tier);
        Assert.True(paid.Entitlements.Features.ShotByShotEntry);
        Assert.Null(paid.Entitlements.TrialShotRoundsRemaining);
    }

    [Fact]
    public async Task A_correction_to_a_course_used_only_by_an_unchanged_round_comes_on_the_next_pull()
    {
        var client = await TestAuth.RegisterAsync(factory, "course-change@example.com");
        var admin = await TestAuth.RegisterAdminAsync(factory, "course-change-admin@example.com");
        var used = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "Corrected Course"));
        await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "Someone Else's Course"));
        await PutAsync(client, Guid.NewGuid(), Quick(used.Tee.Id, Now, QuickHole(1, 4)));
        factory.Time.Advance(TimeSpan.FromMinutes(5));
        var first = await ReadAsync<SyncChangesResponse>(await client.GetAsync("/sync/changes", Ct));
        var since = Uri.EscapeDataString(first.NextSince.ToString("O"));
        factory.Time.Advance(TimeSpan.FromMinutes(5));

        var quiet = await ReadAsync<SyncChangesResponse>(await client.GetAsync($"/sync/changes?since={since}", Ct));
        var pars = (int[])TestData.DefaultPars.Clone();
        pars[0] = 5;
        var correction = await admin.PutAsJsonAsync(
            $"/admin/courses/{used.Course.Id}/tees/{used.Tee.Id}/versions/{used.Version.Id}",
            new
            {
                rating = 72.0m,
                slope = 125,
                holes = pars.Select((par, i) => new { number = i + 1, par, yardage = 400, strokeIndex = TestData.DefaultStrokeIndexes[i] }).ToArray(),
            },
            Ct);
        Assert.Equal(HttpStatusCode.OK, correction.StatusCode);
        var next = await ReadAsync<SyncChangesResponse>(await client.GetAsync($"/sync/changes?since={since}", Ct));

        Assert.Empty(quiet.Rounds);
        Assert.Empty(quiet.Courses);
        Assert.Empty(next.Rounds);
        var course = Assert.Single(next.Courses);
        Assert.Equal(used.Course.Id, course.Id);
        Assert.Equal(5, Assert.Single(Assert.Single(course.Tees).Versions).Holes[0].Par);
    }

    [Fact]
    public async Task Since_with_an_unencoded_plus_offset_is_accepted()
    {
        var client = await TestAuth.RegisterAsync(factory, "plus-since@example.com");

        var response = await client.GetAsync("/sync/changes?since=2026-06-01T14:00:00+02:00", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_since_is_rejected()
    {
        var client = await TestAuth.RegisterAsync(factory, "badsince@example.com");

        var response = await client.GetAsync("/sync/changes?since=yesterday", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
