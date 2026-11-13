namespace CustomGolfStats.Migration.Source;

// Rows read from golftrack (SQL Server), one record per table, with only the columns the migration uses.
// Rounds and round details are read only when IsActive = 1. The courses, tees and holes those rounds play are read
// whatever their own IsActive (golftrack's Stats_* procedures ignore it there too).

public sealed record LegacyCourse(
    int Id,
    string Name,
    string? Address,
    string? Address2,
    string? City,
    string? PostalCode,
    string? PhoneNumber,
    string? Website,
    decimal? Latitude,
    decimal? Longitude);

public sealed record LegacyTee(
    int Id,
    int CourseId,
    string Name,
    string? Gender,
    int Par,
    double? CourseRating,
    int? SlopeRating,
    int FrontNinePar,
    double? FrontNineCourseRating,
    int? FrontNineSlopeRating,
    int BackNinePar,
    double? BackNineCourseRating,
    int? BackNineSlopeRating);

/// <summary>
/// <see cref="Handicap"/> is golftrack's stroke index for the hole. Holes of a played tee are read whether active or
/// not; <see cref="IsActive"/> only decides which of two rows for the same hole number is used.
/// </summary>
public sealed record LegacyHole(int Id, int TeeId, int Number, int Par, int Yardage, int Handicap, bool IsActive);

public sealed record LegacyRound(
    int Id,
    int GolferId,
    int TeeId,
    int? RoundTypeId,
    int? WeatherConditionId,
    int? WindConditionId,
    int? TemperatureId,
    int? HolesPlayed,
    int Score,
    string? Duration,
    decimal? Distance,
    DateTime PlayedOn,
    string? Notes);

public sealed record LegacyRoundDetail(
    int Id,
    int RoundId,
    int HoleId,
    int? SequenceNumber,
    int Score,
    int Putts,
    bool FairwayHit,
    bool TeeShotLeft,
    bool TeeShotRight,
    bool TeeShotLong,
    bool TeeShotShort,
    bool GreenInRegulation,
    int PenaltyShots,
    int SandShots);

public sealed record LegacyHandicap(int Id, int GolferId, DateTime HandicapDate, decimal HandicapIndex);

/// <summary>
/// Everything the migration needs for one golfer: their active rounds and round details, the courses, tees and holes
/// (active or not) those rounds play, their handicap history, and the lookup names (all lookup rows, active or not, since they
/// are only used as text). Lists are ordered by id.
/// </summary>
public sealed record LegacyData(
    int GolferId,
    IReadOnlyList<LegacyCourse> Courses,
    IReadOnlyList<LegacyTee> Tees,
    IReadOnlyList<LegacyHole> Holes,
    IReadOnlyList<LegacyRound> Rounds,
    IReadOnlyList<LegacyRoundDetail> RoundDetails,
    IReadOnlyList<LegacyHandicap> Handicaps,
    IReadOnlyDictionary<int, string> RoundTypes,
    IReadOnlyDictionary<int, string> WeatherConditions,
    IReadOnlyDictionary<int, string> WindConditions,
    IReadOnlyDictionary<int, string> Temperatures);

/// <summary>
/// All golftrack ids of the tables whose rows the import writes (any golfer, active or not); rounds and handicap
/// history rows map to their golfer.
/// </summary>
public sealed record LegacyIdSet(
    IReadOnlyList<int> CourseIds, IReadOnlyDictionary<int, int> RoundGolfers, IReadOnlyDictionary<int, int> HandicapGolfers);
