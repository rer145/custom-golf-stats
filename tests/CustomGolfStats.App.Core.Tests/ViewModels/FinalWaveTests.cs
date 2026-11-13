using System.Net;
using CustomGolfStats.App.Core.ApiAccess;
using CustomGolfStats.App.Core.Tests.Support;
using CustomGolfStats.App.Core.ViewModels;
using Client = CustomGolfStats.Api.Client;

namespace CustomGolfStats.App.Core.Tests.ViewModels;

/// <summary>Whole-app final review: plus handicaps, number text, and a refresh that outlives its session.</summary>
public class FinalWaveTests
{
    // ---- plus handicaps: stored negative, shown and typed with a leading + ----

    [Theory]
    [InlineData(-2.1, "+2.1")]
    [InlineData(0.0, "0.0")]
    [InlineData(12.4, "12.4")]
    [InlineData(null, "-")]
    public void A_handicap_index_below_zero_is_shown_as_a_plus_handicap(double? index, string expected) =>
        Assert.Equal(expected, HandicapText.Format(index));

    [Theory]
    [InlineData("+2.1", -2.1)]
    [InlineData(" +2,1 ", -2.1)]
    [InlineData("2.1", 2.1)]
    [InlineData("12", 12.0)]
    [InlineData("0", 0.0)]
    public void A_leading_plus_means_a_plus_handicap_and_a_bare_number_a_normal_index(string text, double expected)
    {
        Assert.True(HandicapText.TryParse(text, out var value));
        Assert.Equal(expected, value, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-2.1")] // ambiguous: plus handicaps are typed with +
    [InlineData("+")]
    public void Anything_else_is_not_a_handicap(string text) => Assert.False(HandicapText.TryParse(text, out _));

    [Fact]
    public async Task Typing_a_plus_handicap_posts_a_negative_index()
    {
        await using var rig = await VmRig.CreateAsync();
        var stub = new StubHandler(r => r.Path == "/me/handicap"
            ? StubHandler.Json(HttpStatusCode.Created, new Client.HandicapEntryResponse { Id = Guid.NewGuid(), EffectiveOn = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), Index = -2.1 })
            : StubHandler.Status(HttpStatusCode.NotFound));
        var session = ApiClientFactory.Create(StubHandler.BaseAddress, new FakeSecureStore(), stub);
        var vm = new ProfileViewModel(rig.Db, session.Client, session.Tokens, new CustomGolfStats.App.Core.Sync.SyncEngine(rig.Db, session.Client), new FakeConnectivity(), rig.Temp.Clock, rig.Nav);

        vm.NewHandicapText = "+2.1";
        await vm.AddHandicapCommand.ExecuteAsync(null);

        Assert.Contains("-2.1", stub.To("POST", "/me/handicap").Single().Body);
        Assert.Equal(-2.1, vm.LatestHandicapIndex);
        Assert.Equal("", vm.NewHandicapText);
    }

    [Fact]
    public async Task A_handicap_that_cannot_be_read_says_so_and_sends_nothing()
    {
        await using var rig = await VmRig.CreateAsync();
        var stub = new StubHandler(_ => StubHandler.Status(HttpStatusCode.NotFound));
        var session = ApiClientFactory.Create(StubHandler.BaseAddress, new FakeSecureStore(), stub);
        var vm = new ProfileViewModel(rig.Db, session.Client, session.Tokens, new CustomGolfStats.App.Core.Sync.SyncEngine(rig.Db, session.Client), new FakeConnectivity(), rig.Temp.Clock, rig.Nav);

        vm.NewHandicapText = "-2.1";
        await vm.AddHandicapCommand.ExecuteAsync(null);

        Assert.Contains(UiText.HandicapInvalid, vm.HandicapErrors);
        Assert.Empty(stub.Requests);
    }

    // ---- a refresh for an old session must not overwrite a newer session's tokens ----

    [Fact]
    public async Task A_slow_refresh_that_finishes_after_a_new_sign_in_does_not_overwrite_the_new_tokens()
    {
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stub = new StubHandler(async r =>
        {
            if (r.Path == "/auth/refresh")
            {
                refreshStarted.TrySetResult();
                await releaseRefresh.Task;
                return StubHandler.Json(HttpStatusCode.OK, Payloads.Tokens("old-session-access", "old-session-refresh"));
            }
            return StubHandler.Json(HttpStatusCode.OK, Payloads.Tokens("new-access", "new-refresh"));
        });
        var store = new FakeSecureStore();
        store.Values[TokenService.AccessTokenKey] = "old-access";
        store.Values[TokenService.RefreshTokenKey] = "old-refresh";
        var tokens = new TokenService(store, stub.Client());

        var refresh = tokens.RefreshAsync("old-access", TestContext.Current.CancellationToken);
        await refreshStarted.Task;
        await tokens.SignOutAsync();
        await tokens.SignInAsync("b@example.com", "pw", TestContext.Current.CancellationToken);
        releaseRefresh.SetResult();
        await refresh;

        Assert.Equal("new-access", store.Values[TokenService.AccessTokenKey]);
        Assert.Equal("new-refresh", store.Values[TokenService.RefreshTokenKey]);
    }
}
