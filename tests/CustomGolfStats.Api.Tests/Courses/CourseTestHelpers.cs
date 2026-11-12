using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CustomGolfStats.Api.Features.Courses;
using CustomGolfStats.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace CustomGolfStats.Api.Tests.Courses;

internal static class CourseTestHelpers
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The recorded GolfCourseAPI sample as a provider course (id 18734).</summary>
    public static ProviderCourse SampleProviderCourse()
    {
        using var json = JsonDocument.Parse(GolfCourseApiSamples.Course);
        return GolfCourseApiMapper.MapCourseResponse(json.RootElement)!;
    }

    public static ProviderCourse SimpleProviderCourse(string id, string club, string course, string city = "Bend") =>
        new(id, club, course, null, city, "OR", "United States", 44.0, -121.3,
            [new ProviderTee("White", Data.Entities.Gender.Male, 36, 35.0m, 120, null, null, null, null,
                Enumerable.Range(1, 9).Select(i => new ProviderHole(4, 350, i)).ToList())]);

    public static object[] Holes(int[]? pars = null, int[]? strokeIndexes = null)
    {
        pars ??= TestData.DefaultPars;
        strokeIndexes ??= TestData.DefaultStrokeIndexes;
        return pars.Select((par, i) => (object)new { number = i + 1, par, yardage = 300 + i, strokeIndex = strokeIndexes[i] }).ToArray();
    }

    public static object VersionBody(DateOnly? effectiveFrom = null, decimal rating = 71.5m, int slope = 131, int[]? pars = null) => new
    {
        effectiveFrom,
        rating,
        slope,
        frontRating = 35.9m,
        frontSlope = 130,
        backRating = 35.6m,
        backSlope = 132,
        holes = Holes(pars),
    };

    public static object DetailsBody(string name, string? city = "Bend") => new
    {
        name,
        clubName = "Club",
        address = "1 Fairway Dr",
        city,
        region = "OR",
        country = "United States",
        phone = "555-0100",
        website = "https://example.com",
        latitude = 44.0,
        longitude = -121.3,
    };

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            Assert.Fail($"Expected {(int)expected} but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    public static async Task<IDictionary<string, string[]>> ReadValidationErrorsAsync(this HttpResponseMessage response) =>
        (await response.ReadAsync<HttpValidationProblemDetails>(HttpStatusCode.BadRequest)).Errors;
}
