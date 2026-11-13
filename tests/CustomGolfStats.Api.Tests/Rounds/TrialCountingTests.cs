using System.Net;
using CustomGolfStats.Api.Features.Rounds;
using CustomGolfStats.Api.Tests.Infrastructure;
using CustomGolfStats.Domain;
using Microsoft.EntityFrameworkCore;
using static CustomGolfStats.Api.Tests.Rounds.RoundRequests;

namespace CustomGolfStats.Api.Tests.Rounds;

public sealed class TrialCountingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => factory.Time.GetUtcNow();

    [Fact]
    public async Task First_push_of_a_new_shot_round_uses_one_trial_round_once()
    {
        var client = await TestAuth.RegisterAsync(factory, "trial@example.com");
        var teeId = await SeedTeeAsync();
        var id = Guid.NewGuid();

        var first = await PutAsync(client, id, ShotByShot(teeId, Now, ShotHole(1, 1)));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True((await ReadAsync<RoundDto>(first)).TrialCounted);
        Assert.Equal(1, await TrialUsedAsync("trial@example.com"));

        // Pushed again and edited later: still one.
        await PutAsync(client, id, ShotByShot(teeId, Now, ShotHole(1, 1)));
        await PutAsync(client, id, ShotByShot(teeId, Now.AddMinutes(1), ShotHole(1, 1), ShotHole(2, 2)));
        Assert.Equal(1, await TrialUsedAsync("trial@example.com"));

        // Quick rounds never count.
        await PutAsync(client, Guid.NewGuid(), Quick(teeId, Now, QuickHole(1, 4)));
        Assert.Equal(1, await TrialUsedAsync("trial@example.com"));
    }

    [Fact]
    public async Task Switching_a_quick_round_to_shot_by_shot_uses_one_trial_round_once()
    {
        var client = await TestAuth.RegisterAsync(factory, "switch@example.com");
        var teeId = await SeedTeeAsync();
        var id = Guid.NewGuid();
        var at = Now;
        await PutAsync(client, id, Quick(teeId, at, QuickHole(1, 4)));
        Assert.Equal(0, await TrialUsedAsync("switch@example.com"));

        var switched = await PutAsync(client, id, ShotByShot(teeId, at.AddMinutes(1), ShotHole(1, 1)));
        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        Assert.True((await ReadAsync<RoundDto>(switched)).TrialCounted);
        Assert.Equal(1, await TrialUsedAsync("switch@example.com"));

        // Back to quick and to shot-by-shot again: still one.
        await PutAsync(client, id, Quick(teeId, at.AddMinutes(2), QuickHole(1, 4)));
        await PutAsync(client, id, ShotByShot(teeId, at.AddMinutes(3), ShotHole(1, 1)));
        Assert.Equal(1, await TrialUsedAsync("switch@example.com"));
    }

    [Fact]
    public async Task Round_is_stored_when_the_trial_is_used_up()
    {
        var client = await TestAuth.RegisterAsync(factory, "exhausted@example.com");
        await TestAuth.SetAsync(factory, "exhausted@example.com", a => a.TrialShotRoundsUsed = Entitlements.DefaultTrialShotRounds);
        var teeId = await SeedTeeAsync();
        var id = Guid.NewGuid();

        var response = await PutAsync(client, id, ShotByShot(teeId, Now, ShotHole(1, 1)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/rounds/{id}", Ct)).StatusCode);
        Assert.Equal(Entitlements.DefaultTrialShotRounds + 1, await TrialUsedAsync("exhausted@example.com"));
    }

    [Fact]
    public async Task Paid_golfers_never_use_trial_rounds()
    {
        var client = await TestAuth.RegisterAsync(factory, "paid@example.com");
        await TestAuth.SetAsync(factory, "paid@example.com", a => a.Tier = Tier.Paid);
        var teeId = await SeedTeeAsync();
        var id = Guid.NewGuid();

        var response = await PutAsync(client, id, ShotByShot(teeId, Now, ShotHole(1, 1)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await ReadAsync<RoundDto>(response)).TrialCounted);
        Assert.Equal(0, await TrialUsedAsync("paid@example.com"));
    }

    [Fact]
    public async Task Editing_a_shot_round_stored_while_paid_after_a_downgrade_uses_no_trial_round()
    {
        var client = await TestAuth.RegisterAsync(factory, "downgraded@example.com");
        await TestAuth.SetAsync(factory, "downgraded@example.com", a => a.Tier = Tier.Paid);
        var teeId = await SeedTeeAsync();
        var id = Guid.NewGuid();
        await PutAsync(client, id, ShotByShot(teeId, Now, ShotHole(1, 1)));
        await TestAuth.SetAsync(factory, "downgraded@example.com", a => a.Tier = Tier.Free);

        var edited = await PutAsync(client, id, ShotByShot(teeId, Now.AddMinutes(1), ShotHole(1, 1), ShotHole(2, 2)));

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.False((await ReadAsync<RoundDto>(edited)).TrialCounted);
        Assert.Equal(0, await TrialUsedAsync("downgraded@example.com"));
    }

    [Fact]
    public async Task Racing_pushes_of_the_same_new_round_count_once()
    {
        var client = await TestAuth.RegisterAsync(factory, "race@example.com");
        var teeId = await SeedTeeAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var id = Guid.NewGuid();
            var request = ShotByShot(teeId, Now, ShotHole(1, 1));
            var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PutAsync(client, id, request)));
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        }

        Assert.Equal(5, await TrialUsedAsync("race@example.com"));
        var rounds = await factory.WithDbAsync(db => db.Rounds.Where(r => r.Account.Email == "race@example.com").ToListAsync(Ct));
        Assert.Equal(5, rounds.Count);
        Assert.All(rounds, r => Assert.True(r.TrialCounted));
    }

    private Task<int> TrialUsedAsync(string email) =>
        factory.WithDbAsync(db => db.Users.Where(a => a.Email == email).Select(a => a.TrialShotRoundsUsed).SingleAsync(Ct));

    private Task<Guid> SeedTeeAsync() =>
        factory.WithDbAsync(async db => (await TestData.SeedCourseAsync(db)).Tee.Id);
}
