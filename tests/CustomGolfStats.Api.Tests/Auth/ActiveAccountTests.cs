using System.Net;
using System.Net.Http.Json;
using CustomGolfStats.Api.Tests.Infrastructure;

namespace CustomGolfStats.Api.Tests.Auth;

/// <summary>Every authorized request needs an account that exists and isn't disabled; access tokens issued earlier don't outlive
/// it. Failing the check is 401 (the session is over), never 403.</summary>
public sealed class ActiveAccountTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Disabling_an_account_refuses_its_existing_tokens_at_once_and_re_enabling_restores_them()
    {
        var admin = await TestAuth.RegisterAdminAsync(factory, "active-admin@example.com");
        var golfer = await TestAuth.RegisterAsync(factory, "active-golfer@example.com");
        var bystander = await TestAuth.RegisterAsync(factory, "active-bystander@example.com");
        var golferId = await TestAuth.GetAccountIdAsync(factory, "active-golfer@example.com");
        Assert.Equal(HttpStatusCode.OK, (await golfer.GetAsync("/me", Ct)).StatusCode); // cached as active

        var disable = await admin.PutAsJsonAsync($"/admin/users/{golferId}", new { disabled = true }, Ct);
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await golfer.GetAsync("/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await golfer.PostAsJsonAsync("/me/handicap", new { effectiveOn = "2026-06-01", index = 10.0 }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await golfer.GetAsync("/auth/manage/info", Ct)).StatusCode);
        // Other accounts are unaffected.
        Assert.Equal(HttpStatusCode.OK, (await bystander.GetAsync("/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/admin/settings", Ct)).StatusCode);

        await admin.PutAsJsonAsync($"/admin/users/{golferId}", new { disabled = false }, Ct);
        Assert.Equal(HttpStatusCode.OK, (await golfer.GetAsync("/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_disabled_admin_is_refused_by_the_admin_tools_with_401()
    {
        var admin = await TestAuth.RegisterAdminAsync(factory, "disabler@example.com");
        var other = await TestAuth.RegisterAdminAsync(factory, "disabled-admin2@example.com");
        var otherId = await TestAuth.GetAccountIdAsync(factory, "disabled-admin2@example.com");
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/admin/settings", Ct)).StatusCode);

        await admin.PutAsJsonAsync($"/admin/users/{otherId}", new { disabled = true }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/admin/settings", Ct)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_are_unaffected()
    {
        var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/me", Ct)).StatusCode);
        // A golfer without the Admin role still gets 403 (not 401) from the admin tools.
        var golfer = await TestAuth.RegisterAsync(factory, "not-admin@example.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await golfer.GetAsync("/admin/settings", Ct)).StatusCode);
    }
}
