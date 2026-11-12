using System.Text.Json.Serialization;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Domain;

namespace CustomGolfStats.Api.Features.Admin;

// Enums are written and read as their names (per property, so other features' JSON settings are untouched).
// Request enums are strings parsed by the endpoints, so a bad value is a 400 keyed by field.

public sealed record AdminUserDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Role>))] Role Role,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Tier>))] Tier Tier,
    int TrialShotRoundsUsed,
    DateTimeOffset? DisabledAt,
    DateTimeOffset CreatedAt);

/// <summary>
/// Body of <c>PUT /admin/users/{id}</c>. Every field is optional; null keeps the current value. <see cref="Tier"/> and
/// <see cref="Role"/> are enum names (case-insensitive).
/// </summary>
public sealed record UpdateUserRequest(string? Tier, string? Role, bool? Disabled);

public sealed record AdminCourseSummaryDto(
    Guid Id,
    string Name,
    string? ClubName,
    string? City,
    string? Region,
    string? Country,
    [property: JsonConverter(typeof(JsonStringEnumConverter<CourseStatus>))] CourseStatus Status,
    [property: JsonConverter(typeof(JsonStringEnumConverter<CourseSource>))] CourseSource Source,
    DateTimeOffset? LastVerifiedAt,
    Guid? MergedIntoCourseId,
    int OpenFlags,
    DateTimeOffset UpdatedAt);

public sealed record MergeCourseRequest(Guid? IntoCourseId);

/// <param name="CourseId">The merged (source) course, now hidden from search.</param>
/// <param name="RoundsMoved">Rounds re-pointed at the target's tee versions (soft-deleted ones included).</param>
/// <param name="FlagsMoved">Open flags moved to the target.</param>
public sealed record CourseMergeResultDto(Guid CourseId, Guid IntoCourseId, int RoundsMoved, int FlagsMoved);

/// <summary>A used source tee version with no target version of the same tee (name and gender) and EffectiveFrom.</summary>
public sealed record UnmatchedTeeVersionDto(
    Guid TeeId,
    string TeeName,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Gender>))] Gender Gender,
    Guid VersionId,
    DateOnly EffectiveFrom,
    int Rounds);

public sealed record AdminFlagDto(
    Guid Id,
    Guid CourseId,
    string CourseName,
    Guid? TeeId,
    string? TeeName,
    int? HoleNumber,
    string Note,
    [property: JsonConverter(typeof(JsonStringEnumConverter<FlagStatus>))] FlagStatus Status,
    Guid ReporterAccountId,
    string? ReporterEmail,
    DateTimeOffset CreatedAt,
    Guid? ResolvedByAccountId,
    DateTimeOffset? ResolvedAt);

/// <summary>App settings an admin can change.</summary>
/// <param name="TrialShotRounds">How many shot-by-shot rounds a Free golfer may log.</param>
public sealed record AdminSettingsDto(int TrialShotRounds);

/// <summary>Body of <c>PUT /admin/settings</c>: every field is required. <c>TrialShotRounds</c> is an integer &gt;= 0.</summary>
public sealed record UpdateSettingsRequest(int? TrialShotRounds);
