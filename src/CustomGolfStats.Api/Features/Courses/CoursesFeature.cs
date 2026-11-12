namespace CustomGolfStats.Api.Features.Courses;

public static class CoursesFeature
{
    public static IServiceCollection AddCoursesFeature(this IServiceCollection services)
    {
        // Task 4 registers the real ICourseProvider (typed HttpClient) here.
        services.AddScoped<TeeVersionService>();
        return services;
    }

    public static IEndpointRouteBuilder MapCoursesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/courses").WithTags("Courses").RequireAuthorization();
        _ = group;
        return app;
    }
}
