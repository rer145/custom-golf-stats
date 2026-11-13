using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Infrastructure;

namespace CustomGolfStats.Api.Features.Courses;

/// <summary>Course and tee version rules shared by manual create, edits, import and admin corrections.</summary>
public static class CourseRules
{
    /// <summary>Effective date of a tee's first version: before any round anyone could have played.</summary>
    public static readonly DateOnly DefaultEffectiveFrom = new(1900, 1, 1);

    public const int MaxHoles = 18;
    public const int MinPar = 3;
    public const int MaxPar = 6;
    public const decimal MinRating = 1.0m;
    public const decimal MaxRating = 99.9m;
    public const int MinSlope = 55;
    public const int MaxSlope = 155;
    public const int MaxYardage = 1000;
    public const int MaxFlagNote = 4000;

    /// <summary>Slope of a course of standard difficulty; used when a provider tee has no slope.</summary>
    public const int StandardSlope = 113;

    public static void ValidateDetails(CourseDetailsRequest details, ValidationErrors errors, string prefix = "")
    {
        if (string.IsNullOrWhiteSpace(details.Name))
        {
            errors.Add(prefix + "name", "Name is required.");
        }

        MaxLength(details.Name, 200, prefix + "name", errors);
        MaxLength(details.ClubName, 200, prefix + "clubName", errors);
        MaxLength(details.Address, 300, prefix + "address", errors);
        MaxLength(details.City, 100, prefix + "city", errors);
        MaxLength(details.Region, 100, prefix + "region", errors);
        MaxLength(details.Country, 100, prefix + "country", errors);
        MaxLength(details.Phone, 50, prefix + "phone", errors);
        MaxLength(details.Website, 300, prefix + "website", errors);

        if (details.Latitude is { } lat && (lat < -90 || lat > 90 || double.IsNaN(lat)))
        {
            errors.Add(prefix + "latitude", "Latitude must be between -90 and 90.");
        }

        if (details.Longitude is { } lon && (lon < -180 || lon > 180 || double.IsNaN(lon)))
        {
            errors.Add(prefix + "longitude", "Longitude must be between -180 and 180.");
        }

        if (details.Latitude is null != details.Longitude is null)
        {
            errors.Add(prefix + (details.Latitude is null ? "latitude" : "longitude"), "Give both latitude and longitude, or neither.");
        }
    }

    /// <summary>
    /// Validates a tee version: rating and slope ranges, 1 to 18 holes, hole numbers unique (1..18), par 3 to 6,
    /// stroke indexes unique 1..n where n is the number of holes.
    /// </summary>
    public static void ValidateVersion(TeeVersionInput input, ValidationErrors errors, bool requireEffectiveFrom, string prefix = "")
    {
        if (requireEffectiveFrom && input.EffectiveFrom is null)
        {
            errors.Add(prefix + "effectiveFrom", "EffectiveFrom is required.");
        }

        if (input.Rating is null)
        {
            errors.Add(prefix + "rating", "Rating is required.");
        }

        Rating(input.Rating, prefix + "rating", errors);
        Rating(input.FrontRating, prefix + "frontRating", errors);
        Rating(input.BackRating, prefix + "backRating", errors);

        if (input.Slope is null)
        {
            errors.Add(prefix + "slope", "Slope is required.");
        }

        Slope(input.Slope, prefix + "slope", errors);
        Slope(input.FrontSlope, prefix + "frontSlope", errors);
        Slope(input.BackSlope, prefix + "backSlope", errors);

        var holes = input.Holes;
        if (holes is null || holes.Count == 0)
        {
            errors.Add(prefix + "holes", "At least one hole is required.");
            return;
        }

        if (holes.Count > MaxHoles)
        {
            errors.Add(prefix + "holes", $"A tee has at most {MaxHoles} holes.");
            return;
        }

        var numbers = new HashSet<int>();
        var strokeIndexes = new HashSet<int>();
        for (var i = 0; i < holes.Count; i++)
        {
            var hole = holes[i];
            var key = $"{prefix}holes[{i}].";
            if (hole is null)
            {
                errors.Add($"{prefix}holes[{i}]", "Hole is required.");
                continue;
            }

            if (hole.Number < 1 || hole.Number > MaxHoles)
            {
                errors.Add(key + "number", $"Hole number must be between 1 and {MaxHoles}.");
            }
            else if (!numbers.Add(hole.Number))
            {
                errors.Add(key + "number", $"Hole number {hole.Number} is used more than once.");
            }

            if (hole.Par < MinPar || hole.Par > MaxPar)
            {
                errors.Add(key + "par", $"Par must be between {MinPar} and {MaxPar}.");
            }

            if (hole.Yardage < 0 || hole.Yardage > MaxYardage)
            {
                errors.Add(key + "yardage", $"Yardage must be between 0 and {MaxYardage}.");
            }

            if (hole.StrokeIndex < 1 || hole.StrokeIndex > holes.Count)
            {
                errors.Add(key + "strokeIndex", $"Stroke index must be between 1 and {holes.Count}.");
            }
            else if (!strokeIndexes.Add(hole.StrokeIndex))
            {
                errors.Add(key + "strokeIndex", $"Stroke index {hole.StrokeIndex} is used more than once.");
            }
        }
    }

    /// <summary>
    /// Writes a validated <paramref name="input"/> onto <paramref name="version"/>: numbers, holes (matched by number, so
    /// unchanged holes keep their rows) and the par totals computed from the holes.
    /// </summary>
    public static void ApplyVersion(TeeVersion version, TeeVersionInput input)
    {
        if (input.EffectiveFrom is { } effectiveFrom)
        {
            version.EffectiveFrom = effectiveFrom;
        }

        version.Rating = Math.Round(input.Rating!.Value, 1);
        version.Slope = input.Slope!.Value;
        version.FrontRating = input.FrontRating is { } fr ? Math.Round(fr, 1) : null;
        version.FrontSlope = input.FrontSlope;
        version.BackRating = input.BackRating is { } br ? Math.Round(br, 1) : null;
        version.BackSlope = input.BackSlope;
        SetHoles(version, input.Holes!.Select(h => (h.Number, h.Par, h.Yardage, h.StrokeIndex)).ToList());
    }

    /// <summary>Replaces <paramref name="version"/>'s holes and recomputes par, front par and back par.</summary>
    public static void SetHoles(TeeVersion version, IReadOnlyList<(int Number, int Par, int Yardage, int StrokeIndex)> holes)
    {
        var wanted = holes.ToDictionary(h => h.Number);
        version.Holes.RemoveAll(h => !wanted.ContainsKey(h.Number));
        foreach (var hole in holes.OrderBy(h => h.Number))
        {
            var existing = version.Holes.Find(h => h.Number == hole.Number);
            if (existing is null)
            {
                existing = new TeeHole { Number = hole.Number };
                version.Holes.Add(existing);
            }

            existing.Par = hole.Par;
            existing.Yardage = hole.Yardage;
            existing.StrokeIndex = hole.StrokeIndex;
        }

        version.Par = holes.Sum(h => h.Par);
        var numbers = holes.Select(h => h.Number).ToHashSet();
        var isFullEighteen = numbers.Count == 18 && numbers.SetEquals(Enumerable.Range(1, 18));
        version.FrontPar = isFullEighteen ? holes.Where(h => h.Number <= 9).Sum(h => h.Par) : null;
        version.BackPar = isFullEighteen ? holes.Where(h => h.Number >= 10).Sum(h => h.Par) : null;
    }

    /// <summary>Same rule as <see cref="TeeVersionService.ResolveAsync"/>: latest with EffectiveFrom &lt;= date, else earliest.</summary>
    public static Guid? VersionInEffect(IEnumerable<(Guid Id, DateOnly EffectiveFrom)> versions, DateOnly date)
    {
        var list = versions.ToList();
        if (list.Count == 0)
        {
            return null;
        }

        var inEffect = list.Where(v => v.EffectiveFrom <= date).OrderByDescending(v => v.EffectiveFrom).FirstOrDefault();
        return inEffect != default ? inEffect.Id : list.MinBy(v => v.EffectiveFrom).Id;
    }

    /// <summary>Great-circle distance in statute miles.</summary>
    public static double DistanceMiles(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusMiles = 3958.8;
        static double Rad(double degrees) => degrees * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * earthRadiusMiles * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static void ApplyDetails(Course course, CourseDetailsRequest details)
    {
        course.Name = details.Name!.Trim();
        course.ClubName = Clean(details.ClubName);
        course.Address = Clean(details.Address);
        course.City = Clean(details.City);
        course.Region = Clean(details.Region);
        course.Country = Clean(details.Country);
        course.Phone = Clean(details.Phone);
        course.Website = Clean(details.Website);
        course.Latitude = details.Latitude;
        course.Longitude = details.Longitude;
    }

    private static void MaxLength(string? value, int max, string key, ValidationErrors errors)
    {
        if (value is not null && value.Trim().Length > max)
        {
            errors.Add(key, $"At most {max} characters.");
        }
    }

    private static void Rating(decimal? value, string key, ValidationErrors errors)
    {
        if (value is { } v && (v < MinRating || v > MaxRating))
        {
            errors.Add(key, $"Rating must be between {MinRating} and {MaxRating}.");
        }
    }

    private static void Slope(int? value, string key, ValidationErrors errors)
    {
        if (value is { } v && (v < MinSlope || v > MaxSlope))
        {
            errors.Add(key, $"Slope must be between {MinSlope} and {MaxSlope}.");
        }
    }
}
