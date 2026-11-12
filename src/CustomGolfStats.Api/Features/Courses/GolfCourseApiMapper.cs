using System.Globalization;
using System.Text.Json;
using CustomGolfStats.Api.Data.Entities;

namespace CustomGolfStats.Api.Features.Courses;

/// <summary>
/// Maps GolfCourseAPI v1 JSON to provider records. Tolerant by design: every field may be missing, null, or a string
/// holding a number; unknown fields are ignored. Tees with no holes, or with a hole that has no par, are dropped.
/// </summary>
public static class GolfCourseApiMapper
{
    /// <summary><c>GET search</c>: <c>{ "courses": [...] }</c>. Entries without an id are skipped.</summary>
    public static IReadOnlyList<ProviderCourseSummary> MapSearch(JsonElement root)
    {
        if (!TryGetProperty(root, "courses", out var courses) || courses.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<ProviderCourseSummary>();
        foreach (var course in courses.EnumerateArray())
        {
            if (course.ValueKind != JsonValueKind.Object || GetId(course) is not { } id)
            {
                continue;
            }

            var location = GetObject(course, "location");
            results.Add(new ProviderCourseSummary(
                id,
                GetString(course, "club_name") ?? "",
                GetString(course, "course_name") ?? "",
                GetString(location, "city"),
                GetString(location, "state"),
                GetString(location, "country")));
        }

        return results;
    }

    /// <summary><c>GET courses/{id}</c>: <c>{ "course": {...} }</c> or the bare course object.</summary>
    public static ProviderCourse? MapCourseResponse(JsonElement root)
    {
        var course = TryGetProperty(root, "course", out var inner) && inner.ValueKind == JsonValueKind.Object ? inner : root;
        return MapCourse(course);
    }

    public static ProviderCourse? MapCourse(JsonElement course)
    {
        if (course.ValueKind != JsonValueKind.Object || GetId(course) is not { } id)
        {
            return null;
        }

        var location = GetObject(course, "location");
        var tees = new List<ProviderTee>();
        var teeGroups = GetObject(course, "tees");
        foreach (var (key, gender) in new[] { ("male", Gender.Male), ("female", Gender.Female) })
        {
            if (!TryGetProperty(teeGroups, key, out var list) || list.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var tee in list.EnumerateArray())
            {
                if (MapTee(tee, gender) is { } mapped)
                {
                    tees.Add(mapped);
                }
            }
        }

        return new ProviderCourse(
            id,
            GetString(course, "club_name") ?? "",
            GetString(course, "course_name") ?? "",
            GetString(location, "address"),
            GetString(location, "city"),
            GetString(location, "state"),
            GetString(location, "country"),
            GetDouble(location, "latitude"),
            GetDouble(location, "longitude"),
            tees);
    }

    /// <summary>
    /// Our course name: the course name when it already includes the club name (or the club name when it includes
    /// the course name), otherwise "Club - Course".
    /// </summary>
    public static string DisplayName(string clubName, string courseName)
    {
        var club = clubName.Trim();
        var course = courseName.Trim();
        if (club.Length == 0 || course.Contains(club, StringComparison.OrdinalIgnoreCase))
        {
            return course.Length == 0 ? club : course;
        }

        if (course.Length == 0 || club.Contains(course, StringComparison.OrdinalIgnoreCase))
        {
            return club;
        }

        return $"{club} - {course}";
    }

    private static ProviderTee? MapTee(JsonElement tee, Gender gender)
    {
        if (tee.ValueKind != JsonValueKind.Object
            || !TryGetProperty(tee, "holes", out var holesElement)
            || holesElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var raw = new List<(int Par, int Yardage, int? Handicap)>();
        foreach (var hole in holesElement.EnumerateArray())
        {
            if (GetInt(hole, "par") is not { } par)
            {
                return null;
            }

            raw.Add((par, GetInt(hole, "yardage") ?? 0, GetInt(hole, "handicap")));
        }

        if (raw.Count == 0)
        {
            return null;
        }

        // Handicaps are used only when every hole has one, they are distinct and within 1..18 (a 9-hole tee may use
        // 1..18 odd or even numbers). Otherwise number the holes in order.
        var handicaps = raw.Select(h => h.Handicap).ToList();
        var useHandicaps = handicaps.All(h => h is >= 1 and <= 18) && handicaps.Distinct().Count() == raw.Count;
        var holes = raw.Select((h, i) => new ProviderHole(h.Par, h.Yardage, useHandicaps ? h.Handicap!.Value : i + 1)).ToList();

        var name = GetString(tee, "tee_name");
        return new ProviderTee(
            string.IsNullOrWhiteSpace(name) ? "Tee" : name.Trim(),
            gender,
            GetInt(tee, "par_total"),
            GetDecimal(tee, "course_rating"),
            GetInt(tee, "slope_rating"),
            GetDecimal(tee, "front_course_rating"),
            GetInt(tee, "front_slope_rating"),
            GetDecimal(tee, "back_course_rating"),
            GetInt(tee, "back_slope_rating"),
            holes);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static JsonElement GetObject(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.Object ? value : default;

    private static string? GetId(JsonElement element)
    {
        var id = GetString(element, "id");
        return string.IsNullOrWhiteSpace(id) ? null : id.Trim();
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static decimal? GetDecimal(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
    }

    private static int? GetInt(JsonElement element, string name) =>
        GetDecimal(element, name) is { } value && value == decimal.Truncate(value) && value is >= int.MinValue and <= int.MaxValue
            ? (int)value
            : null;

    private static double? GetDouble(JsonElement element, string name) => (double?)GetDecimal(element, name);
}
