using CustomGolfStats.Api.Data.Entities;

namespace CustomGolfStats.Api.Features.Courses;

/// <summary>Entity to DTO mapping (hand-written).</summary>
public static class CourseMapping
{
    /// <param name="course">With tees, versions and holes loaded.</param>
    /// <param name="today">Decides which version of each tee is flagged current.</param>
    public static CourseDetailDto ToDetailDto(this Course course, DateOnly today) => new(
        course.Id,
        course.Name,
        course.ClubName,
        course.Address,
        course.City,
        course.Region,
        course.Country,
        course.Phone,
        course.Website,
        course.Latitude,
        course.Longitude,
        course.Status,
        course.Source,
        course.ProviderCourseId,
        course.LastVerifiedAt,
        course.MergedIntoCourseId,
        // Longest tees first (the usual scorecard order), then by name.
        course.Tees.Select(t => t.ToDto(today))
            .OrderByDescending(t => t.Versions.Where(v => v.IsCurrent).Sum(v => v.Holes.Sum(h => h.Yardage)))
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList());

    public static TeeDto ToDto(this Tee tee, DateOnly today)
    {
        var current = CourseRules.VersionInEffect(tee.Versions.Select(v => (v.Id, v.EffectiveFrom)), today);
        return new TeeDto(
            tee.Id,
            tee.Name,
            tee.Gender,
            tee.Versions.OrderBy(v => v.EffectiveFrom).Select(v => v.ToDto(v.Id == current)).ToList());
    }

    public static TeeVersionDto ToDto(this TeeVersion version, bool isCurrent) => new(
        version.Id,
        version.EffectiveFrom,
        isCurrent,
        version.Par,
        version.Rating,
        version.Slope,
        version.FrontPar,
        version.FrontRating,
        version.FrontSlope,
        version.BackPar,
        version.BackRating,
        version.BackSlope,
        version.Source,
        version.ChangedAt,
        version.Holes.OrderBy(h => h.Number).Select(h => new TeeHoleDto(h.Number, h.Par, h.Yardage, h.StrokeIndex)).ToList());

    public static CourseFlagDto ToDto(this CourseFlag flag) =>
        new(flag.Id, flag.CourseId, flag.TeeId, flag.HoleNumber, flag.Note, flag.Status, flag.CreatedAt);
}
