using CustomGolfStats.Api.Data.Entities;

namespace CustomGolfStats.Api.Features.Courses;

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

public sealed record ProviderTee(
    string Name,
    Gender Gender,
    int Par,
    decimal Rating,
    int Slope,
    decimal? FrontRating,
    int? FrontSlope,
    decimal? BackRating,
    int? BackSlope,
    IReadOnlyList<ProviderHole> Holes);

/// <summary>A hole in provider order; its number is its position (1-based).</summary>
public sealed record ProviderHole(int Par, int Yardage, int StrokeIndex);

/// <summary>GolfCourseAPI. Used only by <c>courses/provider-search</c> and <c>courses/import</c>. Faked in tests.</summary>
public interface ICourseProvider
{
    Task<IReadOnlyList<ProviderCourseSummary>> SearchAsync(string query, CancellationToken ct);

    /// <returns>The course, or null when the provider doesn't know the id.</returns>
    Task<ProviderCourse?> GetAsync(string providerCourseId, CancellationToken ct);
}
