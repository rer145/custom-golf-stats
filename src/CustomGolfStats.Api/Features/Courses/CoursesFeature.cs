namespace CustomGolfStats.Api.Features.Courses;

public static class CoursesFeature
{
    public static IServiceCollection AddCoursesFeature(this IServiceCollection services)
    {
        services.AddHttpClient<ICourseProvider, GolfCourseApiProvider>((provider, client) =>
            GolfCourseApiProvider.Configure(client, provider.GetRequiredService<IConfiguration>()[GolfCourseApiProvider.ApiKeySetting]));
        services.AddScoped<TeeVersionService>();
        services.AddScoped<TeeVersionEditor>();
        return services;
    }

    public static IEndpointRouteBuilder MapCoursesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/courses").WithTags("Courses").RequireAuthorization().MapCourseRoutes();
        return app;
    }
}
