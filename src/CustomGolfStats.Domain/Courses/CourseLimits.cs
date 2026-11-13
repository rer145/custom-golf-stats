using CustomGolfStats.Domain.Holes;

namespace CustomGolfStats.Domain.Courses;

/// <summary>The numeric limits a tee version must respect. The API enforces these on every create and edit, and the app checks them
/// before saving a course by hand, so both always agree.</summary>
public static class CourseLimits
{
    public const int MinHoles = 1;
    public const int MaxHoles = 18;
    public const int MinPar = 3;
    public const int MaxPar = 6;
    public const int MinYardage = 0;
    public const int MaxYardage = 1000;
    public const int MinSlope = 55;
    public const int MaxSlope = 155;
    public const decimal MinRating = 1.0m;
    public const decimal MaxRating = 99.9m;

    // Text lengths (after trimming).
    public const int MaxNameLength = 200;
    public const int MaxClubNameLength = 200;
    public const int MaxAddressLength = 300;
    public const int MaxCityLength = 100;
    public const int MaxRegionLength = 100;
    public const int MaxCountryLength = 100;
    public const int MaxPhoneLength = 50;
    public const int MaxWebsiteLength = 300;
    public const int MaxTeeNameLength = 100;

    /// <summary>Effective date of a tee's first version: before any round anyone could have played.</summary>
    public static readonly DateOnly DefaultEffectiveFrom = new(1900, 1, 1);

    /// <summary>Slope of a course of standard difficulty; used when a source gives none.</summary>
    public const int StandardSlope = 113;
}

public enum CourseError
{
    RatingMissing, RatingOutOfRange, SlopeMissing, SlopeOutOfRange,
    NoHoles, TooManyHoles, HoleNumberOutOfRange, HoleNumberRepeated,
    ParOutOfRange, YardageOutOfRange, StrokeIndexOutOfRange, StrokeIndexRepeated,
}

/// <summary>Pure check of a tee version's rating, slope and holes against <see cref="CourseLimits"/>.</summary>
public static class CourseValidator
{
    /// <summary>Each problem reported at most once. Empty means the layout is acceptable.</summary>
    public static IReadOnlyList<CourseError> Validate(decimal? rating, int? slope, IReadOnlyList<HoleInfo> holes)
    {
        var errors = new List<CourseError>();

        if (rating is null)
            errors.Add(CourseError.RatingMissing);
        else if (rating < CourseLimits.MinRating || rating > CourseLimits.MaxRating)
            errors.Add(CourseError.RatingOutOfRange);

        if (slope is null)
            errors.Add(CourseError.SlopeMissing);
        else if (slope < CourseLimits.MinSlope || slope > CourseLimits.MaxSlope)
            errors.Add(CourseError.SlopeOutOfRange);

        if (holes.Count < CourseLimits.MinHoles)
        {
            errors.Add(CourseError.NoHoles);
            return errors;
        }
        if (holes.Count > CourseLimits.MaxHoles)
        {
            errors.Add(CourseError.TooManyHoles);
            return errors;
        }

        if (holes.Any(h => h.Number < 1 || h.Number > CourseLimits.MaxHoles))
            errors.Add(CourseError.HoleNumberOutOfRange);
        if (holes.GroupBy(h => h.Number).Any(g => g.Count() > 1))
            errors.Add(CourseError.HoleNumberRepeated);
        if (holes.Any(h => h.Par < CourseLimits.MinPar || h.Par > CourseLimits.MaxPar))
            errors.Add(CourseError.ParOutOfRange);
        if (holes.Any(h => h.Yardage < CourseLimits.MinYardage || h.Yardage > CourseLimits.MaxYardage))
            errors.Add(CourseError.YardageOutOfRange);
        if (holes.Any(h => h.StrokeIndex < 1 || h.StrokeIndex > holes.Count))
            errors.Add(CourseError.StrokeIndexOutOfRange);
        if (holes.GroupBy(h => h.StrokeIndex).Any(g => g.Count() > 1))
            errors.Add(CourseError.StrokeIndexRepeated);

        return errors;
    }
}
