namespace CustomGolfStats.Api.Features.Rounds;

public static class RoundsFeature
{
    public static IServiceCollection AddRoundsFeature(this IServiceCollection services)
    {
        services.AddOptions<SyncOptions>().BindConfiguration(SyncOptions.SectionName)
            .Validate(o => o.CursorMargin >= TimeSpan.Zero, "Sync:CursorMargin must not be negative.");
        services.AddOptions<OpenMeteoOptions>().BindConfiguration(OpenMeteoOptions.SectionName);
        services.AddHttpClient<IWeatherProvider, OpenMeteoWeatherProvider>(client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<RoundWeatherFiller>();
        services.AddScoped<RoundUpsertService>();
        return services;
    }

    public static IEndpointRouteBuilder MapRoundsEndpoints(this IEndpointRouteBuilder app)
    {
        RoundsEndpoints.Map(app.MapGroup("/rounds").WithTags("Rounds").RequireAuthorization());
        SyncEndpoints.Map(app.MapGroup("/sync").WithTags("Sync").RequireAuthorization());
        WeatherEndpoints.Map(app.MapGroup("/weather").WithTags("Weather").RequireAuthorization());
        return app;
    }
}
