using System.Net;
using System.Net.Http.Json;
using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Features.Stats;
using CustomGolfStats.Api.Tests.Infrastructure;
using CustomGolfStats.Api.Tests.Rounds;
using CustomGolfStats.Domain;
using CustomGolfStats.Domain.Stats;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CustomGolfStats.Api.Tests.Stats;

public sealed class StatsEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static int _emailCounter;

    private async Task<(HttpClient Client, Guid AccountId)> NewGolferAsync()
    {
        var email = $"stats{Interlocked.Increment(ref _emailCounter)}@example.com";
        var client = await TestAuth.RegisterAsync(factory, email);
        return (client, await TestAuth.GetAccountIdAsync(factory, email));
    }

    /// <summary>Seeds an 18-hole round whose hole results vary by hole so every stat has something to count.</summary>
    private static async Task<Round> SeedRoundAsync(AppDbContext db, Guid accountId, TeeVersion version,
        DateOnly playedOn, int holeCount = 18, int offset = 0)
    {
        var round = await TestData.SeedQuickRoundAsync(db, accountId, version, playedOn,
            Enumerable.Range(0, holeCount).Select(i => version.Holes.Single(h => h.Number == i + 1).Par + ((i + offset) % 3) - 1).ToList());
        foreach (var hole in round.HoleResults)
        {
            var i = hole.HoleNumber + offset;
            hole.Putts = i % 4;
            hole.Gir = i % 2 == 0;
            hole.FairwayHit = i % 3 == 0;
            hole.TeeShotLeft = i % 5 == 0;
            hole.TeeShotRight = i % 7 == 0;
            hole.GreensideSandShots = i % 6 == 0 ? 1 : 0;
        }

        await db.SaveChangesAsync();
        return round;
    }

    private async Task<IReadOnlyList<RoundData>> DomainRoundsAsync(Guid accountId, int? year = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var loader = scope.ServiceProvider.GetRequiredService<RoundDataLoader>();
        var rounds = await loader.LoadAsync(accountId, Ct);
        return year is null ? rounds : SeasonStats.ForYear(rounds, year.Value);
    }

    private static void AssertRatio(Ratio expected, RatioDto actual)
    {
        Assert.Equal(expected.Numerator, actual.Numerator);
        Assert.Equal(expected.Denominator, actual.Denominator);
        Assert.Equal(expected.Value, actual.Value);
    }

    private static void AssertAverage(Average expected, AverageDto actual)
    {
        Assert.Equal(expected.Total, actual.Total);
        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected.Value, actual.Value);
    }

    private async Task<(HttpClient Client, Guid AccountId, SeededCourse Seeded)> SeededGolferAsync(int year, string courseName)
    {
        var (client, accountId) = await NewGolferAsync();
        var seeded = await factory.WithDbAsync(async db =>
        {
            var s = await TestData.SeedCourseAsync(db, courseName);
            await SeedRoundAsync(db, accountId, s.Version, new DateOnly(year, 5, 1));
            await SeedRoundAsync(db, accountId, s.Version, new DateOnly(year, 6, 1), holeCount: 9, offset: 1);
            await SeedRoundAsync(db, accountId, s.Version, new DateOnly(year - 1, 6, 1), offset: 2);
            return s;
        });
        return (client, accountId, seeded);
    }

    [Fact]
    public async Task Loader_uses_par_from_the_rounds_own_tee_version()
    {
        var (_, accountId) = await NewGolferAsync();
        await factory.WithDbAsync(async db =>
        {
            var s = await TestData.SeedCourseAsync(db, "Versioned", effectiveFrom: new DateOnly(2000, 1, 1));
            var v2 = await TestData.AddTeeVersionAsync(db, s.Tee, new DateOnly(2030, 1, 1),
                pars: [5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5]);
            await TestData.SeedQuickRoundAsync(db, accountId, s.Version, new DateOnly(2029, 1, 1), Enumerable.Repeat(4, 18).ToList());
            await TestData.SeedQuickRoundAsync(db, accountId, v2, new DateOnly(2030, 2, 1), Enumerable.Repeat(4, 18).ToList());
        });

        var rounds = await DomainRoundsAsync(accountId);

        Assert.Equal(2, rounds.Count);
        Assert.Equal(72, rounds[0].Par);
        Assert.Equal(90, rounds[1].Par);
    }

    [Fact]
    public async Task Years_lists_years_with_rounds_newest_first()
    {
        var (client, _, _) = await SeededGolferAsync(2025, "Years GC");

        var years = await client.GetFromJsonAsync<StatsYearsResponse>("/stats/years", Ct);

        Assert.Equal([2025, 2024], years!.Years);
    }

    [Fact]
    public async Task Overall_matches_domain_and_includes_handicap_on_or_before_year_end()
    {
        var (client, accountId, _) = await SeededGolferAsync(2025, "Overall GC");
        await factory.WithDbAsync(async db =>
        {
            db.HandicapEntries.AddRange(
                new HandicapEntry { AccountId = accountId, EffectiveOn = new DateOnly(2024, 3, 1), Index = 15.0m },
                new HandicapEntry { AccountId = accountId, EffectiveOn = new DateOnly(2025, 12, 31), Index = 12.4m },
                new HandicapEntry { AccountId = accountId, EffectiveOn = new DateOnly(2026, 1, 1), Index = 9.9m });
            await db.SaveChangesAsync();
        });

        var actual = await client.GetFromJsonAsync<OverallResponse>("/stats/2025/overall", Ct);
        var expected = SeasonStats.Overall(await DomainRoundsAsync(accountId, 2025));

        AssertAverage(expected.ScoringAverage18, actual!.ScoringAverage18);
        Assert.Equal(expected.HolesPlayed, actual.HolesPlayed);
        Assert.Equal(27, actual.HolesPlayed);
        Assert.Equal(expected.ToPar, actual.ToPar);
        Assert.Equal(12.4m, actual.HandicapIndex);

        var earlier = await client.GetFromJsonAsync<OverallResponse>("/stats/2024/overall", Ct);
        Assert.Equal(15.0m, earlier!.HandicapIndex);
        var before = await client.GetFromJsonAsync<OverallResponse>("/stats/2023/overall", Ct);
        Assert.Null(before!.HandicapIndex);
    }

    [Fact]
    public async Task Season_and_results_match_domain()
    {
        var (client, accountId, _) = await SeededGolferAsync(2025, "Season GC");
        var rounds = await DomainRoundsAsync(accountId, 2025);

        var season = await client.GetFromJsonAsync<SeasonResponse>("/stats/2025/season", Ct);
        var expectedSeason = SeasonStats.Season(rounds);
        Assert.Equal(expectedSeason.Rounds9, season!.Rounds9);
        Assert.Equal(expectedSeason.Rounds18, season.Rounds18);
        Assert.Equal(1, season.Rounds9);
        Assert.Equal(1, season.Rounds18);
        Assert.Equal(expectedSeason.Histogram18.Select(b => (b.Min, b.Max, b.Count)),
            season.Histogram18.Select(b => (b.Min, b.Max, b.Count)));

        var results = await client.GetFromJsonAsync<ResultsResponse>("/stats/2025/results", Ct);
        var expected = SeasonStats.Results(rounds);
        Assert.Equal(expected.Count, results!.Rounds.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].RoundId, results.Rounds[i].RoundId);
            Assert.Equal(expected[i].Score, results.Rounds[i].Score);
            Assert.Equal(expected[i].ToPar, results.Rounds[i].ToPar);
            Assert.Equal(expected[i].Putts, results.Rounds[i].Putts);
            Assert.Equal(expected[i].Par4ToPar, results.Rounds[i].Par4ToPar);
            AssertRatio(expected[i].Gir, results.Rounds[i].Gir);
            AssertRatio(expected[i].DrivingAccuracy, results.Rounds[i].DrivingAccuracy);
        }
    }

    [Fact]
    public async Task Scoring_off_the_tee_approach_putting_match_domain()
    {
        var (client, accountId, _) = await SeededGolferAsync(2025, "Sections GC");
        var rounds = await DomainRoundsAsync(accountId, 2025);

        var scoring = (await client.GetFromJsonAsync<ScoringResponse>("/stats/2025/scoring", Ct))!;
        var s = ScoringStats.Calculate(rounds);
        AssertAverage(s.ScoringAverage9, scoring.ScoringAverage9);
        AssertAverage(s.ScoringAverage18, scoring.ScoringAverage18);
        Assert.Equal((s.Lowest18, s.Highest18, s.Lowest9, s.Highest9), (scoring.Lowest18, scoring.Highest18, scoring.Lowest9, scoring.Highest9));
        Assert.Equal((s.Birdies, s.Eagles), (scoring.Birdies, scoring.Eagles));
        AssertRatio(s.ParBreakers, scoring.ParBreakers);
        AssertAverage(s.Par3Average, scoring.Par3Average);
        AssertAverage(s.Par4Average, scoring.Par4Average);
        AssertAverage(s.Par5Average, scoring.Par5Average);
        AssertRatio(s.BirdieOrBetter, scoring.BirdieOrBetter);

        var tee = (await client.GetFromJsonAsync<OffTheTeeResponse>("/stats/2025/off-the-tee", Ct))!;
        var t = OffTheTeeStats.Calculate(rounds);
        AssertRatio(t.DrivingAccuracy, tee.DrivingAccuracy);
        AssertRatio(t.LeftTendency, tee.LeftTendency);
        AssertRatio(t.RightTendency, tee.RightTendency);
        AssertAverage(t.Par4ToParFromFairway, tee.Par4ToParFromFairway);
        AssertAverage(t.Par4ToParFromLeft, tee.Par4ToParFromLeft);
        AssertAverage(t.Par4ToParFromRight, tee.Par4ToParFromRight);
        AssertAverage(t.Par5ToParFromFairway, tee.Par5ToParFromFairway);
        AssertAverage(t.Par5ToParFromLeft, tee.Par5ToParFromLeft);
        AssertAverage(t.Par5ToParFromRight, tee.Par5ToParFromRight);

        var approach = (await client.GetFromJsonAsync<ApproachResponse>("/stats/2025/approach", Ct))!;
        var a = ApproachStats.Calculate(rounds);
        AssertRatio(a.Gir, approach.Gir);
        AssertRatio(a.GirFromFairway, approach.GirFromFairway);
        AssertRatio(a.GirFromLeft, approach.GirFromLeft);
        AssertRatio(a.GirFromRight, approach.GirFromRight);
        AssertRatio(a.SandSave, approach.SandSave);
        AssertRatio(a.Scrambling, approach.Scrambling);
        AssertRatio(a.HoleOut, approach.HoleOut);

        var putting = (await client.GetFromJsonAsync<PuttingResponse>("/stats/2025/putting", Ct))!;
        var p = PuttingStats.Calculate(rounds);
        AssertAverage(p.PuttsPerGir, putting.PuttsPerGir);
        AssertAverage(p.PuttsPerHole, putting.PuttsPerHole);
        AssertRatio(p.OnePutt, putting.OnePutt);
        AssertRatio(p.ThreePuttRate, putting.ThreePuttRate);
        AssertAverage(p.PuttsPerRound9, putting.PuttsPerRound9);
        AssertAverage(p.PuttsPerRound18, putting.PuttsPerRound18);
        AssertRatio(p.BirdieConversion, putting.BirdieConversion);
    }

    [Fact]
    public async Task Career_and_courses_match_domain()
    {
        var (client, accountId, _) = await SeededGolferAsync(2025, "Career GC");
        var all = await DomainRoundsAsync(accountId);

        var career = (await client.GetFromJsonAsync<CareerResponse>("/stats/career", Ct))!;
        var expectedCareer = CareerStats.ByYear(all);
        Assert.Equal(expectedCareer.Select(r => r.Year), career.Years.Select(r => r.Year));
        for (var i = 0; i < expectedCareer.Count; i++)
        {
            var e = expectedCareer[i];
            var a = career.Years[i];
            Assert.Equal((e.Holes, e.ToPar, e.Earliest, e.Latest), (a.Holes, a.ToPar, a.Earliest, a.Latest));
            Assert.Equal((e.Nine.Rounds, e.Eighteen.Rounds, e.Eighteen.Low, e.Eighteen.High),
                (a.Nine.Rounds, a.Eighteen.Rounds, a.Eighteen.Low, a.Eighteen.High));
            AssertAverage(e.PuttsPerHole, a.PuttsPerHole);
            AssertRatio(e.Gir, a.Gir);
            AssertRatio(e.DrivingAccuracy, a.DrivingAccuracy);
        }

        var courses = (await client.GetFromJsonAsync<CoursesResponse>("/stats/courses", Ct))!;
        var expectedCourses = CareerStats.ByCourseAndTee(all);
        Assert.Single(courses.Courses);
        Assert.Equal(expectedCourses[0].CourseName, courses.Courses[0].CourseName);
        Assert.Equal((expectedCourses[0].Holes, expectedCourses[0].ToPar), (courses.Courses[0].Holes, courses.Courses[0].ToPar));
        Assert.Equal(expectedCourses[0].Eighteen.Rounds, courses.Courses[0].Eighteen.Rounds);
        AssertAverage(expectedCourses[0].Par4ToPar, courses.Courses[0].Par4ToPar);
        AssertRatio(expectedCourses[0].Gir, courses.Courses[0].Gir);

        var byYear = (await client.GetFromJsonAsync<CoursesResponse>("/stats/courses?year=2024", Ct))!;
        var expectedByYear = CareerStats.ByCourseAndTee(SeasonStats.ForYear(all, 2024));
        Assert.Equal(expectedByYear[0].Holes, Assert.Single(byYear.Courses).Holes);
        Assert.Equal(18, byYear.Courses[0].Holes);
    }

    [Fact]
    public async Task Handicap_history_is_oldest_first_and_only_the_golfers_own()
    {
        var (client, accountId) = await NewGolferAsync();
        var (_, otherId) = await NewGolferAsync();
        await factory.WithDbAsync(async db =>
        {
            db.HandicapEntries.AddRange(
                new HandicapEntry { AccountId = accountId, EffectiveOn = new DateOnly(2025, 6, 1), Index = 11.0m },
                new HandicapEntry { AccountId = accountId, EffectiveOn = new DateOnly(2024, 6, 1), Index = 14.2m },
                new HandicapEntry { AccountId = otherId, EffectiveOn = new DateOnly(2025, 1, 1), Index = 3.0m });
            await db.SaveChangesAsync();
        });

        var history = await client.GetFromJsonAsync<HandicapHistoryResponse>("/stats/handicap", Ct);

        Assert.Equal([14.2m, 11.0m], history!.Entries.Select(e => e.Index));
    }

    [Fact]
    public async Task Empty_year_returns_nulls_not_errors()
    {
        var (client, _) = await NewGolferAsync();

        var overall = await client.GetFromJsonAsync<OverallResponse>("/stats/2031/overall", Ct);
        Assert.Null(overall!.ScoringAverage18.Value);
        Assert.Null(overall.HandicapIndex);
        Assert.Equal(0, overall.HolesPlayed);

        var scoring = await client.GetFromJsonAsync<ScoringResponse>("/stats/2031/scoring", Ct);
        Assert.Null(scoring!.Lowest18);
        Assert.Null(scoring.ParBreakers.Value);

        var putting = await client.GetFromJsonAsync<PuttingResponse>("/stats/2031/putting", Ct);
        Assert.Null(putting!.PuttsPerHole.Value);

        Assert.Empty((await client.GetFromJsonAsync<ResultsResponse>("/stats/2031/results", Ct))!.Rounds);
        Assert.Empty((await client.GetFromJsonAsync<StatsYearsResponse>("/stats/years", Ct))!.Years);
        Assert.Empty((await client.GetFromJsonAsync<CareerResponse>("/stats/career", Ct))!.Years);
        Assert.Empty((await client.GetFromJsonAsync<CoursesResponse>("/stats/courses", Ct))!.Courses);
    }

    [Fact]
    public async Task Renamed_course_shows_the_new_name()
    {
        var (client, _, seeded) = await SeededGolferAsync(2025, "Old Name GC");
        await factory.WithDbAsync(async db =>
        {
            await db.Courses.Where(c => c.Id == seeded.Course.Id).ExecuteUpdateAsync(
                s => s.SetProperty(c => c.Name, "New Name GC"), Ct);
        });

        var results = await client.GetFromJsonAsync<ResultsResponse>("/stats/2025/results", Ct);
        var courses = await client.GetFromJsonAsync<CoursesResponse>("/stats/courses", Ct);

        Assert.All(results!.Rounds, r => Assert.Equal("New Name GC", r.CourseName));
        Assert.Equal("New Name GC", Assert.Single(courses!.Courses).CourseName);
    }

    [Fact]
    public async Task Deleted_rounds_are_excluded()
    {
        var (client, accountId, _) = await SeededGolferAsync(2025, "Deleted GC");
        await factory.WithDbAsync(async db =>
        {
            var nine = await db.Rounds.Where(r => r.AccountId == accountId && r.PlayedOn == new DateOnly(2025, 6, 1)).SingleAsync(Ct);
            nine.DeletedAt = ApiFactory.StartTime;
            await db.SaveChangesAsync(Ct);
        });

        var overall = await client.GetFromJsonAsync<OverallResponse>("/stats/2025/overall", Ct);

        Assert.Equal(18, overall!.HolesPlayed);
        Assert.Single((await client.GetFromJsonAsync<ResultsResponse>("/stats/2025/results", Ct))!.Rounds);
    }

    [Fact]
    public async Task Another_golfers_rounds_are_never_included()
    {
        var (_, _, _) = await SeededGolferAsync(2025, "Other GC");
        var (client, _) = await NewGolferAsync();

        var results = await client.GetFromJsonAsync<ResultsResponse>("/stats/2025/results", Ct);
        var years = await client.GetFromJsonAsync<StatsYearsResponse>("/stats/years", Ct);

        Assert.Empty(results!.Rounds);
        Assert.Empty(years!.Years);
    }

    [Fact]
    public async Task Scorecard_has_tee_version_holes_with_results_and_hides_other_golfers_rounds()
    {
        var (client, accountId) = await NewGolferAsync();
        var (otherClient, _) = await NewGolferAsync();
        var (round, seeded) = await factory.WithDbAsync(async db =>
        {
            var s = await TestData.SeedCourseAsync(db, "Card GC", teeName: "Blue");
            var r = await SeedRoundAsync(db, accountId, s.Version, new DateOnly(2025, 5, 1), holeCount: 9);
            return (r, s);
        });

        var card = await client.GetFromJsonAsync<ScorecardResponse>($"/rounds/{round.Id}/scorecard", Ct);

        Assert.Equal("Card GC", card!.CourseName);
        Assert.Equal("Blue", card.TeeName);
        Assert.Equal(seeded.Version.Slope, card.Slope);
        Assert.Equal(18, card.Holes.Count);
        Assert.Equal(9, card.HolesPlayed);
        Assert.Equal(TestData.DefaultPars[3], card.Holes[3].Par);
        Assert.Equal(520, card.Holes[3].Yardage);
        Assert.Equal(TestData.DefaultStrokeIndexes[3], card.Holes[3].StrokeIndex);
        Assert.NotNull(card.Holes[8].Result);
        Assert.Null(card.Holes[9].Result);
        Assert.Equal(card.Holes.Where(h => h.Result is not null).Sum(h => h.Result!.Score), card.Score);

        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync($"/rounds/{round.Id}/scorecard", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/rounds/{Guid.NewGuid()}/scorecard", Ct)).StatusCode);
    }

    [Fact]
    public async Task Scorecard_lists_a_hole_played_twice_in_entry_order_then_unplayed_holes()
    {
        var (client, _) = await NewGolferAsync();
        var teeId = await factory.WithDbAsync(async db => (await TestData.SeedCourseAsync(db, "Twice GC")).Tee.Id);
        var roundId = Guid.NewGuid();
        var push = await RoundRequests.PutAsync(client, roundId, RoundRequests.Quick(teeId, factory.Time.GetUtcNow(),
            RoundRequests.QuickHole(1, 5, sequence: 1),
            RoundRequests.QuickHole(2, 4, sequence: 2),
            RoundRequests.QuickHole(1, 3, sequence: 3)));
        Assert.Equal(HttpStatusCode.OK, push.StatusCode);

        var response = await client.GetAsync($"/rounds/{roundId}/scorecard", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var card = (await response.Content.ReadFromJsonAsync<ScorecardResponse>(TestJson.Options, Ct))!;

        Assert.Equal(3, card.HolesPlayed);
        Assert.Equal(12, card.Score);
        Assert.Equal(12 - (2 * TestData.DefaultPars[0]) - TestData.DefaultPars[1], card.ToPar);
        Assert.Equal([1, 2, 1], card.Holes.Take(3).Select(h => h.Number));
        Assert.Equal([1, 2, 3], card.Holes.Take(3).Select(h => h.Result!.EntrySequence));
        Assert.Equal([5, 4, 3], card.Holes.Take(3).Select(h => h.Result!.Score));
        Assert.Equal(TestData.DefaultPars[0], card.Holes[2].Par);
        Assert.Equal(Enumerable.Range(3, 16), card.Holes.Skip(3).Select(h => h.Number));
        Assert.All(card.Holes.Skip(3), h => Assert.Null(h.Result));
    }

    [Fact]
    public async Task Stats_skip_hole_results_whose_hole_the_tee_version_lacks()
    {
        var (client, accountId) = await NewGolferAsync();
        await factory.WithDbAsync(async db =>
        {
            var s = await TestData.SeedCourseAsync(db, "Gap GC");
            await TestData.SeedQuickRoundAsync(db, accountId, s.Version, new DateOnly(2025, 5, 1), [4, 4, 3]);
            // Bypasses the editor's guard to simulate data that lost a played hole.
            await db.TeeHoles.Where(h => h.TeeVersionId == s.Version.Id && h.Number == 2).ExecuteDeleteAsync(Ct);
        });

        var response = await client.GetAsync("/stats/2025/scoring", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var round = Assert.Single(await DomainRoundsAsync(accountId));
        Assert.Equal([1, 3], round.Holes.Select(h => h.HoleNumber));
    }

    [Fact]
    public async Task Stats_routes_require_authentication()
    {
        var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/stats/years", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/stats/2025/overall", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/rounds/{Guid.NewGuid()}/scorecard", Ct)).StatusCode);
    }
}
