using Microsoft.AspNetCore.Http.HttpResults;

namespace CustomGolfStats.Api.Features.Rounds;

/// <summary><c>/weather</c>: proxies the weather service so its key stays off the device.</summary>
internal static class WeatherEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", GetAsync).WithName("WeatherGet");
    }

    /// <summary>Weather at (<paramref name="lat"/>, <paramref name="lon"/>) at <paramref name="at"/>. 404 when the service has no data.</summary>
    private static async Task<Results<Ok<WeatherDto>, NotFound, ValidationProblem, ProblemHttpResult>> GetAsync(
        double? lat, double? lon, DateTimeOffset? at, IWeatherProvider provider, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (lat is null)
            errors.Add("lat", RoundRequestValidation.Required);
        else if (lat is < -90 or > 90 || double.IsNaN(lat.Value))
            errors.Add("lat", RoundRequestValidation.OutOfRange);
        if (lon is null)
            errors.Add("lon", RoundRequestValidation.Required);
        else if (lon is < -180 or > 180 || double.IsNaN(lon.Value))
            errors.Add("lon", RoundRequestValidation.OutOfRange);
        if (at is null)
            errors.Add("at", RoundRequestValidation.Required);
        if (errors.Any)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        WeatherObservation? observation;
        try
        {
            observation = await provider.GetAsync(lat!.Value, lon!.Value, at!.Value.ToUniversalTime(), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return TypedResults.Problem("The weather service is unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }

        return observation is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new WeatherDto(observation.Conditions, observation.TemperatureF, observation.WindSpeedMph, observation.WindDirectionDegrees));
    }
}
