using CustomGolfStats.Api.Data.Entities;

namespace CustomGolfStats.Api.Features.Courses;

/// <summary>Turns a provider course into our entities (not saved).</summary>
public static class CourseImport
{
    /// <summary>
    /// Status Imported, Source GolfCourseApi, one tee per provider tee, each with a single version effective
    /// 1900-01-01. Par totals come from the holes. A tee with more than 18 holes is skipped. Missing ratings become 0
    /// and missing slopes 113 (standard), to be corrected by an edit; out-of-range values are treated as missing. Stroke
    /// indexes that aren't 1..n are ranked into 1..n (<see cref="NormalizeStrokeIndexes"/>).
    /// </summary>
    public static Course Build(ProviderCourse source, string providerCourseId, Guid importedByAccountId, DateTimeOffset now)
    {
        var course = new Course
        {
            Name = Truncate(GolfCourseApiMapper.DisplayName(source.ClubName, source.CourseName), 200) is { Length: > 0 } name
                ? name
                : $"Course {providerCourseId}",
            ClubName = Truncate(CourseRules.Clean(source.ClubName), 200),
            Address = Truncate(CourseRules.Clean(source.Address), 300),
            City = Truncate(CourseRules.Clean(source.City), 100),
            Region = Truncate(CourseRules.Clean(source.Region), 100),
            Country = Truncate(CourseRules.Clean(source.Country), 100),
            Latitude = source.Latitude is >= -90 and <= 90 && source.Longitude is >= -180 and <= 180 ? source.Latitude : null,
            Longitude = source.Latitude is >= -90 and <= 90 && source.Longitude is >= -180 and <= 180 ? source.Longitude : null,
            Status = CourseStatus.Imported,
            Source = CourseSource.GolfCourseApi,
            ProviderCourseId = providerCourseId,
            CreatedByAccountId = importedByAccountId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        foreach (var providerTee in source.Tees.Where(t => t.Holes.Count is > 0 and <= CourseRules.MaxHoles))
        {
            var version = new TeeVersion
            {
                EffectiveFrom = CourseRules.DefaultEffectiveFrom,
                Rating = Rating(providerTee.Rating) ?? 0m,
                Slope = Slope(providerTee.Slope) ?? CourseRules.StandardSlope,
                FrontRating = Rating(providerTee.FrontRating),
                FrontSlope = Slope(providerTee.FrontSlope),
                BackRating = Rating(providerTee.BackRating),
                BackSlope = Slope(providerTee.BackSlope),
                Source = CourseSource.GolfCourseApi,
                ChangedByAccountId = importedByAccountId,
                ChangedAt = now,
            };
            var strokeIndexes = NormalizeStrokeIndexes(providerTee.Holes.Select(h => h.StrokeIndex).ToList());
            CourseRules.SetHoles(version, providerTee.Holes.Select((h, i) => (i + 1, h.Par, h.Yardage, strokeIndexes[i])).ToList());

            var tee = new Tee { Name = Truncate(providerTee.Name.Trim(), 100) ?? "Tee", Gender = providerTee.Gender };
            tee.Versions.Add(version);
            course.Tees.Add(tee);
        }

        return course;
    }

    /// <summary>
    /// Stroke indexes as given when they are a permutation of 1..n (n holes); otherwise their rank, ties broken by hole
    /// order (a nine-hole tee whose provider uses the eighteen-hole odd indexes 1, 3, ..., 17 becomes 1..9).
    /// </summary>
    public static IReadOnlyList<int> NormalizeStrokeIndexes(IReadOnlyList<int> strokeIndexes)
    {
        var n = strokeIndexes.Count;
        if (strokeIndexes.Order().SequenceEqual(Enumerable.Range(1, n)))
        {
            return strokeIndexes;
        }

        var ranked = new int[n];
        var order = Enumerable.Range(0, n).OrderBy(i => strokeIndexes[i]).ThenBy(i => i).ToList();
        for (var rank = 0; rank < n; rank++)
        {
            ranked[order[rank]] = rank + 1;
        }

        return ranked;
    }

    private static decimal? Rating(decimal? value) =>
        value is >= CourseRules.MinRating and <= CourseRules.MaxRating ? Math.Round(value.Value, 1) : null;

    private static int? Slope(int? value) => value is >= CourseRules.MinSlope and <= CourseRules.MaxSlope ? value : null;

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}
