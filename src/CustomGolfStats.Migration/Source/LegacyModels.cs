namespace CustomGolfStats.Migration.Source;

// Rows read from golftrack (SQL Server), one record per table, with only the columns the migration uses.
// Only active rows (IsActive = 1) are read; IsActive itself is therefore not carried.

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

/// <summary><see cref="Handicap"/> is golftrack's stroke index for the hole.</summary>
public sealed record LegacyHole(int Id, int TeeId, int Number, int Par, int Yardage, int Handicap);

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
/// Everything the migration needs for one golfer: their active rounds and round details, the active courses, tees and
/// holes those rounds play, their handicap history, and the lookup names (all lookup rows, active or not, since they
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
