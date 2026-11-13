using System.Net;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Features.Courses;
using CustomGolfStats.Api.Infrastructure;
using CustomGolfStats.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CustomGolfStats.Api.Tests;

/// <summary>Proves the shared test harness works, so feature tests can rely on it.</summary>
public sealed class HarnessTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task RegisterAsync_returns_a_signed_in_client()
    {
        var client = await TestAuth.RegisterAsync(factory, "golfer@example.com");

        var response = await client.GetAsync("/auth/manage/info", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(Guid.Empty, await TestAuth.GetAccountIdAsync(factory, "golfer@example.com"));
    }

    [Fact]
    public async Task Anonymous_client_is_rejected_by_an_authorized_endpoint()
    {
        var response = await factory.CreateClient().GetAsync("/auth/manage/info", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task New_accounts_are_users_and_MakeAdminAsync_grants_the_admin_policy()
    {
        var client = await TestAuth.RegisterAsync(factory, "boss@example.com");
        Assert.False(await SatisfiesAdminPolicyAsync("boss@example.com"));

        client = await TestAuth.MakeAdminAsync(factory, client, "boss@example.com");

        Assert.True(await SatisfiesAdminPolicyAsync("boss@example.com"));
        var response = await client.GetAsync("/auth/manage/info", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Account_created_at_comes_from_the_fake_clock()
    {
        await TestAuth.RegisterAsync(factory, "clock@example.com");

        var createdAt = await factory.WithDbAsync(db =>
            db.Users.Where(a => a.Email == "clock@example.com").Select(a => a.CreatedAt).SingleAsync());

        Assert.Equal(ApiFactory.StartTime, createdAt);
    }

    [Fact]
    public void External_services_are_replaced_by_the_fakes()
    {
        Assert.Same(factory.CourseProvider, factory.Services.GetRequiredService<ICourseProvider>());
        Assert.Same(factory.Time, factory.Services.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void WithTestServices_overrides_a_service()
    {
        var other = new Infrastructure.Fakes.FakeCourseProvider();

        using var variant = factory.WithTestServices(s => s.AddSingleton<ICourseProvider>(other));

        Assert.Same(other, variant.Services.GetRequiredService<ICourseProvider>());
    }

    [Fact]
    public async Task SeedCourseAsync_creates_a_par_72_course_with_18_holes()
    {
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, name: "Seeded"));

        var holes = await factory.WithDbAsync(db =>
            db.TeeHoles.Where(h => h.TeeVersionId == seeded.Version.Id).OrderBy(h => h.Number).ToListAsync());
        Assert.Equal(18, holes.Count);
        Assert.Equal(72, holes.Sum(h => h.Par));
        Assert.Equal(Enumerable.Range(1, 18), holes.Select(h => h.StrokeIndex).Order());
        Assert.Equal(72, seeded.Version.Par);
    }

    private async Task<bool> SatisfiesAdminPolicyAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        var principals = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<Account>>();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var account = await users.FindByEmailAsync(email) ?? throw new InvalidOperationException("No account.");
        var principal = await principals.CreateAsync(account);
        return (await authorization.AuthorizeAsync(principal, Policies.Admin)).Succeeded;
    }
}
