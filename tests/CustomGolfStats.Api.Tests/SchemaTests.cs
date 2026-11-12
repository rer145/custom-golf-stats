using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Tests.Infrastructure;
using CustomGolfStats.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CustomGolfStats.Api.Tests;

public sealed class SchemaTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset Now = ApiFactory.StartTime;

    [Fact]
    public async Task Every_table_accepts_a_row_through_AppDbContext()
    {
        var ct = TestContext.Current.CancellationToken;
        var roundId = Guid.NewGuid();

        await factory.WithDbAsync(async db =>
        {
            var account = new Account { UserName = "schema@example.com", Email = "schema@example.com", FirstName = "Ron", LastName = "R" };
            db.Users.Add(account);
            db.UserClaims.Add(new IdentityUserClaim<Guid> { UserId = account.Id, ClaimType = "c", ClaimValue = "v" });
            db.UserLogins.Add(new IdentityUserLogin<Guid> { UserId = account.Id, LoginProvider = "google", ProviderKey = "sub-1" });
            db.UserTokens.Add(new IdentityUserToken<Guid> { UserId = account.Id, LoginProvider = "p", Name = "n", Value = "v" });

            var course = new Course
            {
                Name = "Schema Links", Status = CourseStatus.Imported, Source = CourseSource.GolfCourseApi,
                ProviderCourseId = "123", CreatedAt = Now, UpdatedAt = Now,
            };
            var tee = new Tee { Course = course, Name = "Blue", Gender = Gender.Male };
            var version = new TeeVersion
            {
                Tee = tee, EffectiveFrom = new DateOnly(1900, 1, 1), Par = 72, Rating = 71.3m, Slope = 128,
                Source = CourseSource.GolfCourseApi, ChangedAt = Now, ChangedByAccountId = account.Id,
            };
            version.Holes.Add(new TeeHole { Number = 1, Par = 4, Yardage = 400, StrokeIndex = 5 });
            account.HomeCourse = course;

            var round = new Round
            {
                Id = roundId, Account = account, TeeVersion = version, PlayedOn = new DateOnly(2026, 5, 30),
                EntryMode = EntryMode.ShotByShot, ClientUpdatedAt = Now, ModifiedAt = Now, TemperatureF = 71.5m,
            };
            var hole = new HoleResult { Round = round, HoleNumber = 1, EntrySequence = 1, Score = 4, Putts = 2, IsDerived = true };
            hole.Shots.Add(new ShotEntity
            {
                Round = round, Sequence = 1, StartLie = Lie.Tee, StartDistance = 400, Club = Club.Driver,
                ShotType = ShotType.Tee, EndLie = Lie.Fairway, EndDistance = 150, Direction = Direction.OnTarget, Shape = Shape.Draw,
            });

            db.AddRange(course, tee, version, round, hole);
            db.HandicapEntries.Add(new HandicapEntry { Account = account, EffectiveOn = new DateOnly(2026, 1, 1), Index = 12.4m });
            db.CourseFlags.Add(new CourseFlag { Course = course, Tee = tee, HoleNumber = 1, Reporter = account, Note = "Par is 5", CreatedAt = Now });
            db.AppSettings.Add(new AppSetting { Key = "SchemaTest", Value = "1" });
            await db.SaveChangesAsync(ct);
        });

        var emptyTables = await EmptyTablesAsync(ct);
        Assert.Empty(emptyTables);

        await factory.WithDbAsync(async db =>
        {
            var round = await db.Rounds
                .Include(r => r.HoleResults).ThenInclude(h => h.Shots)
                .SingleAsync(r => r.Id == roundId, ct);
            Assert.Equal(EntryMode.ShotByShot, round.EntryMode);
            Assert.Equal(RoundType.Casual, round.RoundType);
            Assert.Equal(1, round.Version);
            Assert.Equal(Shape.Draw, Assert.Single(Assert.Single(round.HoleResults).Shots).Shape);

            var account = await db.Users.SingleAsync(ct);
            Assert.Equal(Role.User, account.Role);
            Assert.Equal(Tier.Free, account.Tier);
            Assert.Equal(DistanceUnits.Yards, account.Units);
            Assert.Equal(Now, account.CreatedAt);
        });
    }

    [Fact]
    public async Task Enums_are_stored_as_strings_in_snake_case_columns()
    {
        var ct = TestContext.Current.CancellationToken;
        await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, name: "Enum Course", status: CourseStatus.Verified));

        var status = await ScalarAsync<string>("SELECT status FROM courses WHERE name = 'Enum Course'", ct);

        Assert.Equal("Verified", status);
    }

    [Fact]
    public async Task Trial_shot_rounds_setting_is_seeded()
    {
        var value = await factory.WithDbAsync(db =>
            db.AppSettings.Where(s => s.Key == AppSettingKeys.TrialShotRounds).Select(s => s.Value).SingleAsync());

        Assert.Equal("3", value);
    }

    [Fact]
    public async Task Provider_course_id_is_unique_per_source()
    {
        var ct = TestContext.Current.CancellationToken;
        await factory.WithDbAsync(async db =>
        {
            db.Courses.Add(NewCourse("A", CourseSource.GolfCourseApi, "dup"));
            db.Courses.Add(NewCourse("B", CourseSource.Golftrack, "dup"));
            db.Courses.Add(NewCourse("C", CourseSource.Manual, null));
            db.Courses.Add(NewCourse("D", CourseSource.Manual, null));
            await db.SaveChangesAsync(ct);

            db.Courses.Add(NewCourse("E", CourseSource.GolfCourseApi, "dup"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
        });
    }

    [Fact]
    public async Task Tee_version_effective_date_is_unique_per_tee()
    {
        await factory.WithDbAsync(async db =>
        {
            var seeded = await TestData.SeedCourseAsync(db, name: "Unique Versions");

            await Assert.ThrowsAsync<DbUpdateException>(() =>
                TestData.AddTeeVersionAsync(db, seeded.Tee, TestData.DefaultEffectiveFrom));
        });
    }

    private static Course NewCourse(string name, CourseSource source, string? providerId) => new()
    {
        Name = name, Source = source, ProviderCourseId = providerId, Status = CourseStatus.Imported, CreatedAt = Now, UpdatedAt = Now,
    };

    private async Task<List<string>> EmptyTablesAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(ct);
        var tables = new List<string>();
        await using (var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' AND table_name <> '__EFMigrationsHistory'",
            connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                tables.Add(reader.GetString(0));
            }
        }

        Assert.NotEmpty(tables);
        var empty = new List<string>();
        foreach (var table in tables)
        {
            await using var count = new NpgsqlCommand($"SELECT count(*) FROM \"{table}\"", connection);
            if ((long)(await count.ExecuteScalarAsync(ct))! == 0)
            {
                empty.Add(table);
            }
        }

        return empty;
    }

    private async Task<T> ScalarAsync<T>(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(ct))!;
    }
}
