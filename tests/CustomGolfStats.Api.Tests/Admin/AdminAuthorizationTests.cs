using System.Net;
using System.Text;
using CustomGolfStats.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace CustomGolfStats.Api.Tests.Admin;

/// <summary>Every route under <c>/admin</c>, read from the app's route table so new admin routes are covered automatically.</summary>
public sealed class AdminAuthorizationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_user_gets_403_and_an_anonymous_caller_401_on_every_admin_route()
    {
        var user = await TestAuth.RegisterAsync(factory, "plain-user@example.com");
        var anonymous = factory.CreateClient();
        var routes = AdminRoutes();

        // Users, courses (list, details, version, verify, merge), flags (list, resolve, dismiss), settings (get, put).
        Assert.True(routes.Count >= 12, $"Expected at least 12 admin routes, found {routes.Count}.");
        foreach (var (method, url) in routes)
        {
            Assert.True(
                (await SendAsync(user, method, url)) == HttpStatusCode.Forbidden,
                $"{method} {url} should be 403 for a User.");
            Assert.True(
                (await SendAsync(anonymous, method, url)) == HttpStatusCode.Unauthorized,
                $"{method} {url} should be 401 when anonymous.");
        }
    }

    [Fact]
    public async Task An_admin_is_not_rejected_by_any_admin_route()
    {
        var admin = await TestAuth.RegisterAdminAsync(factory, "route-admin@example.com");

        foreach (var (method, url) in AdminRoutes())
        {
            var status = await SendAsync(admin, method, url);
            Assert.True(status is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized), $"{method} {url} returned {status} for an Admin.");
        }
    }

    private List<(string Method, string Url)> AdminRoutes()
    {
        _ = factory.Server; // builds the app, so the route table is populated
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        var routes = new List<(string, string)>();
        foreach (var endpoint in endpoints)
        {
            var url = BuildUrl(endpoint.RoutePattern);
            if (!url.StartsWith("/admin", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var method in endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [HttpMethods.Get])
            {
                routes.Add((method, url));
            }
        }

        return routes;
    }

    private static string BuildUrl(RoutePattern pattern)
    {
        var url = new StringBuilder();
        foreach (var segment in pattern.PathSegments)
        {
            url.Append('/');
            foreach (var part in segment.Parts)
            {
                url.Append(part switch
                {
                    RoutePatternLiteralPart literal => literal.Content,
                    RoutePatternSeparatorPart separator => separator.Content,
                    RoutePatternParameterPart parameter => SampleValue(parameter),
                    _ => "",
                });
            }
        }

        return url.Length == 0 ? "/" : url.ToString();
    }

    private static string SampleValue(RoutePatternParameterPart parameter) =>
        parameter.ParameterPolicies.Any(p => p.Content is "int" or "long") ? "1" : Guid.NewGuid().ToString();

    private static async Task<HttpStatusCode> SendAsync(HttpClient client, string method, string url)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method is "POST" or "PUT" or "PATCH")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request, Ct);
        return response.StatusCode;
    }
}
