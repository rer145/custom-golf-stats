using System.Collections.Concurrent;
using CustomGolfStats.Api.Features.Rounds;

namespace CustomGolfStats.Api.Tests.Infrastructure.Fakes;

/// <summary>Returns <see cref="Observation"/> (null by default), or throws when <see cref="Fail"/> is set. Records every call.</summary>
public sealed class FakeWeatherProvider : IWeatherProvider
{
    public WeatherObservation? Observation { get; set; }

    public bool Fail { get; set; }

    public ConcurrentQueue<(double Lat, double Lon, DateTimeOffset At)> Calls { get; } = new();

    public Task<WeatherObservation?> GetAsync(double lat, double lon, DateTimeOffset at, CancellationToken ct)
    {
        Calls.Enqueue((lat, lon, at));
        if (Fail)
        {
            throw new HttpRequestException("Fake weather provider failure.");
        }

        return Task.FromResult(Observation);
    }
}
