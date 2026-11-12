using CustomGolfStats.Api.Client;
using CustomGolfStats.Api.Tests.Infrastructure;

namespace CustomGolfStats.Api.Tests.OpenApi;

public sealed class GeneratedClientTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Generated_client_registers_logs_in_and_reads_the_profile()
    {
        var ct = TestContext.Current.CancellationToken;
        var http = factory.CreateClient();
        var api = new CustomGolfStatsApiClient(http);
        const string email = "client-smoke@example.com";

        await api.AuthRegisterAsync(new RegisterRequest { Email = email, Password = TestAuth.Password }, ct);
        var tokens = await api.AuthLoginAsync(null, null, new LoginRequest { Email = email, Password = TestAuth.Password }, ct);
        http.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        var profile = await api.MeGetProfileAsync(ct);

        Assert.Equal(email, profile.Email);
    }

    [Fact]
    public async Task Generated_client_throws_ApiException_with_the_status_code_when_not_signed_in()
    {
        var api = new CustomGolfStatsApiClient(factory.CreateClient());

        var error = await Assert.ThrowsAsync<ApiException>(() => api.MeGetProfileAsync(TestContext.Current.CancellationToken));

        Assert.Equal(401, error.StatusCode);
    }
}
