using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Features.Auth;
using CustomGolfStats.Api.Tests.Infrastructure;
using CustomGolfStats.Domain;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Tests.Auth;

public sealed class AuthEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Tokens(string TokenType, string AccessToken, int ExpiresIn, string RefreshToken);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Tokens> ReadTokensAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<Tokens>(Ct) ?? throw new InvalidOperationException("No tokens.");
    }

    [Fact]
    public async Task Register_login_and_refresh_round_trip()
    {
        var client = factory.CreateClient();
        var register = await client.PostAsJsonAsync("/auth/register", new { email = "round@example.com", password = TestAuth.Password }, Ct);
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var login = await ReadTokensAsync(await client.PostAsJsonAsync("/auth/login", new { email = "round@example.com", password = TestAuth.Password }, Ct));
        Assert.False(string.IsNullOrEmpty(login.AccessToken));
        Assert.False(string.IsNullOrEmpty(login.RefreshToken));

        var refreshed = await ReadTokensAsync(await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = login.RefreshToken }, Ct));
        Assert.False(string.IsNullOrEmpty(refreshed.AccessToken));

        var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await authed.GetAsync("/auth/manage/info", Ct)).StatusCode);
    }

    [Fact]
    public async Task Login_with_wrong_password_is_401()
    {
        await TestAuth.RegisterAsync(factory, "wrongpw@example.com");

        var response = await factory.CreateClient().PostAsJsonAsync("/auth/login", new { email = "wrongpw@example.com", password = "Not-The-Passw0rd!" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Disabled_account_cannot_log_in()
    {
        await TestAuth.RegisterAsync(factory, "disabled-login@example.com");
        await TestAuth.SetAsync(factory, "disabled-login@example.com", a => a.DisabledAt = factory.Time.GetUtcNow());

        var response = await factory.CreateClient().PostAsJsonAsync("/auth/login", new { email = "disabled-login@example.com", password = TestAuth.Password }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Disabled_account_cannot_refresh()
    {
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/auth/register", new { email = "disabled-refresh@example.com", password = TestAuth.Password }, Ct);
        var login = await ReadTokensAsync(await client.PostAsJsonAsync("/auth/login", new { email = "disabled-refresh@example.com", password = TestAuth.Password }, Ct));
        await TestAuth.SetAsync(factory, "disabled-refresh@example.com", a => a.DisabledAt = factory.Time.GetUtcNow());

        var response = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = login.RefreshToken }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task External_sign_in_creates_an_account_then_reuses_it()
    {
        factory.TokenValidator.Accept("google-new", new ExternalIdentity("google", "g-sub-1", "newbie@example.com", true));
        var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync("/auth/external", new { provider = "google", idToken = "google-new", firstName = "Nia", lastName = "Newbie" }, Ct);
        var firstTokens = await ReadTokensAsync(first);
        var second = await ReadTokensAsync(await client.PostAsJsonAsync("/auth/external", new { provider = "google", idToken = "google-new" }, Ct));

        Assert.False(string.IsNullOrEmpty(firstTokens.AccessToken));
        Assert.False(string.IsNullOrEmpty(second.RefreshToken));
        var accounts = await factory.WithDbAsync(db => db.Users.Where(a => a.Email == "newbie@example.com").ToListAsync(Ct));
        var account = Assert.Single(accounts);
        Assert.Equal("Nia", account.FirstName);
        Assert.Equal("Newbie", account.LastName);
        Assert.True(account.EmailConfirmed);
        Assert.Equal(Role.User, account.Role);

        var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firstTokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await authed.GetAsync("/auth/manage/info", Ct)).StatusCode);
    }

    [Fact]
    public async Task External_sign_in_links_a_confirmed_existing_account_instead_of_duplicating()
    {
        await TestAuth.RegisterAsync(factory, "existing@example.com");
        await TestAuth.SetAsync(factory, "existing@example.com", a => a.EmailConfirmed = true);
        var existingId = await TestAuth.GetAccountIdAsync(factory, "existing@example.com");
        factory.TokenValidator.Accept("apple-existing", new ExternalIdentity("apple", "a-sub-1", "Existing@Example.com", true));

        var tokens = await ReadTokensAsync(await factory.CreateClient().PostAsJsonAsync("/auth/external", new { provider = "apple", idToken = "apple-existing" }, Ct));

        var accounts = await factory.WithDbAsync(db => db.Users.Where(a => a.NormalizedEmail == "EXISTING@EXAMPLE.COM").ToListAsync(Ct));
        Assert.Equal(existingId, Assert.Single(accounts).Id);
        var login = Assert.Single(await factory.WithDbAsync(db => db.UserLogins.Where(l => l.UserId == existingId).ToListAsync(Ct)));
        Assert.Equal("apple", login.LoginProvider);
        Assert.Equal("a-sub-1", login.ProviderKey);

        var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var info = await authed.GetFromJsonAsync<System.Text.Json.JsonElement>("/auth/manage/info", Ct);
        Assert.Equal("existing@example.com", info.GetProperty("email").GetString());
    }

    [Fact]
    public async Task External_sign_in_does_not_link_an_unconfirmed_local_account_pre_hijack()
    {
        // The attacker registered the victim's address with a password (register has no email confirmation).
        await TestAuth.RegisterAsync(factory, "prehijack@example.com");
        factory.TokenValidator.Accept("google-victim", new ExternalIdentity("google", "g-sub-victim", "prehijack@example.com", true));

        var response = await factory.CreateClient().PostAsJsonAsync("/auth/external", new { provider = "google", idToken = "google-victim" }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await factory.WithDbAsync(db => db.UserLogins.Where(l => l.ProviderKey == "g-sub-victim").ToListAsync(Ct)));
    }

    [Fact]
    public async Task External_sign_in_does_not_link_an_unverified_external_email()
    {
        await TestAuth.RegisterAsync(factory, "victim@example.com");
        await TestAuth.SetAsync(factory, "victim@example.com", a => a.EmailConfirmed = true);
        factory.TokenValidator.Accept("google-unverified", new ExternalIdentity("google", "g-sub-evil", "victim@example.com", false));

        var response = await factory.CreateClient().PostAsJsonAsync("/auth/external", new { provider = "google", idToken = "google-unverified" }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await factory.WithDbAsync(db => db.UserLogins.Where(l => l.ProviderKey == "g-sub-evil").ToListAsync(Ct)));
    }

    [Fact]
    public async Task Unverified_external_email_is_never_stored_so_it_cannot_squat_an_address()
    {
        factory.TokenValidator.Accept("apple-squat", new ExternalIdentity("apple", "a-sub-squat", "squat@example.com", false));
        await ReadTokensAsync(await factory.CreateClient().PostAsJsonAsync("/auth/external", new { provider = "apple", idToken = "apple-squat" }, Ct));

        var account = await factory.WithDbAsync(db => db.UserLogins.Where(l => l.ProviderKey == "a-sub-squat")
            .Join(db.Users, l => l.UserId, u => u.Id, (_, u) => u).SingleAsync(Ct));
        Assert.Null(account.Email);
        Assert.False(account.EmailConfirmed);

        // The real owner later signs in with a verified token for the same address: a separate account, no absorption.
        factory.TokenValidator.Accept("google-owner", new ExternalIdentity("google", "g-sub-owner", "squat@example.com", true));
        await ReadTokensAsync(await factory.CreateClient().PostAsJsonAsync("/auth/external", new { provider = "google", idToken = "google-owner" }, Ct));
        var owner = await factory.WithDbAsync(db => db.Users.SingleAsync(u => u.Email == "squat@example.com", Ct));
        Assert.NotEqual(account.Id, owner.Id);
        Assert.True(owner.EmailConfirmed);
    }

    [Fact]
    public async Task External_sign_in_with_an_invalid_token_is_401()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/auth/external", new { provider = "google", idToken = "garbage" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task External_sign_in_validates_the_request()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/auth/external", new { provider = "facebook", idToken = "" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Ct);
        Assert.True(problem.GetProperty("errors").TryGetProperty("provider", out _));
        Assert.True(problem.GetProperty("errors").TryGetProperty("idToken", out _));
    }

    [Fact]
    public async Task Disabled_account_cannot_sign_in_externally()
    {
        factory.TokenValidator.Accept("google-disabled", new ExternalIdentity("google", "g-sub-dis", "disabled-ext@example.com", true));
        var client = factory.CreateClient();
        await ReadTokensAsync(await client.PostAsJsonAsync("/auth/external", new { provider = "google", idToken = "google-disabled" }, Ct));
        await TestAuth.SetAsync(factory, "disabled-ext@example.com", a => a.DisabledAt = factory.Time.GetUtcNow());

        var response = await client.PostAsJsonAsync("/auth/external", new { provider = "google", idToken = "google-disabled" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task External_token_without_email_creates_an_account_without_one()
    {
        factory.TokenValidator.Accept("apple-noemail", new ExternalIdentity("apple", "a-sub-noemail", null, false));
        var client = factory.CreateClient();

        await ReadTokensAsync(await client.PostAsJsonAsync("/auth/external", new { provider = "apple", idToken = "apple-noemail" }, Ct));
        await ReadTokensAsync(await client.PostAsJsonAsync("/auth/external", new { provider = "apple", idToken = "apple-noemail" }, Ct));

        var logins = await factory.WithDbAsync(db => db.UserLogins.Where(l => l.ProviderKey == "a-sub-noemail").ToListAsync(Ct));
        Assert.Single(logins);
    }
}
