using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Features.Auth;
using CustomGolfStats.Api.Features.Courses;
using CustomGolfStats.Api.Features.Rounds;
using CustomGolfStats.Api.Infrastructure;
using CustomGolfStats.Api.Tests.Infrastructure.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace CustomGolfStats.Api.Tests.Infrastructure;

/// <summary>
/// The API in memory, over its own fresh database (one per test class: use it as <c>IClassFixture&lt;ApiFactory&gt;</c>).
/// External services are replaced by the fakes exposed below, so tests can arrange and assert on them.
/// To swap more services, either subclass and override <see cref="ConfigureTestServices"/> (then use the subclass
/// as the class fixture), or call <see cref="WithTestServices"/> for a one-off variant that shares this database.
/// </summary>
public class ApiFactory(PostgresFixture postgres) : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>The fake clock's starting point.</summary>
    public static readonly DateTimeOffset StartTime = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    public string ConnectionString { get; private set; } = "";

    public FakeTimeProvider Time { get; } = new(StartTime);

    public FakeCourseProvider CourseProvider { get; } = new();

    public FakeWeatherProvider WeatherProvider { get; } = new();

    public FakeExternalTokenValidator TokenValidator { get; } = new();

    public async ValueTask InitializeAsync() => ConnectionString = await postgres.CreateDatabaseAsync();

    /// <summary>A variant of this API with extra service overrides; it shares this factory's database and fakes.</summary>
    public WebApplicationFactory<Program> WithTestServices(Action<IServiceCollection> configure) =>
        WithWebHostBuilder(builder => builder.ConfigureTestServices(configure));

    /// <summary>Runs <paramref name="action"/> with a scoped <see cref="AppDbContext"/> on this factory's database.</summary>
    public async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <inheritdoc cref="WithDbAsync(Func{AppDbContext, Task})"/>
    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Hook for subclasses: runs after the default fakes are registered, so it can replace anything.</summary>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(AppEnvironments.Testing);
        builder.UseSetting("ConnectionStrings:Postgres", ConnectionString);
        builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Time));
            services.Replace(ServiceDescriptor.Singleton<ICourseProvider>(CourseProvider));
            services.Replace(ServiceDescriptor.Singleton<IWeatherProvider>(WeatherProvider));
            services.Replace(ServiceDescriptor.Singleton<IExternalTokenValidator>(TokenValidator));
            ConfigureTestServices(services);
        });
    }
}
