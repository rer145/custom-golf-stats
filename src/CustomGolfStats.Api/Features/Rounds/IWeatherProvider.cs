namespace CustomGolfStats.Api.Features.Rounds;

/// <summary>Weather at a place and time, in the units rounds store.</summary>
public sealed record WeatherObservation(string Conditions, decimal TemperatureF, decimal WindSpeedMph, int WindDirectionDegrees);

/// <summary>The weather service, called only by the API. Faked in tests.</summary>
public interface IWeatherProvider
{
    /// <returns>The observation, or null when the service has no data for that place and time.</returns>
    Task<WeatherObservation?> GetAsync(double lat, double lon, DateTimeOffset at, CancellationToken ct);
}
