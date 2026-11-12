using CustomGolfStats.Domain.Stats;

namespace CustomGolfStats.Api.Features.Stats;

/// <summary>A domain <see cref="Ratio"/>; <c>Value</c> is null when the denominator is zero.</summary>
public sealed record RatioDto(double? Value, int Numerator, int Denominator)
{
    public static RatioDto From(Ratio r) => new(r.Value, r.Numerator, r.Denominator);
}

/// <summary>A domain <see cref="Average"/>; <c>Value</c> is null when the count is zero.</summary>
public sealed record AverageDto(double? Value, int Total, int Count)
{
    public static AverageDto From(Average a) => new(a.Value, a.Total, a.Count);
}

public sealed record StatsYearsResponse(IReadOnlyList<int> Years);

/// <param name="HandicapIndex">Latest handicap index effective on or before Dec 31 of the year; null if none.</param>
public sealed record OverallResponse(int Year, AverageDto ScoringAverage18, int HolesPlayed, int ToPar,
    decimal? HandicapIndex, DateOnly? HandicapEffectiveOn);

public sealed record ScoreBucketDto(int? Min, int? Max, int Count);

public sealed record SeasonResponse(int Rounds9, int Rounds18, int RoundsOtherLength,
    IReadOnlyList<ScoreBucketDto> Histogram9, IReadOnlyList<ScoreBucketDto> Histogram18);

public sealed record SeasonResultDto(Guid RoundId, DateOnly PlayedOn, string CourseName, string TeeName,
    int HoleCount, int Score, int ToPar, int Putts, RatioDto Gir, RatioDto DrivingAccuracy,
    int? Par3ToPar, int? Par4ToPar, int? Par5ToPar);

public sealed record ResultsResponse(IReadOnlyList<SeasonResultDto> Rounds);

public sealed record ScoringResponse(
    AverageDto ScoringAverage9, AverageDto ScoringAverage18,
    int? Lowest9, int? Highest9, int? Lowest18, int? Highest18,
    int Birdies, int Eagles, RatioDto ParBreakers,
    AverageDto Par3Average, AverageDto Par4Average, AverageDto Par5Average,
    RatioDto BirdieOrBetter);

public sealed record OffTheTeeResponse(
    RatioDto DrivingAccuracy, RatioDto LeftTendency, RatioDto RightTendency,
    AverageDto Par4ToParFromFairway, AverageDto Par4ToParFromLeft, AverageDto Par4ToParFromRight,
    AverageDto Par5ToParFromFairway, AverageDto Par5ToParFromLeft, AverageDto Par5ToParFromRight);

public sealed record ApproachResponse(
    RatioDto Gir, RatioDto GirFromFairway, RatioDto GirFromLeft, RatioDto GirFromRight,
    RatioDto SandSave, RatioDto Scrambling, RatioDto HoleOut);

public sealed record PuttingResponse(
    AverageDto PuttsPerGir, AverageDto PuttsPerHole, RatioDto OnePutt, RatioDto ThreePuttRate,
    AverageDto PuttsPerRound9, AverageDto PuttsPerRound18, RatioDto BirdieConversion);

public sealed record LengthSummaryDto(int Rounds, AverageDto Average, int? Low, int? High);

public sealed record CareerRowDto(int Year, LengthSummaryDto Nine, LengthSummaryDto Eighteen, int Holes, int ToPar,
    AverageDto PuttsPerHole, RatioDto Gir, RatioDto DrivingAccuracy, DateOnly Earliest, DateOnly Latest);

public sealed record CareerResponse(IReadOnlyList<CareerRowDto> Years);

public sealed record CourseRowDto(Guid CourseId, string CourseName, Guid TeeId, string TeeName,
    LengthSummaryDto Nine, LengthSummaryDto Eighteen, int Holes, int ToPar, AverageDto PuttsPerHole,
    RatioDto Gir, RatioDto DrivingAccuracy, AverageDto Par3ToPar, AverageDto Par4ToPar, AverageDto Par5ToPar,
    DateOnly Earliest, DateOnly Latest);

public sealed record CoursesResponse(IReadOnlyList<CourseRowDto> Courses);

public sealed record HandicapPointDto(DateOnly EffectiveOn, decimal Index);

/// <summary>Handicap history, oldest first.</summary>
public sealed record HandicapHistoryResponse(IReadOnlyList<HandicapPointDto> Entries);

public sealed record ScorecardHoleResultDto(int EntrySequence, int Score, int Putts, bool FairwayHit,
    bool TeeShotLeft, bool TeeShotRight, bool TeeShotLong, bool TeeShotShort, bool Gir,
    int PenaltyStrokes, int GreensideSandShots, int FairwaySandShots);

/// <param name="Result">Null when the hole was not played in this round (for example a nine-hole round).</param>
public sealed record ScorecardHoleDto(int Number, int Par, int Yardage, int StrokeIndex, ScorecardHoleResultDto? Result);

public sealed record ScorecardResponse(Guid RoundId, DateOnly PlayedOn,
    Guid CourseId, string CourseName, Guid TeeId, string TeeName,
    DateOnly TeeVersionEffectiveFrom, decimal Rating, int Slope, int Par,
    int HolesPlayed, int Score, int ToPar, int Putts,
    IReadOnlyList<ScorecardHoleDto> Holes);
