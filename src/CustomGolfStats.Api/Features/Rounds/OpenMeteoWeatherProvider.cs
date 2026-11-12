using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CustomGolfStats.Api.Features.Rounds;

/// <summary>
/// Configuration section <c>Weather:OpenMeteo</c>. The defaults are the free endpoints; a commercial plan sets its own
/// URLs (for example <c>https://customer-api.open-meteo.com/v1/forecast</c>) and <see cref="ApiKey"/>.
/// </summary>
public sealed class OpenMeteoOptions
{
    public const string SectionName = "Weather:OpenMeteo";

    public Uri ForecastUrl { get; set; } = new("https://api.open-meteo.com/v1/forecast");

    public Uri ArchiveUrl { get; set; } = new("https://archive-api.open-meteo.com/v1/archive");

    /// <summary>Sent as <c>apikey</c> when set. A secret: environment or user secrets only.</summary>
    public string? ApiKey { get; set; }
}

/// <summary>
/// <see cref="IWeatherProvider"/> over Open-Meteo: the forecast API for times within the last
/// <see cref="ForecastWindow"/> (or later), the historical archive before that. Reads the hour nearest the requested time.
/// </summary>
public sealed class OpenMeteoWeatherProvider(HttpClient http, IOptions<OpenMeteoOptions> options, TimeProvider time) : IWeatherProvider
{
    public static readonly TimeSpan ForecastWindow = TimeSpan.FromDays(5);

    private const string HourlyFields = "temperature_2m,wind_speed_10m,wind_direction_10m,weather_code";

    public async Task<WeatherObservation?> GetAsync(double lat, double lon, DateTimeOffset at, CancellationToken ct)
    {
        var settings = options.Value;
        var utc = at.ToUniversalTime();
        var endpoint = utc >= time.GetUtcNow() - ForecastWindow ? settings.ForecastUrl : settings.ArchiveUrl;
        // The nearest hour can be the first hour of the next day.
        var startDate = DateOnly.FromDateTime(utc.UtcDateTime);
        var endDate = DateOnly.FromDateTime(utc.AddHours(1).UtcDateTime);

        var query = new List<string>
        {
            $"latitude={lat.ToString("R", CultureInfo.InvariantCulture)}",
            $"longitude={lon.ToString("R", CultureInfo.InvariantCulture)}",
            $"start_date={startDate:yyyy-MM-dd}",
            $"end_date={endDate:yyyy-MM-dd}",
            $"hourly={HourlyFields}",
            "temperature_unit=fahrenheit",
            "wind_speed_unit=mph",
            "timezone=GMT",
        };
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            query.Add($"apikey={Uri.EscapeDataString(settings.ApiKey)}");
        }

        var uri = new UriBuilder(endpoint) { Query = string.Join('&', query) }.Uri;
        using var response = await http.GetAsync(uri, ct);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(ct), utc);
    }

    /// <summary>
    /// The observation for the hour nearest <paramref name="at"/> in an Open-Meteo hourly response (requested with
    /// <c>timezone=GMT</c>), or null when that hour is missing or has no values.
    /// </summary>
    public static WeatherObservation? Parse(string json, DateTimeOffset at)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("hourly", out var hourly)
            || !hourly.TryGetProperty("time", out var times) || times.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var utc = at.ToUniversalTime();
        var hourStart = new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
        var nearest = utc - hourStart >= TimeSpan.FromMinutes(30) ? hourStart.AddHours(1) : hourStart;
        var wanted = nearest.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

        var index = -1;
        var i = 0;
        foreach (var value in times.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String && value.GetString() == wanted)
            {
                index = i;
                break;
            }

            i++;
        }

        if (index < 0
            || ValueAt(hourly, "temperature_2m", index) is not { } temperature
            || ValueAt(hourly, "wind_speed_10m", index) is not { } windSpeed
            || ValueAt(hourly, "wind_direction_10m", index) is not { } windDirection
            || ValueAt(hourly, "weather_code", index) is not { } code)
        {
            return null;
        }

        return new WeatherObservation(
            DescribeWeatherCode((int)code),
            Math.Round(temperature, 1),
            Math.Round(windSpeed, 1),
            (int)Math.Round(windDirection, MidpointRounding.AwayFromZero) % 360);
    }

    /// <summary>A short description of a WMO weather interpretation code, as Open-Meteo returns them.</summary>
    public static string DescribeWeatherCode(int code) => code switch
    {
        0 => "Clear sky",
        1 => "Mainly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 => "Light drizzle",
        53 => "Drizzle",
        55 => "Heavy drizzle",
        56 or 57 => "Freezing drizzle",
        61 => "Light rain",
        63 => "Rain",
        65 => "Heavy rain",
        66 or 67 => "Freezing rain",
        71 => "Light snow",
        73 => "Snow",
        75 => "Heavy snow",
        77 => "Snow grains",
        80 => "Light rain showers",
        81 => "Rain showers",
        82 => "Heavy rain showers",
        85 or 86 => "Snow showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm with hail",
        _ => "Unknown",
    };

    private static decimal? ValueAt(JsonElement hourly, string field, int index)
    {
        if (!hourly.TryGetProperty(field, out var values) || values.ValueKind != JsonValueKind.Array || index >= values.GetArrayLength())
        {
            return null;
        }

        var value = values[index];
        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) ? number : null;
    }
}
