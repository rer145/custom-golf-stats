namespace CustomGolfStats.Api.Features.Me;

public static class MeFeature
{
    public static IServiceCollection AddMeFeature(this IServiceCollection services)
    {
        return services;
    }

    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/me").WithTags("Me").RequireAuthorization();
        _ = group;
        return app;
    }
}
