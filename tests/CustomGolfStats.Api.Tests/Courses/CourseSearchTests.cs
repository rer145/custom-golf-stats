using System.Net;
using CustomGolfStats.Api.Features.Courses;
using CustomGolfStats.Api.Tests.Infrastructure;
using static CustomGolfStats.Api.Tests.Courses.CourseTestHelpers;

namespace CustomGolfStats.Api.Tests.Courses;

public sealed class CourseSearchTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Search_matches_name_club_and_city_case_insensitively_and_hides_merged_courses()
    {
        await factory.WithDbAsync(async db =>
        {
            var target = await TestData.SeedCourseAsync(db, name: "Sunset Hills", city: "Bend");
            await TestData.SeedCourseAsync(db, name: "Ocean Dunes", city: "SUNSET Beach");
            var club = await TestData.SeedCourseAsync(db, name: "North", city: "Salem");
            club.Course.ClubName = "Sunsetter Club";
            var merged = await TestData.SeedCourseAsync(db, name: "Sunset Hills (old)");
            merged.Course.MergedIntoCourseId = target.Course.Id;
            await TestData.SeedCourseAsync(db, name: "Unrelated", city: "Portland");
            await db.SaveChangesAsync();
        });
        var client = await TestAuth.RegisterAsync(factory, "search@example.com");

        var results = await (await client.GetAsync("/courses?query=sUnSeT", Ct))
            .ReadAsync<List<CourseSummaryDto>>(HttpStatusCode.OK);

        Assert.Equal(["North", "Ocean Dunes", "Sunset Hills"], results.Select(r => r.Name));
        Assert.All(results, r => Assert.Null(r.DistanceMiles));
    }

    [Fact]
    public async Task Search_treats_like_wildcards_literally()
    {
        await factory.WithDbAsync(async db =>
        {
            await TestData.SeedCourseAsync(db, name: "100% Golf");
            await TestData.SeedCourseAsync(db, name: "Plain Name Golf");
        });
        var client = await TestAuth.RegisterAsync(factory, "wildcards@example.com");

        var percent = await (await client.GetAsync("/courses?query=%25", Ct)).ReadAsync<List<CourseSummaryDto>>(HttpStatusCode.OK);
        var underscore = await (await client.GetAsync("/courses?query=n_n", Ct)).ReadAsync<List<CourseSummaryDto>>(HttpStatusCode.OK);

        Assert.Equal(["100% Golf"], percent.Select(r => r.Name));
        Assert.Empty(underscore);
    }

    [Fact]
    public async Task Near_returns_nearby_courses_closest_first()
    {
        await factory.WithDbAsync(async db =>
        {
            await TestData.SeedCourseAsync(db, name: "Near B", latitude: 10.30, longitude: 10.0);
            await TestData.SeedCourseAsync(db, name: "Near A", latitude: 10.01, longitude: 10.01);
            await TestData.SeedCourseAsync(db, name: "Far Away", latitude: 12.0, longitude: 10.0);
        });
        var client = await TestAuth.RegisterAsync(factory, "near@example.com");

        var results = await (await client.GetAsync("/courses?near=10.0,10.0", Ct)).ReadAsync<List<CourseSummaryDto>>(HttpStatusCode.OK);

        Assert.Equal(["Near A", "Near B"], results.Select(r => r.Name));
        Assert.InRange(results[0].DistanceMiles!.Value, 0.1, 2);
        Assert.InRange(results[1].DistanceMiles!.Value, 20, 30);
    }

    [Fact]
    public async Task Query_with_near_orders_matches_by_distance_with_unknown_locations_last()
    {
        await factory.WithDbAsync(async db =>
        {
            await TestData.SeedCourseAsync(db, name: "Qn Alpha", latitude: 40.0, longitude: -100.0);
            await TestData.SeedCourseAsync(db, name: "Qn Bravo", latitude: 44.0, longitude: -121.3);
            await TestData.SeedCourseAsync(db, name: "Qn Charlie");
        });
        var client = await TestAuth.RegisterAsync(factory, "qnear@example.com");

        var results = await (await client.GetAsync("/courses?query=qn&near=44.0,-121.3", Ct)).ReadAsync<List<CourseSummaryDto>>(HttpStatusCode.OK);

        Assert.Equal(["Qn Bravo", "Qn Alpha", "Qn Charlie"], results.Select(r => r.Name));
    }

    [Theory]
    [InlineData("/courses", "query")]
    [InlineData("/courses?query=%20%20", "query")]
    [InlineData("/courses?near=abc", "near")]
    [InlineData("/courses?near=91,0", "near")]
    [InlineData("/courses?query=x&near=10,181", "near")]
    public async Task Invalid_search_is_a_validation_problem(string url, string key)
    {
        var client = await TestAuth.RegisterAsync(factory, $"bad-{Guid.NewGuid():N}@example.com");

        var errors = await (await client.GetAsync(url, Ct)).ReadValidationErrorsAsync();

        Assert.Contains(key, errors.Keys);
    }

    [Fact]
    public async Task Anonymous_callers_are_rejected()
    {
        var response = await factory.CreateClient().GetAsync("/courses?query=x", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
