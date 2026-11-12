namespace CustomGolfStats.Api.Features.Rounds;

public static class RoundsFeature
{
    public static IServiceCollection AddRoundsFeature(this IServiceCollection services)
    {
        // Task 5 registers the real IWeatherProvider here.
        return services;
    }

    public static IEndpointRouteBuilder MapRoundsEndpoints(this IEndpointRouteBuilder app)
    {
        // Task 5 also adds /sync and /weather groups here, each with RequireAuthorization().
        var group = app.MapGroup("/rounds").WithTags("Rounds").RequireAuthorization();
        _ = group;
        return app;
    }
}
