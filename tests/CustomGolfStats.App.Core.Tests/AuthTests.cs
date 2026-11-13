using System.Net;
using System.Text.Json;
using CustomGolfStats.Api.Client;
using CustomGolfStats.App.Core.ApiAccess;
using CustomGolfStats.App.Core.Tests.Support;

namespace CustomGolfStats.App.Core.Tests;

public class AuthTests
{
    private const string Access1 = "ACCESS-TOKEN-ONE";
    private const string Refresh1 = "REFRESH-TOKEN-ONE";
    private const string Access2 = "ACCESS-TOKEN-TWO";
    private const string Refresh2 = "REFRESH-TOKEN-TWO";

    private static (ApiSession Session, StubHandler Stub, FakeSecureStore Store) Build(Func<Recorded, HttpResponseMessage> respond)
    {
        var stub = new StubHandler(respond);
        var store = new FakeSecureStore();
        return (ApiClientFactory.Create(StubHandler.BaseAddress, store, stub), stub, store);
    }

    private static HttpResponseMessage Profile() => StubHandler.Json(HttpStatusCode.OK,
        new ProfileResponse { Id = Guid.NewGuid(), Email = "a@b.c", FirstName = "A", LastName = "B", Gender = "Male", Units = "Imperial" });

    [Fact]
    public async Task Sign_in_posts_credentials_and_keeps_the_tokens_only_in_the_secure_store()
    {
        var (session, stub, store) = Build(_ => StubHandler.Json(HttpStatusCode.OK, Payloads.Tokens(Access1, Refresh1)));

        await session.Tokens.SignInAsync("a@b.c", "pw", TestContext.Current.CancellationToken);

        var login = Assert.Single(stub.Requests);
        Assert.Equal("/auth/login", login.Path);
        Assert.Null(login.Bearer);
        using var body = JsonDocument.Parse(login.Body!);
        Assert.Equal("a@b.c", body.RootElement.GetProperty("email").GetString());
        Assert.Equal(Access1, store.Values[TokenService.AccessTokenKey]);
        Assert.Equal(Refresh1, store.Values[TokenService.RefreshTokenKey]);
        Assert.True(await session.Tokens.IsSignedInAsync());
    }

    [Fact]
    public async Task Register_creates_the_account_then_signs_in()
    {
        var (session, stub, store) = Build(r => r.Path == "/auth/register" ? StubHandler.Status(HttpStatusCode.OK) : StubHandler.Json(HttpStatusCode.OK, Payloads.Tokens(Access1, Refresh1)));

        await session.Tokens.RegisterAsync("new@b.c", "pw", TestContext.Current.CancellationToken);

        Assert.Equal(["/auth/register", "/auth/login"], stub.Requests.Select(r => r.Path));
        Assert.Equal(Access1, store.Values[TokenService.AccessTokenKey]);
    }

    [Fact]
    public async Task External_sign_in_sends_the_provider_and_id_token()
    {
        var (session, stub, store) = Build(_ => StubHandler.Json(HttpStatusCode.OK, Payloads.Tokens(Access1, Refresh1)));

        await session.Tokens.ExternalSignInAsync("Apple", "id-token-from-platform", "Ada", "Lovelace", TestContext.Current.CancellationToken);

        var req = Assert.Single(stub.Requests);
        Assert.Equal("/auth/external", req.Path);
        using var body = JsonDocument.Parse(req.Body!);
        Assert.Equal("Apple", body.RootElement.GetProperty("provider").GetString());
        Assert.Equal("id-token-from-platform", body.RootElement.GetProperty("idToken").GetString());
        Assert.Equal("Ada", body.RootElement.GetProperty("firstName").GetString());
        Assert.Equal(Refresh1, store.Values[TokenService.RefreshTokenKey]);
    }

    [Fact]
    public async Task A_failed_sign_in_stores_nothing()
    {
        var (session, _, store) = Build(_ => StubHandler.Status(HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<ApiException>(() => session.Tokens.SignInAsync("a@b.c", "bad", TestContext.Current.CancellationToken));

        Assert.Empty(store.Values);
        Assert.False(await session.Tokens.IsSignedInAsync());
    }

    [Fact]
    public async Task Requests_carry_the_bearer_but_auth_calls_do_not()
    {
        var (session, stub, store) = Build(r => r.Path == "/me" ? Profile() : StubHandler.Json(HttpStatusCode.OK, Payloads.Tokens(Access1, Refresh1)));
        await session.Tokens.SignInAsync("a@b.c", "pw", TestContext.Current.CancellationToken);

        await session.Client.MeGetProfileAsync(TestContext.Current.CancellationToken);

        Assert.Null(stub.Requests[0].Bearer);
        Assert.Equal(Access1, stub.Requests[1].Bearer);
        Assert.Equal(2, stub.Requests.Count);
        Assert.NotEmpty(store.Values);
    }

    [Fact]
    public async Task A_401_refreshes_once_and_retries_with_the_new_token_and_the_same_body()
    {
        var (session, stub, store) = Build(r => (r.Method.Method, r.Path, r.Bearer) switch
        {
            (_, "/auth/refresh", _) => StubHandler.Json(HttpStatusCode.OK, Payloads.Tokens(Access2, Refresh2)),
            ("PUT", _, Access1) => StubHandler.Status(HttpStatusCode.Unauthorized),
            ("PUT", _, Access2) => StubHandler.Json(HttpStatusCode.OK, Payloads.Round(Guid.NewGuid(), 1)),
            _ => StubHandler.Status(HttpStatusCode.NotFound),
        });
        store.Values[TokenService.AccessTokenKey] = Access1;
        store.Values[TokenService.RefreshTokenKey] = Refresh1;
        var id = Guid.NewGuid();
        var request = ApiMapperRequest();

        await session.Client.RoundsUpsertAsync(id, request, TestContext.Current.CancellationToken);

        Assert.Equal(["PUT", "POST", "PUT"], stub.Requests.Select(r => r.Method.Method));
        Assert.Equal(Access1, stub.Requests[0].Bearer);
        Assert.Equal(Access2, stub.Requests[2].Bearer);
        Assert.Equal(stub.Requests[0].Body, stub.Requests[2].Body);
        using var refreshBody = JsonDocument.Parse(stub.Requests[1].Body!);
        Assert.Equal(Refresh1, refreshBody.RootElement.GetProperty("refreshToken").GetString());
        Assert.Equal(Access2, store.Values[TokenService.AccessTokenKey]);
        Assert.Equal(Refresh2, store.Values[TokenService.RefreshTokenKey]);
    }

    [Fact]
    public async Task A_second_401_after_the_refresh_is_returned_and_not_retried_again()
    {
        var (session, stub, store) = Build(r => r.Path == "/auth/refresh" ? StubHandler.Json(HttpStatusCode.OK, Payloads.Tokens(Access2, Refresh2)) : StubHandler.Status(HttpStatusCode.Unauthorized));
        store.Values[TokenService.AccessTokenKey] = Access1;
        store.Values[TokenService.RefreshTokenKey] = Refresh1;

        var ex = await Assert.ThrowsAsync<ApiException>(() => session.Client.MeGetProfileAsync(TestContext.Current.CancellationToken));

        Assert.Equal(401, ex.StatusCode);
        Assert.Equal(3, stub.Requests.Count); // GET, refresh, GET: one refresh, one retry
    }

    [Fact]
    public async Task A_rejected_refresh_signs_the_golfer_out_and_surfaces_the_401()
    {
        var (session, stub, store) = Build(r => r.Path == "/auth/refresh" ? StubHandler.Status(HttpStatusCode.Unauthorized) : StubHandler.Status(HttpStatusCode.Unauthorized));
        store.Values[TokenService.AccessTokenKey] = Access1;
        store.Values[TokenService.RefreshTokenKey] = Refresh1;
        var expired = 0;
        session.Tokens.SessionExpired += () => expired++;

        var ex = await Assert.ThrowsAsync<ApiException>(() => session.Client.MeGetProfileAsync(TestContext.Current.CancellationToken));

        Assert.Equal(401, ex.StatusCode);
        Assert.Empty(store.Values);
        Assert.Equal(1, expired);
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task A_network_failure_during_refresh_keeps_the_tokens()
    {
        var (session, _, store) = Build(r => r.Path == "/auth/refresh" ? throw new HttpRequestException("offline") : StubHandler.Status(HttpStatusCode.Unauthorized));
        store.Values[TokenService.AccessTokenKey] = Access1;
        store.Values[TokenService.RefreshTokenKey] = Refresh1;

        await Assert.ThrowsAsync<HttpRequestException>(() => session.Client.MeGetProfileAsync(TestContext.Current.CancellationToken));

        Assert.Equal(Refresh1, store.Values[TokenService.RefreshTokenKey]);
    }

    [Fact]
    public async Task Concurrent_401s_share_a_single_refresh()
    {
        // All five requests are held at the server until every one has arrived, so the 401s genuinely overlap.
        var allArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        var stub = new StubHandler(async r =>
        {
            if (r.Path == "/auth/refresh")
                return StubHandler.Json(HttpStatusCode.OK, Payloads.Tokens(Access2, Refresh2));
            if (r.Bearer == Access1)
            {
                if (Interlocked.Increment(ref arrived) == 5)
                    allArrived.SetResult();
                await allArrived.Task;
                return StubHandler.Status(HttpStatusCode.Unauthorized);
            }
            return Profile();
        });
        var store = new FakeSecureStore();
        store.Values[TokenService.AccessTokenKey] = Access1;
        store.Values[TokenService.RefreshTokenKey] = Refresh1;
        var session = ApiClientFactory.Create(StubHandler.BaseAddress, store, stub);

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => session.Client.MeGetProfileAsync(TestContext.Current.CancellationToken)));

        Assert.Equal(5, arrived);
        Assert.Single(stub.To("POST", "/auth/refresh"));
        Assert.Equal(5, stub.Requests.Count(r => r.Bearer == Access2)); // every request was retried with the new token
    }

    [Fact]
    public async Task Sign_out_clears_the_tokens()
    {
        var (session, _, store) = Build(_ => StubHandler.Json(HttpStatusCode.OK, Payloads.Tokens(Access1, Refresh1)));
        await session.Tokens.SignInAsync("a@b.c", "pw", TestContext.Current.CancellationToken);

        await session.Tokens.SignOutAsync();

        Assert.Empty(store.Values);
        Assert.False(await session.Tokens.IsSignedInAsync());
    }

    [Fact]
    public async Task Tokens_never_reach_the_database_a_sync_result_its_failures_or_its_notices()
    {
        var store = new FakeSecureStore();
        store.Values[TokenService.AccessTokenKey] = Access1;
        store.Values[TokenService.RefreshTokenKey] = Refresh1;
        // The engine runs on the real authenticated client, so the bearer really travels with every request.
        await using var rig = await Sync.SyncRig.CreateAsync(handler => ApiClientFactory.Create(StubHandler.BaseAddress, store, handler).Client);
        var rejected = Samples.FullShotRound();
        var broken = Samples.FullShotRound();
        await rig.Temp.Db.Rounds.SaveAsync(rejected);
        await rig.Temp.Db.Rounds.SaveAsync(broken);
        rig.PutRound = r => SyncRigId(r) == rejected.Round.Id
            ? StubHandler.Status(HttpStatusCode.BadRequest)
            : StubHandler.Status(HttpStatusCode.InternalServerError);

        var result = await rig.SyncAsync();

        Assert.All(rig.Server.Requests, r => Assert.Equal(Access1, r.Bearer));
        Assert.NotEmpty(result.Failures);
        Assert.NotEmpty(result.Notices);
        var everything = result + " " + string.Join(' ', result.Failures) + " " + string.Join(' ', result.Notices.Select(n => n.Message));
        foreach (var token in new[] { Access1, Refresh1 })
        {
            Assert.DoesNotContain(token, everything);
            var dbBytes = await File.ReadAllBytesAsync(rig.Temp.Path_, TestContext.Current.CancellationToken);
            Assert.DoesNotContain(token, System.Text.Encoding.UTF8.GetString(dbBytes));
        }
        Assert.Equal(Refresh1, store.Values[TokenService.RefreshTokenKey]); // still only in the secure store
    }

    [Fact]
    public async Task Only_the_credential_endpoints_skip_the_bearer()
    {
        var (session, stub, store) = Build(_ => StubHandler.Status(HttpStatusCode.OK));
        store.Values[TokenService.AccessTokenKey] = Access1;
        store.Values[TokenService.RefreshTokenKey] = Refresh1;
        using var http = new HttpClient(new AuthHandler(session.Tokens) { InnerHandler = stub }) { BaseAddress = StubHandler.BaseAddress };

        await http.GetAsync("/auth/manage/info", TestContext.Current.CancellationToken);
        await http.PostAsync("/auth/login", new StringContent("{}"), TestContext.Current.CancellationToken);

        Assert.Equal(Access1, stub.Requests[0].Bearer);
        Assert.Null(stub.Requests[1].Bearer);
    }

    private static Guid SyncRigId(Recorded r) => Guid.Parse(r.Path.Split('/')[2]);

    private static UpsertRoundRequest ApiMapperRequest() => new()
    {
        TeeId = Samples.TeeId, PlayedOn = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), ClientUpdatedAt = DateTimeOffset.UnixEpoch, Notes = "n",
    };
}
