using System.Net;
using CustomGolfStats.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Tests.Auth;

public sealed class DataProtectionKeyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_token_issued_by_one_instance_is_accepted_by_another_on_the_same_database()
    {
        var signedIn = await TestAuth.RegisterAsync(factory, "two-hosts@example.com");
        Assert.Equal(HttpStatusCode.OK, (await signedIn.GetAsync("/me", Ct)).StatusCode);

        // A second host (its own service provider and key ring) over the same database.
        await using var secondHost = factory.WithTestServices(_ => { });
        var other = secondHost.CreateClient();
        other.DefaultRequestHeaders.Authorization = signedIn.DefaultRequestHeaders.Authorization;

        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/me", Ct)).StatusCode);
        Assert.True(await factory.WithDbAsync(db => db.DataProtectionKeys.AnyAsync(Ct)));
    }
}
