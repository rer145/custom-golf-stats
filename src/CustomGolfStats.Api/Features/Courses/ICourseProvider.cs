using CustomGolfStats.Api.Data.Entities;

namespace CustomGolfStats.Api.Features.Courses;

/// <summary>One provider search hit (enough to show in a pick list).</summary>
public sealed record ProviderCourseSummary(
    string ProviderCourseId, string ClubName, string CourseName, string? City, string? Region, string? Country);

public sealed record ProviderCourse(
    string ProviderCourseId,
    string ClubName,
    string CourseName,
    string? Address,
    string? City,
    string? Region,
    string? Country,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<ProviderTee> Tees);

/// <summary>
/// A provider tee. Ratings, slopes and par total can be missing in provider data, so they are nullable; the importer
/// fills gaps (par from the holes, rating 0 and slope 113 when unknown) and the course can be corrected later.
/// </summary>
public sealed record ProviderTee(
    string Name,
    Gender Gender,
    int? Par,
    decimal? Rating,
    int? Slope,
    decimal? FrontRating,
    int? FrontSlope,
    decimal? BackRating,
    int? BackSlope,
    IReadOnlyList<ProviderHole> Holes);

/// <summary>
/// A hole in provider order; its number is its position (1-based). <see cref="StrokeIndex"/> is already normalised by
/// the provider adapter (falls back to hole order when the provider's handicaps are missing or inconsistent).
/// </summary>
public sealed record ProviderHole(int Par, int Yardage, int StrokeIndex);

/// <summary>GolfCourseAPI. Used only by <c>courses/provider-search</c> and <c>courses/import</c>. Faked in tests.</summary>
public interface ICourseProvider
{
    /// <exception cref="CourseProviderException">The provider is unreachable, misconfigured or failed.</exception>
    Task<IReadOnlyList<ProviderCourseSummary>> SearchAsync(string query, CancellationToken ct);

    /// <returns>The course, or null when the provider doesn't know the id.</returns>
    /// <exception cref="CourseProviderException">The provider is unreachable, misconfigured or failed.</exception>
    Task<ProviderCourse?> GetAsync(string providerCourseId, CancellationToken ct);
}

/// <summary>The course provider could not answer (network, auth, server error, unreadable response).</summary>
public sealed class CourseProviderException(string message, Exception? innerException = null)
    : Exception(message, innerException);
