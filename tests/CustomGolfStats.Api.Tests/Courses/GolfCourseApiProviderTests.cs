using System.Net;
using System.Text;
using System.Text.Json;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Features.Courses;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CustomGolfStats.Api.Tests.Courses;

/// <summary>GolfCourseAPI mapping and HTTP adapter, against recorded responses. No network.</summary>
public sealed class GolfCourseApiProviderTests
{
    [Fact]
    public void Maps_a_wrapped_course_response()
    {
        using var json = JsonDocument.Parse(GolfCourseApiSamples.Course);

        var course = GolfCourseApiMapper.MapCourseResponse(json.RootElement);

        Assert.NotNull(course);
        Assert.Equal("18734", course.ProviderCourseId);
        Assert.Equal("Cedar Ridge Golf Club", course.ClubName);
        Assert.Equal("Ridge Course", course.CourseName);
        Assert.Equal("4100 Cedar Ridge Rd, Bend, OR 97702, USA", course.Address);
        Assert.Equal("Bend", course.City);
        Assert.Equal("OR", course.Region);
        Assert.Equal("United States", course.Country);
        Assert.Equal(44.0216, course.Latitude);
        Assert.Equal(-121.3442, course.Longitude);

        Assert.Equal(["Blue/Male", "White/Male", "Red/Female"], course.Tees.Select(t => $"{t.Name}/{t.Gender}"));

        var blue = course.Tees[0];
        Assert.Equal(72, blue.Par);
        Assert.Equal(73.4m, blue.Rating);
        Assert.Equal(135, blue.Slope);
        Assert.Equal(36.8m, blue.FrontRating);
        Assert.Equal(133, blue.FrontSlope);
        Assert.Equal(36.6m, blue.BackRating);
        Assert.Equal(137, blue.BackSlope);
        Assert.Equal(18, blue.Holes.Count);
        Assert.Equal(new ProviderHole(4, 402, 7), blue.Holes[0]);
        Assert.Equal(new ProviderHole(5, 532, 4), blue.Holes[17]);
        Assert.Equal(72, blue.Holes.Sum(h => h.Par));

        // Red has no handicap on hole 7, so stroke indexes follow hole order.
        var red = course.Tees[2];
        Assert.Equal(Enumerable.Range(1, 18), red.Holes.Select(h => h.StrokeIndex));
        Assert.Equal(131, red.Holes[6].Yardage);
    }

    [Fact]
    public void Maps_a_bare_nine_hole_course_with_missing_and_string_fields()
    {
        using var json = JsonDocument.Parse(GolfCourseApiSamples.BareNineHoleCourse);

        var course = GolfCourseApiMapper.MapCourseResponse(json.RootElement);

        Assert.NotNull(course);
        Assert.Equal("9021", course.ProviderCourseId);
        Assert.Equal("", course.CourseName);
        Assert.Null(course.Latitude);
        Assert.Null(course.Address);

        // "Broken" has a hole without a par, so it is dropped.
        var tee = Assert.Single(course.Tees);
        Assert.Equal("Yellow", tee.Name);
        Assert.Null(tee.Par);
        Assert.Null(tee.Rating);
        Assert.Equal(112, tee.Slope);
        Assert.Equal(9, tee.Holes.Count);
        Assert.Equal(Enumerable.Range(1, 9), tee.Holes.Select(h => h.StrokeIndex));
    }

    [Fact]
    public void Maps_search_results_and_skips_entries_without_an_id()
    {
        using var json = JsonDocument.Parse(GolfCourseApiSamples.Search);

        var results = GolfCourseApiMapper.MapSearch(json.RootElement);

        Assert.Equal(2, results.Count);
        Assert.Equal(new ProviderCourseSummary("18734", "Cedar Ridge Golf Club", "Ridge Course", "Bend", "OR", "United States"), results[0]);
        Assert.Equal("9021", results[1].ProviderCourseId);
    }

    [Fact]
    public void Display_name_combines_club_and_course_only_when_they_differ()
    {
        Assert.Equal("Cedar Ridge Golf Club - Ridge Course", GolfCourseApiMapper.DisplayName("Cedar Ridge Golf Club", "Ridge Course"));
        Assert.Equal("Pebble Beach Golf Links", GolfCourseApiMapper.DisplayName("Pebble Beach Golf Links", "Pebble Beach Golf Links"));
        Assert.Equal("Torrey Pines South", GolfCourseApiMapper.DisplayName("Torrey Pines", "Torrey Pines South"));
        Assert.Equal("Cedar Ridge Municipal", GolfCourseApiMapper.DisplayName("Cedar Ridge Municipal", ""));
        Assert.Equal("Ridge", GolfCourseApiMapper.DisplayName("", "Ridge"));
    }

    [Fact]
    public async Task Registered_client_calls_the_api_with_the_key_header()
    {
        var handler = new RecordingHandler(_ => Json(GolfCourseApiSamples.Search));
        var provider = BuildProvider(handler, apiKey: "test-key");

        var results = await provider.SearchAsync("cedar ridge", TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.golfcourseapi.com/v1/search?search_query=cedar%20ridge", request.RequestUri!.AbsoluteUri);
        Assert.Equal("Key test-key", request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Get_reads_a_course_and_returns_null_for_404()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/18734", StringComparison.Ordinal)
            ? Json(GolfCourseApiSamples.Course)
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        var provider = BuildProvider(handler, apiKey: "test-key");

        var course = await provider.GetAsync("18734", TestContext.Current.CancellationToken);
        var missing = await provider.GetAsync("404404", TestContext.Current.CancellationToken);

        Assert.Equal("Ridge Course", course!.CourseName);
        Assert.Null(missing);
        Assert.Equal("https://api.golfcourseapi.com/v1/courses/18734", handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Provider_failures_become_CourseProviderException()
    {
        var failing = BuildProvider(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)), apiKey: "k");
        var garbage = BuildProvider(new RecordingHandler(_ => new HttpResponseMessage { Content = new StringContent("<html>") }), apiKey: "k");
        var unconfigured = BuildProvider(new RecordingHandler(_ => Json(GolfCourseApiSamples.Search)), apiKey: null);
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<CourseProviderException>(() => failing.SearchAsync("x", ct));
        await Assert.ThrowsAsync<CourseProviderException>(() => failing.GetAsync("1", ct));
        await Assert.ThrowsAsync<CourseProviderException>(() => garbage.SearchAsync("x", ct));
        await Assert.ThrowsAsync<CourseProviderException>(() => unconfigured.SearchAsync("x", ct));
    }

    private static ICourseProvider BuildProvider(HttpMessageHandler handler, string? apiKey)
    {
        var settings = new Dictionary<string, string?>();
        if (apiKey is not null)
        {
            settings["GolfCourseApi:ApiKey"] = apiKey;
        }

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddSingleton(TimeProvider.System);
        services.AddCoursesFeature();
        services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));
        return services.BuildServiceProvider().GetRequiredService<ICourseProvider>();
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }
}
