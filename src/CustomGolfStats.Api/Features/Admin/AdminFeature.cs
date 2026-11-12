using CustomGolfStats.Api.Infrastructure;

namespace CustomGolfStats.Api.Features.Admin;

public static class AdminFeature
{
    public static IServiceCollection AddAdminFeature(this IServiceCollection services)
    {
        return services;
    }

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // Every admin route goes in this group, so the Admin policy covers them all.
        var group = app.MapGroup("/admin").WithTags("Admin").RequireAuthorization(Policies.Admin);
        _ = group;
        return app;
    }
}
