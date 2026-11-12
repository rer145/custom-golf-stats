using System.Text.Json.Serialization;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Domain;

namespace CustomGolfStats.Api.Features.Rounds;

// Enums travel as their names. The converter is set per property so this feature doesn't change JSON for the others.

/// <summary>
/// <c>PUT /rounds/{id}</c>: the whole round as the device has it. Missing weather is looked up by the server when the
/// round has a start time and the course has coordinates.
/// </summary>
public sealed record UpsertRoundRequest(
    Guid TeeId,
    DateOnly PlayedOn,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] EntryMode? EntryMode,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] RoundType? RoundType,
    string? Notes,
    string? WeatherConditions,
    decimal? TemperatureF,
    decimal? WindSpeedMph,
    int? WindDirectionDegrees,
    int? DurationMinutes,
    decimal? DistanceMiles,
    int? StepCount,
    DateTimeOffset ClientUpdatedAt,
    IReadOnlyList<UpsertHoleRequest>? Holes);

/// <summary>
/// One hole played. Quick rounds send the summary fields (score is required; the rest default to zero or false).
/// Shot-by-shot rounds send <see cref="Shots"/>; their summary fields are ignored and derived by the server.
/// </summary>
public sealed record UpsertHoleRequest(
    int HoleNumber,
    int EntrySequence,
    int? Score,
    int? Putts,
    bool? FairwayHit,
    bool? TeeShotLeft,
    bool? TeeShotRight,
    bool? TeeShotLong,
    bool? TeeShotShort,
    bool? Gir,
    int? PenaltyStrokes,
    int? GreensideSandShots,
    int? FairwaySandShots,
    IReadOnlyList<ShotDto>? Shots);

/// <summary>One stroke. Distances are yards, or feet when the lie is Green. <see cref="EndDistance"/> is null when holed.</summary>
public sealed record ShotDto(
    int Sequence,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] Lie StartLie,
    int StartDistance,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] Club Club,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ShotType ShotType,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] Lie EndLie,
    int? EndDistance,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] Direction Direction,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] Shape? Shape,
    int PenaltyStrokes);

/// <summary>A full round, as stored. <see cref="DeletedAt"/> is set on tombstones returned by <c>sync/changes</c>.</summary>
public sealed record RoundDto(
    Guid Id,
    Guid CourseId,
    string CourseName,
    Guid TeeId,
    string TeeName,
    Guid TeeVersionId,
    DateOnly PlayedOn,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] EntryMode EntryMode,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] RoundType RoundType,
    string? Notes,
    string? WeatherConditions,
    decimal? TemperatureF,
    decimal? WindSpeedMph,
    int? WindDirectionDegrees,
    string? LegacyWeather,
    int? DurationMinutes,
    decimal? DistanceMiles,
    int? StepCount,
    DateTimeOffset ClientUpdatedAt,
    DateTimeOffset ModifiedAt,
    DateTimeOffset? DeletedAt,
    int Version,
    bool TrialCounted,
    IReadOnlyList<HoleResultDto> Holes);

/// <summary>One stored hole result. <see cref="Par"/> comes from the round's tee version.</summary>
public sealed record HoleResultDto(
    int HoleNumber,
    int EntrySequence,
    int Par,
    int Score,
    int Putts,
    bool FairwayHit,
    bool TeeShotLeft,
    bool TeeShotRight,
    bool TeeShotLong,
    bool TeeShotShort,
    bool Gir,
    int PenaltyStrokes,
    int GreensideSandShots,
    int FairwaySandShots,
    bool IsDerived,
    IReadOnlyList<ShotDto> Shots);

/// <summary>A row of <c>GET /rounds</c>.</summary>
public sealed record RoundSummaryDto(
    Guid Id,
    DateOnly PlayedOn,
    Guid CourseId,
    string CourseName,
    Guid TeeId,
    string TeeName,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] EntryMode EntryMode,
    int Holes,
    int Score,
    int ToPar);

/// <summary>
/// <c>GET /sync/changes</c>. Send <see cref="NextSince"/> back as <c>since</c> on the next pull (not <see cref="ServerTime"/>).
/// <see cref="NextSince"/> is <see cref="ServerTime"/> minus a safety margin, so rounds can come again: key them by id and version.
/// </summary>
public sealed record SyncChangesResponse(
    DateTimeOffset ServerTime,
    DateTimeOffset NextSince,
    IReadOnlyList<RoundDto> Rounds,
    IReadOnlyList<SyncCourseDto> Courses,
    IReadOnlyList<SyncHandicapEntryDto> HandicapEntries,
    SyncEntitlementsDto Entitlements);

public sealed record SyncCourseDto(
    Guid Id,
    string Name,
    string? ClubName,
    string? Address,
    string? City,
    string? Region,
    string? Country,
    string? Phone,
    string? Website,
    double? Latitude,
    double? Longitude,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] CourseStatus Status,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] CourseSource Source,
    Guid? MergedIntoCourseId,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<SyncTeeDto> Tees);

public sealed record SyncTeeDto(
    Guid Id,
    string Name,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] Gender Gender,
    IReadOnlyList<SyncTeeVersionDto> Versions);

public sealed record SyncTeeVersionDto(
    Guid Id,
    DateOnly EffectiveFrom,
    int Par,
    decimal Rating,
    int Slope,
    int? FrontPar,
    decimal? FrontRating,
    int? FrontSlope,
    int? BackPar,
    decimal? BackRating,
    int? BackSlope,
    IReadOnlyList<SyncTeeHoleDto> Holes);

public sealed record SyncTeeHoleDto(int Number, int Par, int Yardage, int StrokeIndex);

public sealed record SyncHandicapEntryDto(Guid Id, DateOnly EffectiveOn, decimal Index);

/// <summary><see cref="TrialShotRoundsRemaining"/> is null for Paid golfers (unlimited).</summary>
public sealed record SyncEntitlementsDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] Tier Tier,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] Role Role,
    SyncEntitlementFeaturesDto Features,
    int? TrialShotRoundsRemaining);

public sealed record SyncEntitlementFeaturesDto(bool ShotByShotEntry);

/// <summary><c>GET /weather</c>.</summary>
public sealed record WeatherDto(string Conditions, decimal TemperatureF, decimal WindSpeedMph, int WindDirectionDegrees);
