using System.Net;
using System.Net.Http.Json;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Features.Courses;
using CustomGolfStats.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CustomGolfStats.Api.Tests.Courses.CourseTestHelpers;

namespace CustomGolfStats.Api.Tests.Courses;

public sealed class CourseImportTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Import_creates_the_course_with_one_version_per_tee()
    {
        factory.CourseProvider.Add(SampleProviderCourse());
        var client = await TestAuth.RegisterAsync(factory, "importer@example.com");
        var accountId = await TestAuth.GetAccountIdAsync(factory, "importer@example.com");

        var course = await (await client.PostAsJsonAsync("/courses/import", new { providerCourseId = "18734" }, Ct))
            .ReadAsync<CourseDetailDto>(HttpStatusCode.Created);

        Assert.Equal("Cedar Ridge Golf Club - Ridge Course", course.Name);
        Assert.Equal("Cedar Ridge Golf Club", course.ClubName);
        Assert.Equal("Bend", course.City);
        Assert.Equal("OR", course.Region);
        Assert.Equal(44.0216, course.Latitude);
        Assert.Equal(CourseStatus.Imported, course.Status);
        Assert.Equal(CourseSource.GolfCourseApi, course.Source);
        Assert.Equal("18734", course.ProviderCourseId);
        Assert.Equal(["Blue/Male", "White/Male", "Red/Female"], course.Tees.Select(t => $"{t.Name}/{t.Gender}"));

        var blue = Assert.Single(course.Tees[0].Versions);
        Assert.Equal(new DateOnly(1900, 1, 1), blue.EffectiveFrom);
        Assert.True(blue.IsCurrent);
        Assert.Equal(CourseSource.GolfCourseApi, blue.Source);
        Assert.Equal((72, 73.4m, 135, 36, 36, 36.8m, 133, 36.6m, 137),
            (blue.Par, blue.Rating, blue.Slope, blue.FrontPar, blue.BackPar, blue.FrontRating, blue.FrontSlope, blue.BackRating, blue.BackSlope));
        Assert.Equal(Enumerable.Range(1, 18), blue.Holes.Select(h => h.Number));
        Assert.Equal(new TeeHoleDto(2, 5, 548, 1), blue.Holes[1]);

        var changedBy = await factory.WithDbAsync(db =>
            db.TeeVersions.Where(v => v.Id == blue.Id).Select(v => v.ChangedByAccountId).SingleAsync(Ct));
        Assert.Equal(accountId, changedBy);
    }

    [Fact]
    public async Task Importing_an_already_imported_course_returns_it_without_asking_the_provider()
    {
        factory.CourseProvider.Add(SimpleProviderCourse("idem-1", "Idem Club", "Idem Course"));
        var client = await TestAuth.RegisterAsync(factory, "idem@example.com");
        var first = await (await client.PostAsJsonAsync("/courses/import", new { providerCourseId = "idem-1" }, Ct))
            .ReadAsync<CourseDetailDto>(HttpStatusCode.Created);
        var getCalls = factory.CourseProvider.GetCalls;

        var second = await (await client.PostAsJsonAsync("/courses/import", new { providerCourseId = "idem-1" }, Ct))
            .ReadAsync<CourseDetailDto>(HttpStatusCode.OK);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(getCalls, factory.CourseProvider.GetCalls);
        Assert.Equal(1, await factory.WithDbAsync(db => db.Courses.CountAsync(c => c.ProviderCourseId == "idem-1", Ct)));
    }

    [Fact]
    public async Task Import_of_an_unknown_course_is_404_and_a_blank_id_is_400()
    {
        var client = await TestAuth.RegisterAsync(factory, "unknown@example.com");

        var unknown = await client.PostAsJsonAsync("/courses/import", new { providerCourseId = "nope" }, Ct);
        var blank = await client.PostAsJsonAsync("/courses/import", new { providerCourseId = " " }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Contains("providerCourseId", (await blank.ReadValidationErrorsAsync()).Keys);
    }

    [Fact]
    public async Task Provider_search_marks_courses_that_are_already_imported()
    {
        factory.CourseProvider.Add(SimpleProviderCourse("mark-1", "Marker Club", "East"));
        factory.CourseProvider.Add(SimpleProviderCourse("mark-2", "Marker Club", "West"));
        var client = await TestAuth.RegisterAsync(factory, "marker@example.com");
        var imported = await (await client.PostAsJsonAsync("/courses/import", new { providerCourseId = "mark-1" }, Ct))
            .ReadAsync<CourseDetailDto>(HttpStatusCode.Created);

        var results = await (await client.GetAsync("/courses/provider-search?query=marker", Ct))
            .ReadAsync<List<ProviderSearchResultDto>>(HttpStatusCode.OK);

        Assert.Equal(2, results.Count);
        Assert.Equal(new ProviderSearchResultDto("mark-1", "Marker Club", "East", "Bend", "OR", "United States", true, imported.Id), results[0]);
        Assert.False(results[1].AlreadyImported);
        Assert.Null(results[1].CourseId);
    }

    [Fact]
    public async Task Provider_search_needs_a_query()
    {
        var client = await TestAuth.RegisterAsync(factory, "noquery@example.com");
        var calls = factory.CourseProvider.SearchCalls;

        var errors = await (await client.GetAsync("/courses/provider-search?query=", Ct)).ReadValidationErrorsAsync();

        Assert.Contains("query", errors.Keys);
        Assert.Equal(calls, factory.CourseProvider.SearchCalls);
    }

    [Fact]
    public async Task Provider_failures_are_502()
    {
        using var broken = factory.WithTestServices(s => s.AddSingleton<ICourseProvider>(new ThrowingProvider()));
        var client = await TestAuth.RegisterAsync(broken, "broken@example.com");

        var search = await client.GetAsync("/courses/provider-search?query=x", Ct);
        var import = await client.PostAsJsonAsync("/courses/import", new { providerCourseId = "x" }, Ct);

        Assert.Equal(HttpStatusCode.BadGateway, search.StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, import.StatusCode);
    }

    private sealed class ThrowingProvider : ICourseProvider
    {
        public Task<IReadOnlyList<ProviderCourseSummary>> SearchAsync(string query, CancellationToken ct) =>
            throw new CourseProviderException("down");

        public Task<ProviderCourse?> GetAsync(string providerCourseId, CancellationToken ct) =>
            throw new CourseProviderException("down");
    }
}
