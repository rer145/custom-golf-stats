using System.Net.Http.Headers;
using System.Net.Http.Json;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CustomGolfStats.Api.Tests.Infrastructure;

/// <summary>Registers and signs in golfers through the real <c>/auth</c> endpoints.</summary>
public static class TestAuth
{
    public const string Password = "Test-Passw0rd!";

    /// <summary>Registers <paramref name="email"/>, logs in and returns a new client carrying the bearer token.</summary>
    public static Task<HttpClient> RegisterAsync(WebApplicationFactory<Program> factory, string email) =>
        RegisterAsync(factory.CreateClient(), email);

    /// <summary>Registers <paramref name="email"/>, logs in and sets the bearer token on <paramref name="client"/>, which it returns.</summary>
    public static async Task<HttpClient> RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/auth/register", new { email, password = Password });
        await EnsureSuccessAsync(response, "register");
        return await LoginAsync(client, email);
    }

    /// <summary>Logs in and sets the bearer token on <paramref name="client"/>, which it returns.</summary>
    public static async Task<HttpClient> LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/auth/login", new { email, password = Password });
        await EnsureSuccessAsync(response, "login");
        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>()
            ?? throw new InvalidOperationException("Login returned no tokens.");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    /// <summary>
    /// Gives an existing account the Admin role, then logs <paramref name="client"/> in again so its token carries the
    /// role claim (tokens are issued with the role at login time).
    /// </summary>
    public static async Task<HttpClient> MakeAdminAsync(WebApplicationFactory<Program> factory, HttpClient client, string email)
    {
        await SetAsync(factory, email, account => account.Role = Role.Admin);
        return await LoginAsync(client, email);
    }

    /// <summary>Registers a new account, promotes it to Admin and returns a client signed in as it.</summary>
    public static async Task<HttpClient> RegisterAdminAsync(WebApplicationFactory<Program> factory, string email)
    {
        var client = await RegisterAsync(factory, email);
        return await MakeAdminAsync(factory, client, email);
    }

    public static async Task<Guid> GetAccountIdAsync(WebApplicationFactory<Program> factory, string email) =>
        await WithAccountAsync(factory, email, account => account.Id);

    /// <summary>Changes an account directly in the database (for example tier or trial count).</summary>
    public static Task SetAsync(WebApplicationFactory<Program> factory, string email, Action<Account> change) =>
        WithAccountAsync(factory, email, account =>
        {
            change(account);
            return 0;
        });

    private static async Task<T> WithAccountAsync<T>(WebApplicationFactory<Program> factory, string email, Func<Account, T> use)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CustomGolfStats.Api.Data.AppDbContext>();
        var normalized = email.ToUpperInvariant();
        var account = await db.Users.SingleAsync(a => a.NormalizedEmail == normalized);
        var result = use(account);
        await db.SaveChangesAsync();
        return result;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string step)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"TestAuth {step} failed with {(int)response.StatusCode}: {body}");
        }
    }

    private sealed record TokenResponse(string AccessToken, string RefreshToken);
}
