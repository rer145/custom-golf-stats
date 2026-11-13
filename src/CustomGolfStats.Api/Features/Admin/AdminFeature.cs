using CustomGolfStats.Api.Infrastructure;

namespace CustomGolfStats.Api.Features.Admin;

public static class AdminFeature
{
    public static IServiceCollection AddAdminFeature(this IServiceCollection services)
    {
        services.AddScoped<CourseMerger>();
        return services;
    }

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // Every admin route goes in this group, so the Admin policy covers them all (tests enumerate the route table).
        var group = app.MapGroup("/admin").WithTags("Admin").RequireAuthorization(Policies.Admin);
        group.MapAdminUserRoutes();
        group.MapAdminCourseRoutes();
        group.MapAdminFlagRoutes();
        group.MapAdminSettingsRoutes();
        return app;
    }
}
