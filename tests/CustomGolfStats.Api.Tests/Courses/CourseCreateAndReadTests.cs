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
    public async Task Manual_create_with_device_ids_is_idempotent_for_its_creator()
    {
        var client = await TestAuth.RegisterAsync(factory, "offline-creator@example.com");
        var other = await TestAuth.RegisterAsync(factory, "offline-other@example.com");
        var courseId = Guid.NewGuid();
        var teeId = Guid.NewGuid();
        object Body(string name) => new
        {
            id = courseId,
            name,
            tees = new object[] { new { id = teeId, name = "White", rating = 70.1m, slope = 124, holes = Holes() } },
        };

        var created = await (await client.PostAsJsonAsync("/courses", Body("Offline Links"), Ct)).ReadAsync<CourseDetailDto>(HttpStatusCode.Created);
        var retried = await (await client.PostAsJsonAsync("/courses", Body("Offline Links"), Ct)).ReadAsync<CourseDetailDto>(HttpStatusCode.OK);
        var renamed = await client.PostAsJsonAsync("/courses", Body("Another Name"), Ct);
        var someoneElse = await other.PostAsJsonAsync("/courses", Body("Offline Links"), Ct);
        var teeClash = await client.PostAsJsonAsync("/courses", new
        {
            name = "Tee Clash",
            tees = new object[] { new { id = teeId, name = "White", rating = 70.1m, slope = 124, holes = Holes() } },
        }, Ct);

        Assert.Equal((courseId, teeId), (created.Id, Assert.Single(created.Tees).Id));
        Assert.Equal(courseId, retried.Id);
        Assert.Single(retried.Tees);
        Assert.Equal(HttpStatusCode.Conflict, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, someoneElse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, teeClash.StatusCode);
        Assert.Equal(1, await factory.WithDbAsync(db => Task.FromResult(db.Courses.Count(c => c.Id == courseId))));
    }

    [Fact]
    public async Task Adding_a_tee_creates_it_with_its_first_version()
    {
        var client = await TestAuth.RegisterAsync(factory, "tee-adder@example.com");
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, name: "Add Tee GC", status: CourseStatus.Imported));
        factory.Time.Advance(TimeSpan.FromMinutes(1));
        var teeId = Guid.NewGuid();
        var body = new { id = teeId, name = "Gold", gender = "Female", effectiveFrom = new DateOnly(2024, 1, 1), rating = 74.0m, slope = 140, holes = Holes() };

        var tee = await (await client.PostAsJsonAsync($"/courses/{seeded.Course.Id}/tees", body, Ct)).ReadAsync<TeeDto>(HttpStatusCode.Created);
        var retried = await (await client.PostAsJsonAsync($"/courses/{seeded.Course.Id}/tees", body, Ct)).ReadAsync<TeeDto>(HttpStatusCode.OK);
        var invalid = await (await client.PostAsJsonAsync($"/courses/{seeded.Course.Id}/tees", new { name = "", rating = 74.0m, slope = 10 }, Ct))
            .ReadValidationErrorsAsync();
        var missing = await client.PostAsJsonAsync($"/courses/{Guid.NewGuid()}/tees", body, Ct);

        Assert.Equal((teeId, "Gold", Gender.Female), (tee.Id, tee.Name, tee.Gender));
        var version = Assert.Single(tee.Versions);
        Assert.Equal((new DateOnly(2024, 1, 1), 72, 74.0m, 140), (version.EffectiveFrom, version.Par, version.Rating, version.Slope));
        Assert.Equal(teeId, retried.Id);
        Assert.Contains("name", invalid.Keys);
        Assert.Contains("slope", invalid.Keys);
        Assert.Contains("holes", invalid.Keys);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var course = await factory.WithDbAsync(db => Task.FromResult(db.Courses.Single(c => c.Id == seeded.Course.Id)));
        Assert.Equal((CourseStatus.UserEdited, factory.Time.GetUtcNow()), (course.Status, course.UpdatedAt));
    }

    [Fact]
    public async Task Adding_a_tee_to_a_verified_course_as_a_golfer_becomes_a_flag()
    {
        var client = await TestAuth.RegisterAsync(factory, "tee-proposer@example.com");
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, name: "Verified Tee GC", status: CourseStatus.Verified));

        var flag = await (await client.PostAsJsonAsync($"/courses/{seeded.Course.Id}/tees",
                new { name = "Gold", rating = 74.0m, slope = 140, holes = Holes() }, Ct))
            .ReadAsync<CourseFlagDto>(HttpStatusCode.Accepted);

        Assert.Equal(seeded.Course.Id, flag.CourseId);
        Assert.Contains("Gold", flag.Note);
        Assert.Equal(1, await factory.WithDbAsync(db => Task.FromResult(db.Tees.Count(t => t.CourseId == seeded.Course.Id))));
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
