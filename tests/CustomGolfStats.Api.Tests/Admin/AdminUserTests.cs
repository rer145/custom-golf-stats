using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CustomGolfStats.Api.Features.Admin;
using CustomGolfStats.Api.Tests.Infrastructure;
using CustomGolfStats.Domain;
using Microsoft.EntityFrameworkCore;
using static CustomGolfStats.Api.Tests.Courses.CourseTestHelpers;

namespace CustomGolfStats.Api.Tests.Admin;

public sealed class AdminUserTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Users_are_listed_and_filtered_by_query()
    {
        var admin = await TestAuth.RegisterAdminAsync(factory, "list-admin@example.com");
        await TestAuth.RegisterAsync(factory, "alice.list@example.com");
        await TestAuth.RegisterAsync(factory, "bob.list@example.com");
        await TestAuth.SetAsync(factory, "bob.list@example.com", a => (a.FirstName, a.LastName) = ("Robert", "Zeta"));

        var all = await (await admin.GetAsync("/admin/users", Ct)).ReadAsync<List<AdminUserDto>>(HttpStatusCode.OK);
        var byEmail = await (await admin.GetAsync("/admin/users?query=ALICE", Ct)).ReadAsync<List<AdminUserDto>>(HttpStatusCode.OK);
        var byName = await (await admin.GetAsync("/admin/users?query=zeta", Ct)).ReadAsync<List<AdminUserDto>>(HttpStatusCode.OK);

        Assert.Contains(all, u => u.Email == "list-admin@example.com" && u.Role == Role.Admin);
        Assert.Contains(all, u => u.Email == "alice.list@example.com" && u.Role == Role.User && u.Tier == Tier.Free);
        Assert.Equal(["alice.list@example.com"], byEmail.Select(u => u.Email));
        Assert.Equal(["bob.list@example.com"], byName.Select(u => u.Email));
        Assert.Equal("Robert", byName[0].FirstName);
    }

    [Fact]
    public async Task Tier_change_shows_in_the_golfers_entitlements_without_signing_in_again()
    {
        var admin = await TestAuth.RegisterAdminAsync(factory, "tier-admin@example.com");
        var golfer = await TestAuth.RegisterAsync(factory, "tier-golfer@example.com");
        var golferId = await TestAuth.GetAccountIdAsync(factory, "tier-golfer@example.com");
        Assert.Equal("Free", (await EntitlementsAsync(golfer)).GetProperty("tier").GetString());

        var updated = await (await admin.PutAsJsonAsync($"/admin/users/{golferId}", new { tier = "paid" }, Ct))
            .ReadAsync<AdminUserDto>(HttpStatusCode.OK);

        Assert.Equal(Tier.Paid, updated.Tier);
        Assert.Equal(Role.User, updated.Role);
        var entitlements = await EntitlementsAsync(golfer);
        Assert.Equal("Paid", entitlements.GetProperty("tier").GetString());
        Assert.Equal(JsonValueKind.Null, entitlements.GetProperty("trialShotRoundsRemaining").ValueKind);
    }

    [Fact]
    public async Task Role_change_applies_at_the_next_sign_in()
    {
        var admin = await TestAuth.RegisterAdminAsync(factory, "role-admin@example.com");
        var golfer = await TestAuth.RegisterAsync(factory, "role-golfer@example.com");
        var golferId = await TestAuth.GetAccountIdAsync(factory, "role-golfer@example.com");

        var updated = await (await admin.PutAsJsonAsync($"/admin/users/{golferId}", new { role = "Admin" }, Ct))
            .ReadAsync<AdminUserDto>(HttpStatusCode.OK);

        Assert.Equal(Role.Admin, updated.Role);
        Assert.Equal(HttpStatusCode.Forbidden, (await golfer.GetAsync("/admin/settings", Ct)).StatusCode);
        await TestAuth.LoginAsync(golfer, "role-golfer@example.com");
        Assert.Equal(HttpStatusCode.OK, (await golfer.GetAsync("/admin/settings", Ct)).StatusCode);

        // Another admin can be demoted.
        var demoted = await (await admin.PutAsJsonAsync($"/admin/users/{golferId}", new { role = "User" }, Ct))
            .ReadAsync<AdminUserDto>(HttpStatusCode.OK);
        Assert.Equal(Role.User, demoted.Role);
    }

    [Fact]
    public async Task An_admin_cannot_remove_their_own_admin_role_or_disable_themselves()
    {
        var admin = await TestAuth.RegisterAdminAsync(factory, "self-admin@example.com");
        var adminId = await TestAuth.GetAccountIdAsync(factory, "self-admin@example.com");

        var demote = await (await admin.PutAsJsonAsync($"/admin/users/{adminId}", new { role = "User" }, Ct)).ReadValidationErrorsAsync();
        var disable = await (await admin.PutAsJsonAsync($"/admin/users/{adminId}", new { disabled = true }, Ct)).ReadValidationErrorsAsync();

        Assert.Contains("role", demote.Keys);
        Assert.Contains("disabled", disable.Keys);
        var account = await factory.WithDbAsync(db => db.Users.SingleAsync(a => a.Id == adminId, Ct));
        Assert.Equal(Role.Admin, account.Role);
        Assert.Null(account.DisabledAt);

        // Changing their own tier is fine.
        var paid = await (await admin.PutAsJsonAsync($"/admin/users/{adminId}", new { tier = "Paid", role = "Admin" }, Ct))
            .ReadAsync<AdminUserDto>(HttpStatusCode.OK);
        Assert.Equal(Tier.Paid, paid.Tier);
    }

    [Fact]
    public async Task Disabling_blocks_sign_in_and_enabling_restores_it()
    {
        var admin = await TestAuth.RegisterAdminAsync(factory, "disable-admin@example.com");
        await TestAuth.RegisterAsync(factory, "disable-golfer@example.com");
        var golferId = await TestAuth.GetAccountIdAsync(factory, "disable-golfer@example.com");

        var disabled = await (await admin.PutAsJsonAsync($"/admin/users/{golferId}", new { disabled = true }, Ct))
            .ReadAsync<AdminUserDto>(HttpStatusCode.OK);
        var login = await factory.CreateClient().PostAsJsonAsync(
            "/auth/login", new { email = "disable-golfer@example.com", password = TestAuth.Password }, Ct);

        Assert.Equal(ApiFactory.StartTime, disabled.DisabledAt);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        // Disabling again keeps the original time.
        factory.Time.Advance(TimeSpan.FromMinutes(5));
        var again = await (await admin.PutAsJsonAsync($"/admin/users/{golferId}", new { disabled = true }, Ct))
            .ReadAsync<AdminUserDto>(HttpStatusCode.OK);
        Assert.Equal(ApiFactory.StartTime, again.DisabledAt);

        var enabled = await (await admin.PutAsJsonAsync($"/admin/users/{golferId}", new { disabled = false }, Ct))
            .ReadAsync<AdminUserDto>(HttpStatusCode.OK);
        Assert.Null(enabled.DisabledAt);
        await TestAuth.LoginAsync(factory.CreateClient(), "disable-golfer@example.com");
    }

    [Fact]
    public async Task Invalid_values_and_unknown_users_are_rejected()
    {
        var admin = await TestAuth.RegisterAdminAsync(factory, "invalid-admin@example.com");
        await TestAuth.RegisterAsync(factory, "invalid-golfer@example.com");
        var golferId = await TestAuth.GetAccountIdAsync(factory, "invalid-golfer@example.com");

        var errors = await (await admin.PutAsJsonAsync($"/admin/users/{golferId}", new { tier = "Gold", role = "1" }, Ct))
            .ReadValidationErrorsAsync();
        var missing = await admin.PutAsJsonAsync($"/admin/users/{Guid.NewGuid()}", new { tier = "Paid" }, Ct);

        Assert.Contains("tier", errors.Keys);
        Assert.Contains("role", errors.Keys);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static async Task<JsonElement> EntitlementsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/me/entitlements", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();
    }
}
