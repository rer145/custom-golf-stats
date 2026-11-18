using System.Net;
using System.Net.Http.Json;
using CustomGolfStats.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Tests.Me;

/// <summary>An admin deleting their own account. Its own class (so its own database): the "only active admin" rule counts every
/// admin in the database.</summary>
public sealed class AdminAccountDeletionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<HttpResponseMessage> DeleteAccountAsync(HttpClient client, object body) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/me") { Content = JsonContent.Create(body) }, Ct);

    [Fact]
    public async Task The_only_active_admin_cannot_delete_their_account_until_another_admin_exists()
    {
        var admin = await TestAuth.RegisterAdminAsync(factory, "sole-admin@example.com");
        var adminId = await TestAuth.GetAccountIdAsync(factory, "sole-admin@example.com");
        await TestAuth.RegisterAsync(factory, "disabled-admin@example.com");
        await TestAuth.SetAsync(factory, "disabled-admin@example.com", a =>
        {
            a.Role = Domain.Role.Admin;
            a.DisabledAt = ApiFactory.StartTime; // a disabled admin doesn't count
        });

        // Four wrong guesses, then the right password: refused as the only admin, and the guesses are forgotten.
        for (var i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await DeleteAccountAsync(admin, new { password = $"Guess-{i}" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await DeleteAccountAsync(admin, new { password = TestAuth.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await DeleteAccountAsync(admin, new { password = "Guess-again" })).StatusCode);
        Assert.True(await factory.WithDbAsync(db => db.Users.AnyAsync(a => a.Id == adminId, Ct)));

        await TestAuth.RegisterAdminAsync(factory, "second-admin@example.com");
        var deleted = await DeleteAccountAsync(admin, new { password = TestAuth.Password });

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        // The deleted admin's access token no longer opens the admin tools.
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/admin/settings", Ct)).StatusCode);
    }
}
