using System.Net;
using System.Net.Http.Json;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Features.Courses;
using CustomGolfStats.Api.Tests.Infrastructure;
using static CustomGolfStats.Api.Tests.Courses.CourseTestHelpers;

namespace CustomGolfStats.Api.Tests.Courses;

public sealed class CourseCreateAndReadTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Manual_create_stores_a_user_edited_course_with_its_tees_and_holes()
    {
        var client = await TestAuth.RegisterAsync(factory, "creator@example.com");
        var body = new
        {
            name = "  Backyard Links  ",
            city = "Sisters",
            region = "OR",
            latitude = 44.29,
            longitude = -121.55,
            tees = new object[]
            {
                new { name = "Blue", gender = "Male", rating = 70.1m, slope = 124, holes = Holes() },
                new
                {
                    name = "Red", gender = "Female", effectiveFrom = new DateOnly(2025, 3, 1), rating = 34.0m, slope = 115,
                    holes = Holes([4, 3, 5, 4, 4, 3, 4, 5, 4], [1, 2, 3, 4, 5, 6, 7, 8, 9]),
                },
            },
        };

        var response = await client.PostAsJsonAsync("/courses", body, Ct);
        var course = await response.ReadAsync<CourseDetailDto>(HttpStatusCode.Created);

        Assert.Equal($"/courses/{course.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal("Backyard Links", course.Name);
        Assert.Equal(CourseSource.Manual, course.Source);
        Assert.Equal(CourseStatus.UserEdited, course.Status);
        Assert.Null(course.ProviderCourseId);

        var blue = Assert.Single(course.Tees[0].Versions);
        Assert.Equal(Gender.Male, course.Tees[0].Gender);
        Assert.Equal(new DateOnly(1900, 1, 1), blue.EffectiveFrom);
        Assert.Equal((72, 36, 36, 70.1m, 124), (blue.Par, blue.FrontPar, blue.BackPar, blue.Rating, blue.Slope));
        Assert.Equal(CourseSource.Manual, blue.Source);
        Assert.Equal(18, blue.Holes.Count);

        var red = Assert.Single(course.Tees[1].Versions);
        Assert.Equal(new DateOnly(2025, 3, 1), red.EffectiveFrom);
        Assert.Equal(36, red.Par);
        Assert.Null(red.FrontPar);
        Assert.Null(red.BackPar);

        var read = await (await client.GetAsync($"/courses/{course.Id}", Ct)).ReadAsync<CourseDetailDto>(HttpStatusCode.OK);
        Assert.Equal(course.Id, read.Id);
        Assert.Equal(2, read.Tees.Count);
    }

    [Fact]
    public async Task Manual_create_validates_holes()
    {
        var client = await TestAuth.RegisterAsync(factory, "invalid@example.com");
        var holes = new object[]
        {
            new { number = 1, par = 4, yardage = 380, strokeIndex = 1 },
            new { number = 1, par = 4, yardage = 380, strokeIndex = 2 }, // duplicate number
            new { number = 3, par = 2, yardage = 100, strokeIndex = 3 }, // par too low
            new { number = 4, par = 7, yardage = 700, strokeIndex = 3 }, // par too high, duplicate stroke index
            new { number = 5, par = 4, yardage = 380, strokeIndex = 6 }, // stroke index outside 1..5
        };
        var body = new
        {
            name = "",
            tees = new object[] { new { name = "White", gender = "Male", rating = 70.0m, slope = 300, holes } },
        };

        var errors = await (await client.PostAsJsonAsync("/courses", body, Ct)).ReadValidationErrorsAsync();

        Assert.Equal(
            ["name", "tees[0].holes[1].number", "tees[0].holes[2].par", "tees[0].holes[3].par", "tees[0].holes[3].strokeIndex",
                "tees[0].holes[4].strokeIndex", "tees[0].slope"],
            errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Manual_create_needs_at_least_one_tee_with_holes()
    {
        var client = await TestAuth.RegisterAsync(factory, "notees@example.com");

        var noTees = await (await client.PostAsJsonAsync("/courses", new { name = "Empty", tees = Array.Empty<object>() }, Ct))
            .ReadValidationErrorsAsync();
        var noHoles = await (await client.PostAsJsonAsync("/courses", new
        {
            name = "Holeless",
            tees = new object[] { new { name = "", rating = 70.0m, slope = 120, holes = Array.Empty<object>() } },
        }, Ct)).ReadValidationErrorsAsync();

        Assert.Contains("tees", noTees.Keys);
        Assert.Contains("tees[0].name", noHoles.Keys);
        Assert.Contains("tees[0].holes", noHoles.Keys);
    }

    [Fact]
    public async Task Read_returns_every_version_and_flags_the_one_in_effect_today()
    {
        // The fake clock says 2026-06-01: the 2024 version is current, 2027 is in the future.
        var seeded = await factory.WithDbAsync(async db =>
        {
            var s = await TestData.SeedCourseAsync(db, name: "Versioned");
            await TestData.AddTeeVersionAsync(db, s.Tee, new DateOnly(2027, 1, 1));
            await TestData.AddTeeVersionAsync(db, s.Tee, new DateOnly(2024, 1, 1));
            return s;
        });
        var client = await TestAuth.RegisterAsync(factory, "reader@example.com");

        var course = await (await client.GetAsync($"/courses/{seeded.Course.Id}", Ct)).ReadAsync<CourseDetailDto>(HttpStatusCode.OK);

        var versions = Assert.Single(course.Tees).Versions;
        Assert.Equal(["1900-01-01", "2024-01-01", "2027-01-01"], versions.Select(v => v.EffectiveFrom.ToString("yyyy-MM-dd")));
        Assert.Equal([false, true, false], versions.Select(v => v.IsCurrent));
        Assert.Equal(Enumerable.Range(1, 18), versions[1].Holes.Select(h => h.Number));
    }

    [Fact]
    public async Task Read_of_an_unknown_course_is_404()
    {
        var client = await TestAuth.RegisterAsync(factory, "missing@example.com");

        var response = await client.GetAsync($"/courses/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
