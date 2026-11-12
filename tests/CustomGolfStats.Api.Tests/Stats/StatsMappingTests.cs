using System.Net;
using System.Net.Http.Json;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Features.Stats;
using CustomGolfStats.Api.Tests.Infrastructure;
using CustomGolfStats.Domain;
using CustomGolfStats.Domain.Holes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CustomGolfStats.Api.Tests.Stats;

/// <summary>Checks the DB-row to domain mapping against literals (not through the loader).</summary>
public sealed class StatsMappingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static int _emailCounter;

    private async Task<(HttpClient Client, Guid AccountId)> NewGolferAsync()
    {
        var email = $"mapping{Interlocked.Increment(ref _emailCounter)}@example.com";
        var client = await TestAuth.RegisterAsync(factory, email);
        return (client, await TestAuth.GetAccountIdAsync(factory, email));
    }

    /// <summary>Default pars: hole 1 = 4, hole 2 = 4, hole 3 = 3, hole 4 = 5.</summary>
    private async Task<(Round Round, SeededCourse Seeded)> SeedDistinctiveRoundAsync(Guid accountId, string course = "Mapping GC")
    {
        return await factory.WithDbAsync(async db =>
        {
            var s = await TestData.SeedCourseAsync(db, course, teeName: "Gold");
            var round = new Round
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                TeeVersionId = s.Version.Id,
                PlayedOn = new DateOnly(2025, 4, 2),
                EntryMode = EntryMode.Quick,
                ClientUpdatedAt = ApiFactory.StartTime,
                ModifiedAt = ApiFactory.StartTime,
            };
            round.HoleResults.AddRange(
            [
                new HoleResult { HoleNumber = 1, EntrySequence = 3, Score = 5, Putts = 3, TeeShotLeft = true, PenaltyStrokes = 1, GreensideSandShots = 1 },
                new HoleResult { HoleNumber = 2, EntrySequence = 1, Score = 4, Putts = 2, TeeShotRight = true, FairwaySandShots = 2 },
                new HoleResult { HoleNumber = 3, EntrySequence = 2, Score = 3, Putts = 1, Gir = true, TeeShotLong = true },
                new HoleResult { HoleNumber = 4, EntrySequence = 4, Score = 6, Putts = 0, FairwayHit = true, TeeShotShort = true },
            ]);
            db.Rounds.Add(round);
            await db.SaveChangesAsync();
            return (round, s);
        });
    }

    [Fact]
    public async Task Loader_maps_every_hole_field_and_orders_by_entry_sequence()
    {
        var (_, accountId) = await NewGolferAsync();
        await SeedDistinctiveRoundAsync(accountId);

        await using var scope = factory.Services.CreateAsyncScope();
        var rounds = await scope.ServiceProvider.GetRequiredService<RoundDataLoader>().LoadAsync(accountId, Ct);

        var round = Assert.Single(rounds);
        Assert.Equal("Mapping GC", round.CourseName);
        Assert.Equal("Gold", round.TeeName);
        Assert.Equal(new DateOnly(2025, 4, 2), round.PlayedOn);
        Assert.Equal(4, round.Holes.Count);
        Assert.Equal(
        [
            new HoleSummary(2, 1, 4, 4, 2, false, false, true, false, false, false, 0, 0, 2),
            new HoleSummary(3, 2, 3, 3, 1, false, false, false, true, false, true, 0, 0, 0),
            new HoleSummary(1, 3, 4, 5, 3, false, true, false, false, false, false, 1, 1, 0),
            new HoleSummary(4, 4, 5, 6, 0, true, false, false, false, true, false, 0, 0, 0),
        ], round.Holes);
        Assert.Equal(18, round.Score);
        Assert.Equal(16, round.Par);
        Assert.Equal(2, round.ToPar);
        Assert.Equal(6, round.Putts);
    }

    [Fact]
    public async Task Scorecard_maps_hole_results_and_totals_to_literals()
    {
        var (client, accountId) = await NewGolferAsync();
        var (round, _) = await SeedDistinctiveRoundAsync(accountId, "Card Mapping GC");

        var card = (await client.GetFromJsonAsync<ScorecardResponse>($"/rounds/{round.Id}/scorecard", Ct))!;

        Assert.Equal(4, card.HolesPlayed);
        Assert.Equal(18, card.Score);
        Assert.Equal(2, card.ToPar);
        Assert.Equal(6, card.Putts);
        Assert.Equal(72, card.Par);
        Assert.Equal(72.0m, card.Rating);
        Assert.Equal(125, card.Slope);
        Assert.Equal(new DateOnly(1900, 1, 1), card.TeeVersionEffectiveFrom);

        // Played holes come in the order they were played, then the unplayed ones by number.
        Assert.Equal([2, 3, 1, 4, 5, 6], card.Holes.Take(6).Select(h => h.Number));
        var h1 = card.Holes.Single(h => h.Number == 1);
        Assert.Equal((1, 4, 380, 7), (h1.Number, h1.Par, h1.Yardage, h1.StrokeIndex));
        Assert.Equal(new ScorecardHoleResultDto(3, 5, 3, false, true, false, false, false, false, 1, 1, 0), h1.Result);

        var h2 = card.Holes.Single(h => h.Number == 2);
        Assert.Equal((2, 4, 380, 11), (h2.Number, h2.Par, h2.Yardage, h2.StrokeIndex));
        Assert.Equal(new ScorecardHoleResultDto(1, 4, 2, false, false, true, false, false, false, 0, 0, 2), h2.Result);

        var h3 = card.Holes.Single(h => h.Number == 3);
        Assert.Equal((3, 3, 150, 15), (h3.Number, h3.Par, h3.Yardage, h3.StrokeIndex));
        Assert.Equal(new ScorecardHoleResultDto(2, 3, 1, false, false, false, true, false, true, 0, 0, 0), h3.Result);

        var h4 = card.Holes.Single(h => h.Number == 4);
        Assert.Equal((4, 5, 520, 1), (h4.Number, h4.Par, h4.Yardage, h4.StrokeIndex));
        Assert.Equal(new ScorecardHoleResultDto(4, 6, 0, true, false, false, false, true, false, 0, 0, 0), h4.Result);
    }

    [Fact]
    public async Task Renamed_tee_shows_the_new_name()
    {
        var (client, accountId) = await NewGolferAsync();
        var (_, seeded) = await SeedDistinctiveRoundAsync(accountId, "Tee Rename GC");
        await factory.WithDbAsync(async db =>
        {
            await db.Tees.Where(t => t.Id == seeded.Tee.Id).ExecuteUpdateAsync(
                s => s.SetProperty(t => t.Name, "Platinum"), Ct);
        });

        var results = await client.GetFromJsonAsync<ResultsResponse>("/stats/2025/results", Ct);
        var courses = await client.GetFromJsonAsync<CoursesResponse>("/stats/courses", Ct);

        Assert.Equal("Platinum", Assert.Single(results!.Rounds).TeeName);
        Assert.Equal("Platinum", Assert.Single(courses!.Courses).TeeName);
    }

    [Fact]
    public async Task Scorecard_of_a_soft_deleted_round_is_not_found()
    {
        var (client, accountId) = await NewGolferAsync();
        var (round, _) = await SeedDistinctiveRoundAsync(accountId, "Deleted Card GC");
        await factory.WithDbAsync(async db =>
        {
            await db.Rounds.Where(r => r.Id == round.Id).ExecuteUpdateAsync(
                s => s.SetProperty(r => r.DeletedAt, ApiFactory.StartTime), Ct);
        });

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/rounds/{round.Id}/scorecard", Ct)).StatusCode);
    }
}
