using System.Net;
using System.Text;
using CustomGolfStats.Api.Features.Rounds;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CustomGolfStats.Api.Tests.Rounds;

public sealed class OpenMeteoWeatherProviderTests
{
    // Recorded from https://archive-api.open-meteo.com/v1/archive?latitude=40.0&longitude=-75.0&start_date=2025-06-01
    // &end_date=2025-06-01&hourly=temperature_2m,wind_speed_10m,wind_direction_10m,weather_code
    // &temperature_unit=fahrenheit&wind_speed_unit=mph&timezone=GMT
    private const string ArchiveSample = """
        {"latitude":40.03515,"longitude":-75.020966,"generationtime_ms":0.1361370086669922,"utc_offset_seconds":0,"timezone":"GMT","timezone_abbreviation":"GMT","elevation":13.0,"hourly_units":{"time":"iso8601","temperature_2m":"°F","wind_speed_10m":"mp/h","wind_direction_10m":"°","weather_code":"wmo code"},"hourly":{"time":["2025-06-01T00:00","2025-06-01T01:00","2025-06-01T02:00","2025-06-01T03:00","2025-06-01T04:00","2025-06-01T05:00","2025-06-01T06:00","2025-06-01T07:00","2025-06-01T08:00","2025-06-01T09:00","2025-06-01T10:00","2025-06-01T11:00","2025-06-01T12:00","2025-06-01T13:00","2025-06-01T14:00","2025-06-01T15:00","2025-06-01T16:00","2025-06-01T17:00","2025-06-01T18:00","2025-06-01T19:00","2025-06-01T20:00","2025-06-01T21:00","2025-06-01T22:00","2025-06-01T23:00"],"temperature_2m":[57.7,57.9,56.6,55.9,53.2,52.3,51.4,50.8,50.3,50.0,50.0,51.4,53.8,57.7,60.2,61.9,64.0,65.5,66.0,66.7,65.8,66.1,65.2,63.8],"wind_speed_10m":[6.6,8.5,10.8,11.8,10.2,7.9,7.6,7.5,7.2,7.5,7.7,6.9,8.0,9.2,11.3,11.9,13.5,13.3,12.4,12.4,11.8,10.8,7.1,5.6],"wind_direction_10m":[298,293,295,309,304,295,279,271,259,255,256,253,258,257,265,274,280,279,274,270,265,274,261,236],"weather_code":[51,3,3,3,3,1,0,0,0,0,0,0,0,0,0,0,2,2,3,3,3,3,3,3]}}
        """;

    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("2025-06-01T14:20:00Z", "Clear sky", 60.2, 11.3, 265)]
    [InlineData("2025-06-01T14:40:00Z", "Clear sky", 61.9, 11.9, 274)]
    [InlineData("2025-06-01T00:10:00Z", "Light drizzle", 57.7, 6.6, 298)]
    [InlineData("2025-06-01T18:00:00Z", "Overcast", 66.0, 12.4, 274)]
    [InlineData("2025-06-01T16:00:00Z", "Partly cloudy", 64.0, 13.5, 280)]
    public void Parses_the_hour_nearest_the_requested_time(string at, string conditions, decimal temperature, decimal wind, int direction)
    {
        var observation = OpenMeteoWeatherProvider.Parse(ArchiveSample, DateTimeOffset.Parse(at));

        Assert.Equal(new WeatherObservation(conditions, temperature, wind, direction), observation);
    }

    [Fact]
    public void Returns_null_when_the_hour_is_missing_or_empty()
    {
        Assert.Null(OpenMeteoWeatherProvider.Parse(ArchiveSample, new DateTimeOffset(2025, 6, 2, 6, 0, 0, TimeSpan.Zero)));
        const string empty = """{"hourly":{"time":["2025-06-01T00:00"],"temperature_2m":[null],"wind_speed_10m":[null],"wind_direction_10m":[null],"weather_code":[null]}}""";
        Assert.Null(OpenMeteoWeatherProvider.Parse(empty, new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.Null(OpenMeteoWeatherProvider.Parse("{}", Now));
    }

    [Theory]
    [InlineData(0, "Clear sky")]
    [InlineData(45, "Fog")]
    [InlineData(63, "Rain")]
    [InlineData(75, "Heavy snow")]
    [InlineData(81, "Rain showers")]
    [InlineData(95, "Thunderstorm")]
    [InlineData(99, "Thunderstorm with hail")]
    [InlineData(42, "Unknown")]
    public void Describes_weather_codes(int code, string expected)
    {
        Assert.Equal(expected, OpenMeteoWeatherProvider.DescribeWeatherCode(code));
    }

    [Fact]
    public async Task Recent_times_use_the_forecast_api()
    {
        var handler = new RecordingHandler(ArchiveSample);
        var provider = CreateProvider(handler, apiKey: null);

        await provider.GetAsync(40.0, -75.0, Now.AddDays(-4), Ct);

        var uri = Assert.Single(handler.Requests);
        Assert.Equal("https://api.open-meteo.com/v1/forecast", uri.GetLeftPart(UriPartial.Path));
        Assert.Contains("latitude=40", uri.Query, StringComparison.Ordinal);
        Assert.Contains("longitude=-75", uri.Query, StringComparison.Ordinal);
        Assert.Contains("start_date=2026-05-28", uri.Query, StringComparison.Ordinal);
        Assert.Contains("temperature_unit=fahrenheit", uri.Query, StringComparison.Ordinal);
        Assert.Contains("wind_speed_unit=mph", uri.Query, StringComparison.Ordinal);
        Assert.Contains("timezone=GMT", uri.Query, StringComparison.Ordinal);
        Assert.Contains("hourly=temperature_2m,wind_speed_10m,wind_direction_10m,weather_code", Uri.UnescapeDataString(uri.Query), StringComparison.Ordinal);
        Assert.DoesNotContain("apikey", uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Older_times_use_the_archive_api_and_parse_the_response()
    {
        var handler = new RecordingHandler(ArchiveSample);
        var provider = CreateProvider(handler, apiKey: "secret-key");

        var observation = await provider.GetAsync(40.0, -75.0, new DateTimeOffset(2025, 6, 1, 14, 0, 0, TimeSpan.Zero), Ct);

        var uri = Assert.Single(handler.Requests);
        Assert.Equal("https://archive-api.open-meteo.com/v1/archive", uri.GetLeftPart(UriPartial.Path));
        Assert.Contains("start_date=2025-06-01", uri.Query, StringComparison.Ordinal);
        Assert.Contains("apikey=secret-key", uri.Query, StringComparison.Ordinal);
        Assert.Equal(new WeatherObservation("Clear sky", 60.2m, 11.3m, 265), observation);
    }

    [Fact]
    public async Task Base_urls_come_from_configuration()
    {
        var handler = new RecordingHandler(ArchiveSample);
        var options = new OpenMeteoOptions
        {
            ForecastUrl = new Uri("https://customer-api.example.test/v1/forecast"),
            ArchiveUrl = new Uri("https://customer-archive.example.test/v1/archive"),
        };
        var provider = new OpenMeteoWeatherProvider(new HttpClient(handler), Options.Create(options), new FakeTimeProvider(Now));

        await provider.GetAsync(1, 2, Now, Ct);
        await provider.GetAsync(1, 2, Now.AddDays(-30), Ct);

        Assert.Equal(
            ["https://customer-api.example.test/v1/forecast", "https://customer-archive.example.test/v1/archive"],
            handler.Requests.Select(u => u.GetLeftPart(UriPartial.Path)));
    }

    [Fact]
    public async Task Http_errors_throw()
    {
        var provider = CreateProvider(new RecordingHandler("{}", HttpStatusCode.InternalServerError), apiKey: null);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetAsync(1, 2, Now, Ct));
    }

    private static OpenMeteoWeatherProvider CreateProvider(RecordingHandler handler, string? apiKey) =>
        new(new HttpClient(handler), Options.Create(new OpenMeteoOptions { ApiKey = apiKey }), new FakeTimeProvider(Now));

    private sealed class RecordingHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
