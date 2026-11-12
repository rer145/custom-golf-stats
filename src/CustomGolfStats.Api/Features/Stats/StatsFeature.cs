namespace CustomGolfStats.Api.Features.Stats;

public static class StatsFeature
{
    public static IServiceCollection AddStatsFeature(this IServiceCollection services)
    {
        return services;
    }

    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/stats").WithTags("Stats").RequireAuthorization();
        _ = group;
        return app;
    }
}
