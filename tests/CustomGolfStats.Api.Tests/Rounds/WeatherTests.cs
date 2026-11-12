using System.Net;
using CustomGolfStats.Api.Features.Rounds;
using CustomGolfStats.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using static CustomGolfStats.Api.Tests.Rounds.RoundRequests;

namespace CustomGolfStats.Api.Tests.Rounds;

public sealed class WeatherTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly WeatherObservation Sunny = new("Clear sky", 71.5m, 8.2m, 270);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => factory.Time.GetUtcNow();

    [Fact]
    public async Task Weather_endpoint_proxies_the_provider()
    {
        var client = await TestAuth.RegisterAsync(factory, "proxy@example.com");
        factory.WeatherProvider.Observation = Sunny;
        factory.WeatherProvider.Fail = false;

        var response = await client.GetAsync("/weather?lat=40.5&lon=-75.25&at=2026-05-30T13:00:00Z", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var weather = await ReadAsync<WeatherDto>(response);
        Assert.Equal(new WeatherDto("Clear sky", 71.5m, 8.2m, 270), weather);
        Assert.Contains(factory.WeatherProvider.Calls, c =>
            c.Lat == 40.5 && c.Lon == -75.25 && c.At == new DateTimeOffset(2026, 5, 30, 13, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "not json {")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "{}")]
    public async Task Weather_endpoint_maps_provider_failures_to_502(HttpStatusCode providerStatus, string providerBody)
    {
        var real = new OpenMeteoWeatherProvider(
            new HttpClient(new CannedHandler(providerStatus, providerBody)),
            Options.Create(new OpenMeteoOptions()),
            factory.Time);
        var api = factory.WithTestServices(services => services.Replace(ServiceDescriptor.Singleton<IWeatherProvider>(real)));
        var client = await TestAuth.RegisterAsync(api, $"malformed-{(int)providerStatus}@example.com");

        var response = await client.GetAsync("/weather?lat=40&lon=-75&at=2026-05-30T13:00:00Z", Ct);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task Weather_endpoint_validates_its_query()
    {
        var client = await TestAuth.RegisterAsync(factory, "badquery@example.com");

        var response = await client.GetAsync("/weather?lat=91&lon=-181", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadAsync<HttpValidationProblemDetails>(response);
        Assert.True(problem.Errors.ContainsKey("lat"));
        Assert.True(problem.Errors.ContainsKey("lon"));
        Assert.True(problem.Errors.ContainsKey("at"));
    }

    [Fact]
    public async Task Push_fills_missing_weather_from_the_course_location()
    {
        var client = await TestAuth.RegisterAsync(factory, "fill@example.com");
        factory.WeatherProvider.Observation = Sunny;
        factory.WeatherProvider.Fail = false;
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, latitude: 33.5, longitude: -82.0));
        var id = Guid.NewGuid();
        var startedAt = new DateTimeOffset(2026, 5, 30, 14, 0, 0, TimeSpan.Zero);

        var response = await PutAsync(client, id, Quick(seeded.Tee.Id, Now, QuickHole(1, 4)) with { StartedAt = startedAt });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var round = await ReadAsync<RoundDto>(response);
        Assert.Equal(("Clear sky", 71.5m, 8.2m, 270), (round.WeatherConditions, round.TemperatureF, round.WindSpeedMph, round.WindDirectionDegrees));
        Assert.Contains(factory.WeatherProvider.Calls, c => c.Lat == 33.5 && c.Lon == -82.0 && c.At == startedAt);
        var stored = await ReadAsync<RoundDto>(await client.GetAsync($"/rounds/{id}", Ct));
        Assert.Equal("Clear sky", stored.WeatherConditions);

        // A later push without weather keeps what the server filled in, without asking the provider again.
        var calls = factory.WeatherProvider.Calls.Count;
        var again = await ReadAsync<RoundDto>(await PutAsync(client, id, Quick(seeded.Tee.Id, Now.AddMinutes(1), QuickHole(1, 5)) with { StartedAt = startedAt }));
        Assert.Equal("Clear sky", again.WeatherConditions);
        Assert.Equal(calls, factory.WeatherProvider.Calls.Count);
    }

    [Fact]
    public async Task Weather_sent_by_the_device_is_kept()
    {
        var client = await TestAuth.RegisterAsync(factory, "own@example.com");
        factory.WeatherProvider.Observation = Sunny;
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, latitude: 10, longitude: 10));
        var request = Quick(seeded.Tee.Id, Now, QuickHole(1, 4)) with
        {
            StartedAt = Now,
            WeatherConditions = "Rain",
            TemperatureF = 50m,
            WindSpeedMph = 20m,
            WindDirectionDegrees = 90,
        };

        var round = await ReadAsync<RoundDto>(await PutAsync(client, Guid.NewGuid(), request));

        Assert.Equal(("Rain", 50m, 20m, 90), (round.WeatherConditions, round.TemperatureF, round.WindSpeedMph, round.WindDirectionDegrees));
        Assert.DoesNotContain(factory.WeatherProvider.Calls, c => c.Lat == 10 && c.Lon == 10);
    }

    [Fact]
    public async Task Provider_failure_still_stores_the_round()
    {
        var client = await TestAuth.RegisterAsync(factory, "failing@example.com");
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, latitude: 20, longitude: 20));
        var id = Guid.NewGuid();
        factory.WeatherProvider.Fail = true;
        try
        {
            var response = await PutAsync(client, id, Quick(seeded.Tee.Id, Now, QuickHole(1, 4)) with { StartedAt = Now });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Null((await ReadAsync<RoundDto>(response)).WeatherConditions);
            Assert.Contains(factory.WeatherProvider.Calls, c => c.Lat == 20 && c.Lon == 20);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/rounds/{id}", Ct)).StatusCode);
        }
        finally
        {
            factory.WeatherProvider.Fail = false;
        }
    }

    [Fact]
    public async Task No_lookup_without_start_time_or_course_coordinates()
    {
        var client = await TestAuth.RegisterAsync(factory, "nolookup@example.com");
        factory.WeatherProvider.Observation = Sunny;
        var located = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, latitude: 30, longitude: 30));
        var unlocated = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db));
        var before = factory.WeatherProvider.Calls.Count;

        var noStart = await ReadAsync<RoundDto>(await PutAsync(client, Guid.NewGuid(), Quick(located.Tee.Id, Now, QuickHole(1, 4))));
        var noCoords = await ReadAsync<RoundDto>(await PutAsync(client, Guid.NewGuid(), Quick(unlocated.Tee.Id, Now, QuickHole(1, 4)) with { StartedAt = Now }));

        Assert.Null(noStart.WeatherConditions);
        Assert.Null(noCoords.WeatherConditions);
        Assert.DoesNotContain(factory.WeatherProvider.Calls.Skip(before), c => c.Lat == 30);
    }

    private sealed class CannedHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
