using System.Text.Json.Serialization;
using CustomGolfStats.Api.Data.Entities;

namespace CustomGolfStats.Api.Features.Courses;

/// <summary>A database search hit. <see cref="DistanceMiles"/> is set only when the search had <c>near</c>.</summary>
public sealed record CourseSummaryDto(
    Guid Id,
    string Name,
    string? ClubName,
    string? City,
    string? Region,
    string? Country,
    double? Latitude,
    double? Longitude,
    CourseStatus Status,
    double? DistanceMiles);

public sealed record CourseDetailDto(
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
    CourseStatus Status,
    CourseSource Source,
    string? ProviderCourseId,
    DateTimeOffset? LastVerifiedAt,
    Guid? MergedIntoCourseId,
    IReadOnlyList<TeeDto> Tees);

public sealed record TeeDto(
    Guid Id,
    string Name,
    Gender Gender,
    IReadOnlyList<TeeVersionDto> Versions);

/// <summary>A tee version. <see cref="IsCurrent"/> marks the version in effect today (server date, UTC).</summary>
public sealed record TeeVersionDto(
    Guid Id,
    DateOnly EffectiveFrom,
    bool IsCurrent,
    int Par,
    decimal Rating,
    int Slope,
    int? FrontPar,
    decimal? FrontRating,
    int? FrontSlope,
    int? BackPar,
    decimal? BackRating,
    int? BackSlope,
    CourseSource Source,
    DateTimeOffset ChangedAt,
    IReadOnlyList<TeeHoleDto> Holes);

public sealed record TeeHoleDto(int Number, int Par, int Yardage, int StrokeIndex);

/// <summary>A provider search hit; <see cref="CourseId"/> is our course when it was already imported.</summary>
public sealed record ProviderSearchResultDto(
    string ProviderCourseId,
    string ClubName,
    string CourseName,
    string? City,
    string? Region,
    string? Country,
    bool AlreadyImported,
    Guid? CourseId);

public sealed record CourseFlagDto(
    Guid Id,
    Guid CourseId,
    Guid? TeeId,
    int? HoleNumber,
    string Note,
    FlagStatus Status,
    DateTimeOffset CreatedAt);

public sealed record ImportCourseRequest(string? ProviderCourseId);

/// <summary>Course details (not versioned). <c>PUT /courses/{id}</c> replaces all of them.</summary>
public sealed record CourseDetailsRequest(
    string? Name,
    string? ClubName,
    string? Address,
    string? City,
    string? Region,
    string? Country,
    string? Phone,
    string? Website,
    double? Latitude,
    double? Longitude);

public sealed record CreateCourseRequest(
    string? Name,
    string? ClubName,
    string? Address,
    string? City,
    string? Region,
    string? Country,
    string? Phone,
    string? Website,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<CreateTeeRequest>? Tees)
{
    [JsonIgnore]
    public CourseDetailsRequest Details => new(Name, ClubName, Address, City, Region, Country, Phone, Website, Latitude, Longitude);
}

/// <summary>A tee and its first version. <see cref="EffectiveFrom"/> defaults to 1900-01-01, gender to Unspecified.</summary>
public sealed record CreateTeeRequest(
    string? Name,
    Gender? Gender,
    DateOnly? EffectiveFrom,
    decimal? Rating,
    int? Slope,
    decimal? FrontRating,
    int? FrontSlope,
    decimal? BackRating,
    int? BackSlope,
    IReadOnlyList<TeeHoleInput>? Holes)
{
    [JsonIgnore]
    public TeeVersionInput Version =>
        new(EffectiveFrom ?? CourseRules.DefaultEffectiveFrom, Rating, Slope, FrontRating, FrontSlope, BackRating, BackSlope, Holes);
}

/// <summary>
/// A tee version's numbers. Body of <c>POST .../versions</c> (where <see cref="EffectiveFrom"/> is required) and
/// <c>PUT .../versions/{versionId}</c> (null keeps the current date); also the input of <see cref="TeeVersionEditor"/>.
/// Par, front par and back par are computed from the holes.
/// </summary>
public sealed record TeeVersionInput(
    DateOnly? EffectiveFrom,
    decimal? Rating,
    int? Slope,
    decimal? FrontRating,
    int? FrontSlope,
    decimal? BackRating,
    int? BackSlope,
    IReadOnlyList<TeeHoleInput>? Holes);

public sealed record TeeHoleInput(int Number, int Par, int Yardage, int StrokeIndex);

public sealed record CreateFlagRequest(Guid? TeeId, int? HoleNumber, string? Note);
