using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.Domain;
using CustomGolfStats.Domain.Holes;
using CustomGolfStats.Domain.Shots;

namespace CustomGolfStats.App.Core.Tests.Support;

public static class Samples
{
    public static readonly Guid CourseId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid TeeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid TeeVersionId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static CourseRecord Course(string name = "Pine Valley")
    {
        var holes = Enumerable.Range(1, 18)
            .Select(n => new TeeHoleRow { TeeVersionId = TeeVersionId, Number = n, Par = n % 3 == 0 ? 3 : n % 3 == 1 ? 4 : 5, Yardage = 100 + n * 20, StrokeIndex = n })
            .ToList();
        var version = new TeeVersionRow
        {
            Id = TeeVersionId, TeeId = TeeId, EffectiveFrom = new DateTime(2020, 1, 1), Par = holes.Sum(h => h.Par),
            Rating = 71.2, Slope = 130, FrontPar = 36, FrontRating = 35.6, FrontSlope = 128,
        };
        var tee = new TeeRow { Id = TeeId, CourseId = CourseId, Name = "Blue", Gender = Api.Client.Gender.Male };
        var course = new CourseRow
        {
            Id = CourseId, Name = name, ClubName = "Pine Valley GC", City = "Clementon", Region = "NJ", Country = "US",
            Latitude = 39.78, Longitude = -74.98, Status = Api.Client.CourseStatus.Verified, Source = Api.Client.CourseSource.GolfCourseApi,
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        return new CourseRecord(course, [new TeeRecord(tee, [new TeeVersionRecord(version, holes)])]);
    }

    public static RoundRow Round(Guid? id = null, EntryMode mode = EntryMode.ShotByShot) => new()
    {
        Id = id ?? Guid.NewGuid(), CourseId = CourseId, CourseName = "Pine Valley", TeeId = TeeId, TeeName = "Blue",
        TeeVersionId = TeeVersionId, PlayedOn = new DateTime(2026, 6, 1), StartedAt = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
        EntryMode = mode, RoundType = Api.Client.RoundType.Casual, Notes = "windy",
        WeatherConditions = "Clear", TemperatureF = 72.5, WindSpeedMph = 8.0, WindDirectionDegrees = 270,
    };

    /// <summary>Hole 1 par 4: drive to the fairway, approach to the green, two putts (score 4).</summary>
    public static IReadOnlyList<Shot> Par4Shots() =>
    [
        new Shot(1, Lie.Tee, 380, Club.Driver, ShotType.Tee, Lie.Fairway, 150, Direction.OnTarget, Shape.Draw, 0),
        new Shot(2, Lie.Fairway, 150, Club.Iron7, ShotType.Approach, Lie.Green, 18, Direction.Short, null, 0),
        new Shot(3, Lie.Green, 18, Club.Putter, ShotType.Putt, Lie.Green, 2, Direction.OnTarget, null, 0),
        new Shot(4, Lie.Green, 2, Club.Putter, ShotType.Putt, Lie.Holed, null, Direction.OnTarget, null, 0),
    ];

    /// <summary>Penalty off the tee, layup, sand, bunker holed (a chip-in style finish with no putts).</summary>
    public static IReadOnlyList<Shot> Par5Shots() =>
    [
        new Shot(1, Lie.Tee, 500, Club.Driver, ShotType.Tee, Lie.Rough, 270, Direction.Left, Shape.Hook, 1),
        new Shot(2, Lie.Rough, 270, Club.Wood3, ShotType.Layup, Lie.FairwaySand, 120, Direction.OnTarget, null, 0),
        new Shot(3, Lie.FairwaySand, 120, Club.PitchingWedge, ShotType.Approach, Lie.GreensideSand, 20, Direction.Short, Shape.Fat, 0),
        new Shot(4, Lie.GreensideSand, 20, Club.SandWedge, ShotType.Bunker, Lie.Holed, null, Direction.OnTarget, null, 0),
    ];

    public static RoundRecord FullShotRound(Guid? id = null)
    {
        var round = Round(id);
        var hole1 = HoleRecord.FromShots(round.Id, new HoleInfo(1, 4, 380, 7), 1, Par4Shots());
        var hole2 = HoleRecord.FromShots(round.Id, new HoleInfo(2, 5, 500, 1), 2, Par5Shots());
        return new RoundRecord(round, [hole1, hole2]);
    }

    /// <summary>Marks the round as already known to the server (Version 1, ModifiedAt set).</summary>
    public static RoundRecord Synced(RoundRecord r)
    {
        r.Round.Version = 1;
        r.Round.ModifiedAt = new DateTimeOffset(2026, 6, 1, 13, 0, 0, TimeSpan.Zero);
        return r;
    }

    public static HoleRecord QuickHole(Guid roundId, int number, int par, int score, int putts) =>
        new(new HoleRow
        {
            RoundId = roundId, HoleNumber = number, EntrySequence = number, Par = par, Score = score, Putts = putts,
            FairwayHit = par >= 4, Gir = score - putts <= par - 2,
        }, []);
}
