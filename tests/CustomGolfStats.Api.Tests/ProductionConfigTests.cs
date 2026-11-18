using System.Net;
using CustomGolfStats.Api.Infrastructure;
using CustomGolfStats.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CustomGolfStats.Api.Tests;

/// <summary>Settings the deployed API depends on behind Caddy (deploy/compose.yaml).</summary>
public sealed class ProductionConfigTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public void Data_protection_uses_a_fixed_application_name()
    {
        // Without it the discriminator is the content root path, so a new image path would invalidate every token.
        var options = factory.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value;

        Assert.Equal("CustomGolfStats", options.ApplicationDiscriminator);
    }

    [Fact]
    public async Task Forwarded_headers_are_applied_from_the_proxy_network()
    {
        var context = await SendThroughProxyAsync(remoteIp: "172.30.0.5", knownNetworks: "172.30.0.0/24");

        Assert.Equal("https", context.Request.Scheme);
        Assert.Equal(IPAddress.Parse("203.0.113.9"), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task Forwarded_headers_are_ignored_from_outside_the_proxy_network()
    {
        var context = await SendThroughProxyAsync(remoteIp: "198.51.100.7", knownNetworks: "172.30.0.0/24");

        Assert.Equal("http", context.Request.Scheme);
        Assert.Equal(IPAddress.Parse("198.51.100.7"), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task Forwarded_headers_are_ignored_when_no_proxy_network_is_configured()
    {
        var context = await SendThroughProxyAsync(remoteIp: "172.30.0.5", knownNetworks: null);

        Assert.Equal("http", context.Request.Scheme);
        Assert.Equal(IPAddress.Parse("172.30.0.5"), context.Connection.RemoteIpAddress);
    }

    [Theory]
    [InlineData("com.example.app", new[] { "com.example.app" })]
    [InlineData(" a.example , b.example ,", new[] { "a.example", "b.example" })]
    [InlineData("", new string[0])]
    public void Comma_separated_lists_bind_as_arrays(string value, string[] expected)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Apple:ClientIds"] = value });

        configuration.ExpandCommaSeparatedList("Auth:Apple:ClientIds");

        Assert.Equal(expected, configuration.GetSection("Auth:Apple:ClientIds").Get<string[]>() ?? []);
    }

    [Fact]
    public void Indexed_lists_are_left_alone()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Google:ClientIds:0"] = "one",
            ["Auth:Google:ClientIds:1"] = "two",
        });

        configuration.ExpandCommaSeparatedList("Auth:Google:ClientIds");

        Assert.Equal(["one", "two"], configuration.GetSection("Auth:Google:ClientIds").Get<string[]>() ?? []);
    }

    private async Task<HttpContext> SendThroughProxyAsync(string remoteIp, string? knownNetworks)
    {
        using var api = factory.WithWebHostBuilder(b =>
        {
            if (knownNetworks is not null)
            {
                b.UseSetting(ReverseProxy.KnownNetworksSetting, knownNetworks);
            }
        });

        return await api.Server.SendAsync(c =>
        {
            c.Request.Method = HttpMethods.Get;
            c.Request.Path = "/health";
            c.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
            c.Request.Headers["X-Forwarded-For"] = "203.0.113.9";
            c.Request.Headers["X-Forwarded-Proto"] = "https";
        }, TestContext.Current.CancellationToken);
    }
}
