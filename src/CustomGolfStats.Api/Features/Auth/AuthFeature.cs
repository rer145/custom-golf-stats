using CustomGolfStats.Api.Data.Entities;

namespace CustomGolfStats.Api.Features.Auth;

public static class AuthFeature
{
    public static IServiceCollection AddAuthFeature(this IServiceCollection services)
    {
        // Task 2 registers the real IExternalTokenValidator here.
        return services;
    }

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Identity's register / login / refresh (and manage) endpoints, so the test harness can sign in from day one.
        // Task 2 owns this folder: disabled-account checks, external sign-in, trimming unused routes.
        app.MapGroup("/auth").WithTags("Auth").MapIdentityApi<Account>();
        return app;
    }
}
