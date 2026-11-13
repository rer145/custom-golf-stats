using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Features.Stats;
using CustomGolfStats.Domain;
using CustomGolfStats.Domain.Stats;
using CustomGolfStats.Migration.Import;
using CustomGolfStats.Migration.Mapping;
using CustomGolfStats.Migration.Source;
using CustomGolfStats.Migration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomGolfStats.Migration.Tests.Import;

public sealed class ImporterTests(MsSqlFixture sql, PostgresFixture postgres) : IAsyncLifetime
{
    private MigrationScenario _scenario = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _scenario = await MigrationScenario.CreateAsync(sql, postgres);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<ImportResult> ImportAsync(
        bool dryRun = false, string email = MigrationScenario.AccountEmail, int? golferId = null, bool replaceOtherGolfer = false)
    {
        await using var db = _scenario.CreateTargetContext();
        var importer = new Importer(new GolftrackReader(_scenario.SourceConnectionString), db, TimeProvider.System);
        return await importer.RunAsync(golferId ?? _scenario.GolferId, email, dryRun, replaceOtherGolfer, Ct);
    }

    [Fact]
    public async Task Imports_into_an_empty_target()
    {
        var result = await ImportAsync(email: "  ann.golfer@EXAMPLE.test ");

        Assert.True(result.Committed);
        Assert.Equal(_scenario.AccountId, result.AccountId);
        Assert.Equal(new ImportCounts(2, 0, 2, 27, 4, 63, 2, 0, 0, 0), result.Counts);

        await using var db = _scenario.CreateTargetContext();
        Assert.Equal(2, await db.Courses.CountAsync(c => c.Source == CourseSource.Golftrack && c.Status == CourseStatus.Verified, Ct));
        Assert.Equal(4, await db.Rounds.CountAsync(r => r.AccountId == _scenario.AccountId, Ct));
        Assert.Equal(63, await db.HoleResults.CountAsync(Ct));
        Assert.Equal(
            [(new DateOnly(2023, 1, 1), 12.4m), (new DateOnly(2024, 1, 1), 11.8m)],
            (await db.HandicapEntries.Where(h => h.AccountId == _scenario.AccountId).OrderBy(h => h.EffectiveOn).ToListAsync(Ct))
                .Select(h => (h.EffectiveOn, h.Index)));

        var first = await db.Rounds.SingleAsync(r => r.PlayedOn == new DateOnly(2023, 5, 1), Ct);
        Assert.Equal(EntryMode.Quick, first.EntryMode);
        Assert.Equal(RoundType.League, first.RoundType);
        Assert.Equal("Clear; Breezy; Mild", first.LegacyWeather);
        Assert.Equal(250, first.DurationMinutes);
        Assert.Equal(5.2m, first.DistanceMiles);
        Assert.False(first.TrialCounted);
        var account = await db.Users.SingleAsync(a => a.Id == _scenario.AccountId, Ct);
        Assert.Equal(0, account.TrialShotRoundsUsed);
    }

    [Fact]
    public async Task Stats_work_on_the_imported_rounds()
    {
        await ImportAsync();

        await using var db = _scenario.CreateTargetContext();
        var rounds = await new RoundDataLoader(db, NullLogger<RoundDataLoader>.Instance).LoadAsync(_scenario.AccountId, Ct);

        Assert.Equal(4, rounds.Count);
        Assert.All(rounds, r => Assert.NotEmpty(r.Holes));
        var byYear = CareerStats.ByYear(rounds);
        var y2024 = Assert.Single(byYear, r => r.Year == 2024);
        Assert.Equal(2, y2024.Eighteen.Rounds);
        Assert.Equal(72, y2024.Eighteen.Low);
        Assert.Equal(1, y2024.Nine.Rounds);
        Assert.Equal(54, y2024.Nine.Low);
        Assert.Equal(45, y2024.Holes);
        Assert.Equal(18, y2024.ToPar); // 0 on the two 72s, +18 on the nine (all 6s on par 4s)
        var y2023 = Assert.Single(byYear, r => r.Year == 2023);
        Assert.Equal(90, y2023.Eighteen.High);
        var putting = PuttingStats.Calculate(SeasonStats.ForYear(rounds, 2024));
        Assert.Equal(new Average(68, 2), putting.PuttsPerRound18); // the 0-putt hole 7 in each copy
        Assert.Equal(new Average(18, 1), putting.PuttsPerRound9);
    }

    [Fact]
    public async Task Second_run_gives_identical_rows()
    {
        var first = await ImportAsync();
        var before = await SnapshotAsync();

        var second = await ImportAsync();
        var after = await SnapshotAsync();

        Assert.Equal(before, after);
        Assert.Equal(first.Counts with { RoundsRemoved = 4, HandicapEntriesRemoved = 2, CoursesRemoved = 2 }, second.Counts);
        Assert.Equal(first.Mapped.Findings.Count, second.Mapped.Findings.Count);
    }

    [Fact]
    public async Task Rerun_removes_rounds_deactivated_in_golftrack_since_the_last_run()
    {
        await ImportAsync();
        await SetRoundActiveAsync(_scenario.InactiveRoundId, true);
        await ImportAsync();
        await using (var db = _scenario.CreateTargetContext())
        {
            Assert.Equal(5, await db.Rounds.CountAsync(Ct));
        }

        await SetRoundActiveAsync(_scenario.InactiveRoundId, false);
        var result = await ImportAsync();

        Assert.Equal(5, result.Counts.RoundsRemoved);
        await using var check = _scenario.CreateTargetContext();
        Assert.Equal(4, await check.Rounds.CountAsync(Ct));
        Assert.False(await check.Rounds.AnyAsync(r => r.Id == DeterministicIds.For("Round", _scenario.InactiveRoundId), Ct));
    }

    [Fact]
    public async Task Leaves_other_accounts_api_courses_and_the_golfers_own_rounds_untouched()
    {
        await ImportAsync();
        var other = await _scenario.CreateAccountAsync("other@example.test");
        var pineValleyVersion = DeterministicIds.For("TeeVersion", _scenario.PineValleyTeeId);
        Guid apiCourseId, othersRoundOnApiCourse, othersRoundOnGolftrackCourse, ownAppRound;
        DateTimeOffset pineValleyUpdatedAt;
        var earlier = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using (var db = _scenario.CreateTargetContext())
        {
            var apiCourse = ApiCourse();
            db.Courses.Add(apiCourse);
            apiCourseId = apiCourse.Id;
            var apiVersion = apiCourse.Tees[0].Versions[0];
            othersRoundOnApiCourse = AddRound(db, other, apiVersion.Id, earlier);
            othersRoundOnGolftrackCourse = AddRound(db, other, pineValleyVersion, earlier);
            ownAppRound = AddRound(db, _scenario.AccountId, pineValleyVersion, earlier);
            db.HandicapEntries.Add(new HandicapEntry { Id = Guid.NewGuid(), AccountId = _scenario.AccountId, EffectiveOn = new DateOnly(2025, 1, 1), Index = 9.9m });
            await db.SaveChangesAsync(Ct);
            pineValleyUpdatedAt = (await db.Courses.SingleAsync(c => c.Name == "Pine Valley", Ct)).UpdatedAt;
        }

        var result = await ImportAsync();

        Assert.Equal(1, result.Counts.CoursesReused);
        Assert.Equal(1, result.Counts.CoursesWritten);
        Assert.Equal(1, result.Counts.CoursesRemoved);
        await using var check = _scenario.CreateTargetContext();
        Assert.True(await check.Courses.AnyAsync(c => c.Id == apiCourseId && c.Source == CourseSource.GolfCourseApi, Ct));
        Assert.Equal(18, await check.TeeHoles.CountAsync(h => h.TeeVersion.Tee.CourseId == apiCourseId, Ct));
        foreach (var id in new[] { othersRoundOnApiCourse, othersRoundOnGolftrackCourse, ownAppRound })
        {
            var round = await check.Rounds.Include(r => r.HoleResults).SingleAsync(r => r.Id == id, Ct);
            Assert.Equal(earlier, round.ModifiedAt);
            Assert.Single(round.HoleResults);
        }

        Assert.Equal(pineValleyUpdatedAt, (await check.Courses.SingleAsync(c => c.Name == "Pine Valley", Ct)).UpdatedAt);
        Assert.Equal(1, await check.Rounds.CountAsync(r => r.AccountId == other && r.TeeVersionId == pineValleyVersion, Ct));
        Assert.Equal(5, await check.Rounds.CountAsync(r => r.AccountId == _scenario.AccountId, Ct));
        Assert.Equal(3, await check.HandicapEntries.CountAsync(h => h.AccountId == _scenario.AccountId, Ct));
    }

    [Fact]
    public async Task Dry_run_commits_nothing()
    {
        var result = await ImportAsync(dryRun: true);

        Assert.False(result.Committed);
        Assert.Equal(4, result.Counts.Rounds);
        await using var db = _scenario.CreateTargetContext();
        Assert.Equal(0, await db.Courses.CountAsync(Ct));
        Assert.Equal(0, await db.Rounds.CountAsync(Ct));
        Assert.Equal(0, await db.HandicapEntries.CountAsync(Ct));
    }

    [Fact]
    public async Task Dry_run_after_an_import_leaves_it_in_place()
    {
        await ImportAsync();
        var before = await SnapshotAsync();

        var result = await ImportAsync(dryRun: true);

        Assert.Equal(4, result.Counts.RoundsRemoved);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task Golfer_without_active_rounds_is_a_user_error_and_changes_nothing()
    {
        await ImportAsync();
        var before = await SnapshotAsync();

        var error = await Assert.ThrowsAsync<MigrationUserException>(() => ImportAsync(golferId: 9999));

        Assert.Contains("9999 has no active rounds", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task Replacing_another_golfers_import_is_refused_without_the_flag()
    {
        await ImportAsync();
        var before = await SnapshotAsync();

        var error = await Assert.ThrowsAsync<MigrationUserException>(() => ImportAsync(golferId: _scenario.OtherGolferId));

        Assert.Contains("--replace-other-golfer", error.Message, StringComparison.Ordinal);
        Assert.Contains("4 rounds and 2 handicap entries", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task Replacing_another_golfers_import_with_the_flag_counts_what_it_clears()
    {
        await ImportAsync();

        var result = await ImportAsync(golferId: _scenario.OtherGolferId, replaceOtherGolfer: true);

        Assert.Equal((4, 2), (result.Counts.RoundsRemovedFromOtherGolfers, result.Counts.HandicapEntriesRemovedFromOtherGolfers));
        Assert.Equal(1, result.Counts.Rounds);
        await using var db = _scenario.CreateTargetContext();
        Assert.Equal(1, await db.Rounds.CountAsync(r => r.AccountId == _scenario.AccountId, Ct));
    }

    [Fact]
    public async Task Missing_account_is_a_user_error()
    {
        var error = await Assert.ThrowsAsync<MigrationUserException>(() => ImportAsync(email: "nobody@example.test"));

        Assert.Contains("nobody@example.test", error.Message, StringComparison.Ordinal);
        await using var db = _scenario.CreateTargetContext();
        Assert.Equal(0, await db.Courses.CountAsync(Ct));
    }

    [Fact]
    public async Task Rounds_already_imported_into_another_account_are_a_user_error()
    {
        await ImportAsync();
        var other = await _scenario.CreateAccountAsync("other@example.test");
        var before = await SnapshotAsync();

        var error = await Assert.ThrowsAsync<MigrationUserException>(() => ImportAsync(email: "other@example.test"));

        Assert.Contains("another account", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, await SnapshotAsync());
        await using var db = _scenario.CreateTargetContext();
        Assert.Equal(0, await db.Rounds.CountAsync(r => r.AccountId == other, Ct));
    }

    private static Course ApiCourse()
    {
        var version = new TeeVersion
        {
            Id = Guid.NewGuid(),
            EffectiveFrom = new DateOnly(1900, 1, 1),
            Par = 72,
            Rating = 71.0m,
            Slope = 130,
            Source = CourseSource.GolfCourseApi,
        };
        version.Holes.AddRange(Enumerable.Range(1, 18).Select(n => new TeeHole { Id = Guid.NewGuid(), Number = n, Par = 4, Yardage = 400, StrokeIndex = n }));
        var tee = new Tee { Id = Guid.NewGuid(), Name = "Blue", Gender = Gender.Male };
        tee.Versions.Add(version);
        var course = new Course
        {
            Id = Guid.NewGuid(),
            Name = "Api Course",
            Status = CourseStatus.Imported,
            Source = CourseSource.GolfCourseApi,
            ProviderCourseId = "12345",
        };
        course.Tees.Add(tee);
        return course;
    }

    private static Guid AddRound(AppDbContext db, Guid accountId, Guid teeVersionId, DateTimeOffset at)
    {
        var round = new Round
        {
            Id = Guid.CreateVersion7(),
            AccountId = accountId,
            TeeVersionId = teeVersionId,
            PlayedOn = new DateOnly(2025, 6, 1),
            EntryMode = EntryMode.Quick,
            ClientUpdatedAt = at,
            ModifiedAt = at,
        };
        round.HoleResults.Add(new HoleResult { Id = Guid.CreateVersion7(), HoleNumber = 1, EntrySequence = 1, Score = 4, Putts = 2 });
        db.Rounds.Add(round);
        return round.Id;
    }

    private async Task SetRoundActiveAsync(int roundId, bool active)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(_scenario.SourceConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new Microsoft.Data.SqlClient.SqlCommand("UPDATE dbo.Round SET IsActive = @a WHERE Id = @id", connection);
        command.Parameters.AddWithValue("@a", active);
        command.Parameters.AddWithValue("@id", roundId);
        await command.ExecuteNonQueryAsync(Ct);
    }

    /// <summary>Every id and the main values of every golf row in the target, sorted, as one comparable string.</summary>
    private async Task<string> SnapshotAsync()
    {
        await using var db = _scenario.CreateTargetContext();
        var lines = new List<string>();
        lines.AddRange((await db.Courses.ToListAsync(Ct)).Select(c => $"course {c.Id} {c.Name} {c.Source} {c.ProviderCourseId} {c.Status} {c.Latitude}"));
        lines.AddRange((await db.Tees.ToListAsync(Ct)).Select(t => $"tee {t.Id} {t.CourseId} {t.Name} {t.Gender}"));
        lines.AddRange((await db.TeeVersions.ToListAsync(Ct)).Select(v => $"version {v.Id} {v.TeeId} {v.Par} {v.Rating} {v.Slope} {v.FrontRating} {v.BackSlope}"));
        lines.AddRange((await db.TeeHoles.ToListAsync(Ct)).Select(h => $"hole {h.Id} {h.TeeVersionId} {h.Number} {h.Par} {h.Yardage} {h.StrokeIndex}"));
        lines.AddRange((await db.Rounds.ToListAsync(Ct)).Select(r =>
            $"round {r.Id} {r.AccountId} {r.TeeVersionId} {r.PlayedOn} {r.RoundType} {r.LegacyWeather} {r.DurationMinutes} {r.DistanceMiles} {r.Notes} {r.DeletedAt}"));
        lines.AddRange((await db.HoleResults.ToListAsync(Ct)).Select(h =>
            $"result {h.Id} {h.RoundId} {h.HoleNumber} {h.EntrySequence} {h.Score} {h.Putts} {h.Gir} {h.GreensideSandShots}"));
        lines.AddRange((await db.HandicapEntries.ToListAsync(Ct)).Select(h => $"handicap {h.Id} {h.AccountId} {h.EffectiveOn} {h.Index}"));
        lines.AddRange((await db.Users.ToListAsync(Ct)).Select(a => $"account {a.Id} {a.Email} {a.TrialShotRoundsUsed} {a.Tier}"));
        lines.Sort(StringComparer.Ordinal);
        return string.Join('\n', lines);
    }
}
