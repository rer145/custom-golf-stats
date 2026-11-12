using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Features.Rounds;

/// <summary>
/// Fills a saved round's missing weather from <see cref="IWeatherProvider"/>, using its start time and its course's
/// coordinates. Never fails the caller: provider and save failures are logged and ignored.
/// </summary>
public sealed partial class RoundWeatherFiller(AppDbContext db, IWeatherProvider provider, TimeProvider time, ILogger<RoundWeatherFiller> logger)
{
    private const int ConditionsMaxLength = 100;

    public static bool HasWeather(Round round) =>
        round.WeatherConditions is not null || round.TemperatureF is not null
        || round.WindSpeedMph is not null || round.WindDirectionDegrees is not null;

    public async Task FillIfMissingAsync(Guid roundId, CancellationToken ct)
    {
        var round = await db.Rounds.SingleOrDefaultAsync(r => r.Id == roundId, ct);
        if (round is null || round.DeletedAt is not null || round.StartedAt is not { } startedAt || HasWeather(round))
        {
            return;
        }

        var location = await db.TeeVersions
            .Where(v => v.Id == round.TeeVersionId)
            .Select(v => new { v.Tee.Course.Latitude, v.Tee.Course.Longitude })
            .SingleAsync(ct);
        if (location is not { Latitude: { } lat, Longitude: { } lon })
        {
            return;
        }

        WeatherObservation? observation;
        try
        {
            observation = await provider.GetAsync(lat, lon, startedAt, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            LogProviderFailed(ex, roundId);
            return;
        }

        if (observation is null)
        {
            return;
        }

        var conditions = observation.Conditions;
        round.WeatherConditions = conditions.Length > ConditionsMaxLength ? conditions[..ConditionsMaxLength] : conditions;
        round.TemperatureF = Math.Round(observation.TemperatureF, 1);
        round.WindSpeedMph = Math.Round(observation.WindSpeedMph, 1);
        round.WindDirectionDegrees = observation.WindDirectionDegrees;
        round.ModifiedAt = time.GetUtcNow();
        round.Version++;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Usually a newer push won the race; the weather is filled on the next push instead.
            LogSaveFailed(ex, roundId);
            db.ChangeTracker.Clear();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Weather lookup failed for round {RoundId}; the round was saved without weather.")]
    private partial void LogProviderFailed(Exception exception, Guid roundId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Saving weather for round {RoundId} failed.")]
    private partial void LogSaveFailed(Exception exception, Guid roundId);
}
