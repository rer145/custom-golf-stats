using CustomGolfStats.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CustomGolfStats.Api.Tests.Auth;

/// <summary>Auth:BearerTokenExpiration is a Development-only knob for exercising client refresh by hand.</summary>
public sealed class BearerLifetimeSettingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private TimeSpan ExpirationIn(string environment, string? setting)
    {
        var app = factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            if (setting is not null)
                b.UseSetting("Auth:BearerTokenExpiration", setting);
        });
        return app.Services.GetRequiredService<IOptionsMonitor<BearerTokenOptions>>().Get(IdentityConstants.BearerScheme).BearerTokenExpiration;
    }

    [Fact]
    public void Development_can_shorten_the_access_token_lifetime() =>
        Assert.Equal(TimeSpan.FromSeconds(30), ExpirationIn("Development", "00:00:30"));

    [Fact]
    public void Other_environments_ignore_the_setting() =>
        Assert.Equal(TimeSpan.FromHours(1), ExpirationIn("Testing", "00:00:30"));

    [Fact]
    public void Unset_keeps_the_default_hour() =>
        Assert.Equal(TimeSpan.FromHours(1), ExpirationIn("Development", null));
}
