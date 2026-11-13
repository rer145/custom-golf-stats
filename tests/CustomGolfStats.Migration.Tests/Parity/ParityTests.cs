using CustomGolfStats.Api.Data;
using CustomGolfStats.Migration.Import;
using CustomGolfStats.Migration.Mapping;
using CustomGolfStats.Migration.Parity;
using CustomGolfStats.Migration.Source;
using CustomGolfStats.Migration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Migration.Tests.Parity;

/// <summary>
/// golftrack's procedures from 03-sprocs.sql against a small synthetic history, imported, then compared. Pars of the
/// tee: 4 4 3 5 4 4 3 4 5 (twice), 72.
/// <list type="bullet">
/// <item>2024-05-01, 18 holes: bogeys, except a birdie on hole 1 (GIR, 1 putt) and an albatross on hole 4 (par 5, 2
/// strokes, 0 putts, GIR). Fairway flag on holes 1, 2 and on hole 3 (a par 3), tee shot left on hole 5, a sand shot on 6.</item>
/// <item>2024-06-01, 18 holes: all pars, 2 putts, GIR, fairways hit on the par 4s and 5s.</item>
/// <item>2024-07-01, 9 holes (front nine): bogeys.</item>
/// <item>2023-08-01, 18 holes: all pars.</item>
/// </list>
/// </summary>
public sealed class ParityTests(MsSqlFixture sql, PostgresFixture postgres) : IAsyncLifetime
{
    private const string Email = "parity@example.test";
    private static readonly int[] Pars = [4, 4, 3, 5, 4, 4, 3, 4, 5, 4, 4, 3, 5, 4, 4, 3, 4, 5];

    private string _source = null!;
    private string _target = null!;
    private int _golferId;
    private int _allParsRound;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _source = await sql.CreateDatabaseAsync(withProcedures: true);
        _target = await postgres.CreateDatabaseAsync();
        await SeedAsync();
        await using var db = CreateTarget();
        db.Users.Add(new Api.Data.Entities.Account { UserName = Email, NormalizedUserName = Email.ToUpperInvariant(), Email = Email, NormalizedEmail = Email.ToUpperInvariant() });
        await db.SaveChangesAsync(Ct);
        await new Importer(new GolftrackReader(_source), db, TimeProvider.System).RunAsync(_golferId, Email, dryRun: false, Ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Plain_stats_match_and_rule_differences_are_intended()
    {
        var result = await RunAsync();

        Assert.Equal([2023, 2024], result.Years.Select(y => y.Year));
        var y2024 = result.Years.Single(y => y.Year == 2024);
        Assert.Equal(3, y2024.Context.ImportedRounds);
        Assert.Equal(0, y2024.Context.OtherGolfersRounds);

        Assert.Equal(Verdict.Match, Row(y2024, "Scoring", "TotalHoles").Verdict);
        Assert.Equal(45, Row(y2024, "Scoring", "TotalHoles").New);
        Assert.Equal(Verdict.Match, Row(y2024, "Scoring", "Birdies").Verdict);
        Assert.Equal(Verdict.Match, Row(y2024, "Scoring", "Avg18HoleScore").Verdict);
        Assert.Equal(Verdict.Match, Row(y2024, "Putting", "AvgPutts9Holes").Verdict);
        Assert.Equal(Verdict.Match, Row(y2024, "Career (this year)", "RelatedToPar").Verdict);
        Assert.Equal(Verdict.Match, Row(y2024, "Career (this year)", "EarliestRound").Verdict);

        var eagles = Row(y2024, "Scoring", "Eagles");
        Assert.Equal((0d, 1d), (eagles.Golftrack!.Value, eagles.New!.Value));
        Assert.Equal(Verdict.Intended, eagles.Verdict);
        Assert.Equal(IntendedRules.EaglesIncludeAlbatrosses, eagles.Rule);
        Assert.Equal(Verdict.Intended, Row(y2024, "Scoring", "ParBreakers").Verdict);
        Assert.Equal(Verdict.Intended, Row(y2024, "Off the tee", "FairwaysHit").Verdict);
        Assert.Equal(IntendedRules.ParThreeTeeShotsIgnored, Row(y2024, "Approach", "TeeShotFairway").Rule);

        // Only golftrack's Round_SeasonSummary (which counts holes, not rounds) differs without a rule.
        var unexplained = result.Years.SelectMany(y => y.Sections).Where(s => s.Rows.Any(r => r.Verdict == Verdict.Unexplained));
        Assert.All(unexplained, s => Assert.Equal("Round_SeasonSummary", s.Procedure));
        Assert.Equal(Verdict.NotCompared, Row(y2024, "Off the tee", "ShortTeeHit").Verdict);

        var course = Assert.Single(y2024.Sections, s => s.Procedure == "Stats_CourseStats");
        Assert.Equal("Course: Parity Links / White", course.Title);
        Assert.DoesNotContain(course.Rows, r => r.Verdict == Verdict.Unexplained);

        var markdown = ParityReport.ToMarkdown(result, Email, DateTimeOffset.UnixEpoch);
        Assert.Contains("| Eagles | 0 | 1 | +1 | intended: Eagles include albatrosses |", markdown, StringComparison.Ordinal);
        Assert.Contains("| TotalHoles | 45 | 45 | 0 | match |", markdown, StringComparison.Ordinal);
        Assert.Contains("## Intended differences", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tampered_imported_hole_is_unexplained()
    {
        await using (var db = CreateTarget())
        {
            var roundId = DeterministicIds.For("Round", _allParsRound);
            var hole = await db.HoleResults.SingleAsync(h => h.RoundId == roundId && h.HoleNumber == 1, Ct);
            hole.Score += 1;
            await db.SaveChangesAsync(Ct);
        }

        var result = await RunAsync();

        var y2024 = result.Years.Single(y => y.Year == 2024);
        var par4 = Row(y2024, "Scoring", "TotalPar4Score");
        Assert.Equal(Verdict.Unexplained, par4.Verdict);
        Assert.Equal(1, par4.New - par4.Golftrack);
        Assert.Equal(Verdict.Unexplained, Row(y2024, "Career (this year)", "RelatedToPar").Verdict);
        Assert.Equal(Verdict.Match, Row(y2024, "Scoring", "TotalHoles").Verdict);
        Assert.Contains("| TotalPar4Score |", ParityReport.ToMarkdown(result, Email, DateTimeOffset.UnixEpoch), StringComparison.Ordinal);
        Assert.Contains("**unexplained**", ParityReport.ToMarkdown(result, Email, DateTimeOffset.UnixEpoch), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_imported_is_a_user_error()
    {
        await using var db = CreateTarget();
        db.Users.Add(new Api.Data.Entities.Account { UserName = "empty@example.test", NormalizedUserName = "EMPTY@EXAMPLE.TEST", Email = "empty@example.test", NormalizedEmail = "EMPTY@EXAMPLE.TEST" });
        await db.SaveChangesAsync(Ct);

        var runner = new ParityRunner(new GolftrackReader(_source), new LegacyStatsReader(_source), db, TimeProvider.System);
        var error = await Assert.ThrowsAsync<MigrationUserException>(() => runner.RunAsync(_golferId, "empty@example.test", Ct));

        Assert.Contains("Run import first", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_the_statistics_procedures_can_be_run()
    {
        var reader = new LegacyStatsReader(_source);

        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.RunAsync("Round_Delete", new Dictionary<string, int>(), Ct));
    }

    private static ParityRow Row(ParityYear year, string section, string column) =>
        Assert.Single(Assert.Single(year.Sections, s => s.Title == section).Rows, r => r.Column == column);

    private async Task<ParityResult> RunAsync()
    {
        await using var db = CreateTarget();
        return await new ParityRunner(new GolftrackReader(_source), new LegacyStatsReader(_source), db, TimeProvider.System)
            .RunAsync(_golferId, Email, Ct);
    }

    private AppDbContext CreateTarget() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseAppDatabase(_target).Options, TimeProvider.System);

    private async Task SeedAsync()
    {
        var seed = new LegacySeed(_source);
        _golferId = await seed.GolferAsync("Pat");
        var course = await seed.CourseAsync("Parity Links");
        var tee = await seed.TeeAsync(course);
        var holes = new List<int>();
        for (var i = 0; i < 18; i++)
        {
            holes.Add(await seed.HoleAsync(tee, i + 1, par: Pars[i]));
        }

        // 2024-05-01: bogeys, birdie on 1, albatross on 4.
        var scores = Pars.Select(p => p + 1).ToArray();
        scores[0] = 3;
        scores[3] = 2;
        var mixed = await seed.RoundAsync(_golferId, tee, new DateTime(2024, 5, 1), score: scores.Sum());
        for (var i = 0; i < 18; i++)
        {
            await seed.RoundDetailAsync(
                mixed, holes[i], sequenceNumber: i + 1, score: scores[i],
                putts: i == 0 ? 1 : i == 3 ? 0 : 2,
                gir: i is 0 or 3,
                fairwayHit: i is 0 or 1 or 2,
                teeShotLeft: i == 4,
                sandShots: i == 5 ? 1 : 0);
        }

        _allParsRound = await AllParsAsync(seed, tee, holes, new DateTime(2024, 6, 1));

        var nine = await seed.RoundAsync(_golferId, tee, new DateTime(2024, 7, 1), score: Pars.Take(9).Sum() + 9, holesPlayed: 9);
        for (var i = 0; i < 9; i++)
        {
            await seed.RoundDetailAsync(nine, holes[i], score: Pars[i] + 1, putts: 2);
        }

        await AllParsAsync(seed, tee, holes, new DateTime(2023, 8, 1));
    }

    private async Task<int> AllParsAsync(LegacySeed seed, int tee, List<int> holes, DateTime playedOn)
    {
        var round = await seed.RoundAsync(_golferId, tee, playedOn, score: 72);
        for (var i = 0; i < 18; i++)
        {
            await seed.RoundDetailAsync(round, holes[i], score: Pars[i], putts: 2, gir: true, fairwayHit: Pars[i] != 3);
        }

        return round;
    }
}
