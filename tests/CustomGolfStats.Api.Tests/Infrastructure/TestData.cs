using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Domain;

namespace CustomGolfStats.Api.Tests.Infrastructure;

public sealed record SeededCourse(Course Course, Tee Tee, TeeVersion Version);

/// <summary>Seeds shared test data straight through <see cref="AppDbContext"/>.</summary>
public static class TestData
{
    /// <summary>Par 72: 36 out, 36 in.</summary>
    public static readonly int[] DefaultPars = [4, 4, 3, 5, 4, 4, 3, 4, 5, 4, 3, 4, 5, 4, 4, 3, 5, 4];

    public static readonly int[] DefaultStrokeIndexes = [7, 11, 15, 1, 5, 13, 17, 3, 9, 8, 16, 2, 12, 6, 14, 18, 4, 10];

    public static readonly DateOnly DefaultEffectiveFrom = new(1900, 1, 1);

    /// <summary>
    /// A course with one tee and one version of 18 holes (pars from <paramref name="pars"/> or <see cref="DefaultPars"/>;
    /// yardage 150/380/520 for par 3/4/5). Saved before returning.
    /// </summary>
    public static async Task<SeededCourse> SeedCourseAsync(
        AppDbContext db,
        string name = "Test Course",
        string teeName = "White",
        Gender teeGender = Gender.Male,
        DateOnly? effectiveFrom = null,
        int[]? pars = null,
        CourseStatus status = CourseStatus.UserEdited,
        CourseSource source = CourseSource.Manual,
        string? city = null,
        double? latitude = null,
        double? longitude = null)
    {
        var course = new Course
        {
            Name = name,
            City = city,
            Latitude = latitude,
            Longitude = longitude,
            Status = status,
            Source = source,
            CreatedAt = ApiFactory.StartTime,
            UpdatedAt = ApiFactory.StartTime,
        };
        var tee = new Tee { Course = course, Name = teeName, Gender = teeGender };
        db.Courses.Add(course);
        db.Tees.Add(tee);
        var version = AddVersion(db, tee, effectiveFrom ?? DefaultEffectiveFrom, pars ?? DefaultPars, source);
        await db.SaveChangesAsync();
        return new SeededCourse(course, tee, version);
    }

    /// <summary>Adds and saves another version of <paramref name="tee"/>.</summary>
    public static async Task<TeeVersion> AddTeeVersionAsync(AppDbContext db, Tee tee, DateOnly effectiveFrom, int[]? pars = null)
    {
        var version = AddVersion(db, tee, effectiveFrom, pars ?? DefaultPars, CourseSource.Manual);
        await db.SaveChangesAsync();
        return version;
    }

    /// <summary>
    /// A quick-entry round, one hole result per score (holes 1..n in order, 2 putts, no penalties). Saved before returning.
    /// </summary>
    public static async Task<Round> SeedQuickRoundAsync(
        AppDbContext db,
        Guid accountId,
        TeeVersion version,
        DateOnly playedOn,
        IReadOnlyList<int> scores,
        DateTimeOffset? modifiedAt = null)
    {
        var at = modifiedAt ?? ApiFactory.StartTime;
        var round = new Round
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            TeeVersionId = version.Id,
            PlayedOn = playedOn,
            EntryMode = EntryMode.Quick,
            ClientUpdatedAt = at,
            ModifiedAt = at,
        };
        for (var i = 0; i < scores.Count; i++)
        {
            round.HoleResults.Add(new HoleResult
            {
                HoleNumber = i + 1,
                EntrySequence = i + 1,
                Score = scores[i],
                Putts = 2,
            });
        }

        db.Rounds.Add(round);
        await db.SaveChangesAsync();
        return round;
    }

    private static TeeVersion AddVersion(AppDbContext db, Tee tee, DateOnly effectiveFrom, int[] pars, CourseSource source)
    {
        var version = new TeeVersion
        {
            Tee = tee,
            EffectiveFrom = effectiveFrom,
            Par = pars.Sum(),
            Rating = 72.0m,
            Slope = 125,
            FrontPar = pars.Length == 18 ? pars[..9].Sum() : null,
            BackPar = pars.Length == 18 ? pars[9..].Sum() : null,
            Source = source,
            ChangedAt = ApiFactory.StartTime,
        };
        for (var i = 0; i < pars.Length; i++)
        {
            version.Holes.Add(new TeeHole
            {
                Number = i + 1,
                Par = pars[i],
                Yardage = pars[i] switch { 3 => 150, 4 => 380, _ => 520 },
                StrokeIndex = pars.Length == DefaultStrokeIndexes.Length ? DefaultStrokeIndexes[i] : i + 1,
            });
        }

        db.TeeVersions.Add(version);
        return version;
    }
}
