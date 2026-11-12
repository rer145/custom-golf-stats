using System.Net;
using CustomGolfStats.Api.Infrastructure;
using CustomGolfStats.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CustomGolfStats.Api.Tests;

/// <summary>Every route requires authorization unless it is on the explicit anonymous allow-list below.</summary>
public sealed class RouteSecurityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly HashSet<string> Anonymous =
    [
        // Identity's bearer API: sign-up, sign-in and token refresh, plus the inert email and password-reset routes.
        "POST /auth/register",
        "POST /auth/login",
        "POST /auth/refresh",
        "GET /auth/confirmEmail",
        "POST /auth/resendConfirmationEmail",
        "POST /auth/forgotPassword",
        "POST /auth/resetPassword",
        "POST /auth/external",
        "GET /health",
        "GET /openapi/{documentName}.json",
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_route_outside_the_allow_list_requires_authorization()
    {
        var routes = Routes();
        Assert.NotEmpty(routes);

        var open = routes.Where(r => !OpenApiSecurity.RequiresAuthorization(r.Endpoint.Metadata)).Select(r => r.Key).Order().ToList();
        Assert.Equal(Anonymous.Order(), open);

        var anonymous = factory.CreateClient();
        foreach (var route in routes.Where(r => !Anonymous.Contains(r.Key)))
        {
            using var request = new HttpRequestMessage(new HttpMethod(route.Method), Concrete(route.Pattern));
            var response = await anonymous.SendAsync(request, Ct);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{route.Key} answered {(int)response.StatusCode} to an anonymous caller.");
        }
    }

    private List<(string Key, string Method, string Pattern, RouteEndpoint Endpoint)> Routes()
    {
        _ = factory.CreateClient(); // starts the host
        var sources = factory.Services.GetServices<EndpointDataSource>();
        return sources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(m => (Method: m, Pattern: "/" + e.RoutePattern.RawText!.TrimStart('/'), Endpoint: e)))
            .Select(r => ($"{r.Method} {r.Pattern}", r.Method, r.Pattern, r.Endpoint))
            .ToList();
    }

    /// <summary>Fills route parameters with values their constraints accept.</summary>
    private static string Concrete(string pattern) =>
        System.Text.RegularExpressions.Regex.Replace(pattern, @"\{(\w+)(:(\w+))?\}", m => m.Groups[3].Value switch
        {
            "int" => "2026",
            _ => Guid.NewGuid().ToString(),
        });
}
