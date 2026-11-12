using System.Net;
using CustomGolfStats.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace CustomGolfStats.Api.Tests;

public sealed class HealthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_returns_ok_with_database()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Health_reports_unhealthy_when_database_is_unreachable()
    {
        // Production skips the startup migration, so only the health check touches the unreachable database.
        var client = factory.WithWebHostBuilder(b => b
                .UseEnvironment("Production")
                .UseSetting("ConnectionStrings:Postgres", "Host=127.0.0.1;Port=1;Database=none;Username=x;Password=x;Timeout=1"))
            .CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_document_is_served_anonymously()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"openapi\"", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
