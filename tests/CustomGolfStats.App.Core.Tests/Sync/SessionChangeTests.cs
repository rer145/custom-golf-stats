using System.Net;
using CustomGolfStats.App.Core.ApiAccess;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Sync;
using CustomGolfStats.App.Core.Tests.Support;
using CustomGolfStats.App.Core.Tests.ViewModels;
using CustomGolfStats.App.Core.ViewModels;
using Client = CustomGolfStats.Api.Client;

namespace CustomGolfStats.App.Core.Tests.Sync;

/// <summary>A sync that is already running when the session changes must not carry one account's data to another.</summary>
public class SessionChangeTests
{
    private sealed record Rig(TempDb Temp, StubHandler Stub, FakeSecureStore Store, SyncEngine Engine, AccountSwitchService Accounts,
        TaskCompletionSource<HttpResponseMessage> FirstPut, TaskCompletionSource FirstPutStarted) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Temp.DisposeAsync();
    }

    /// <summary>Signed in as ada with two dirty rounds; the first PUT blocks until released (and honours cancellation when asked to).</summary>
    private static async Task<Rig> CreateAsync(bool putHonoursCancellation)
    {
        var temp = await TempDb.CreateAsync();
        await temp.Db.Courses.SaveFromServerAsync(Samples.Course());
        await temp.Db.Rounds.SaveAsync(Samples.FullShotRound());
        await temp.Db.Rounds.SaveAsync(Samples.FullShotRound());
        var firstPut = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var puts = 0;
        var stub = new StubHandler(async r =>
        {
            if (r.Method.Method == "PUT" && Interlocked.Increment(ref puts) == 1)
            {
                started.TrySetResult();
                return await firstPut.Task;
            }
            return r.Path == "/sync/changes"
                ? StubHandler.Json(HttpStatusCode.OK, Payloads.Changes(DateTimeOffset.UnixEpoch.AddDays(5), Payloads.Round(Guid.NewGuid(), 1, "bob's round")))
                : StubHandler.Json(HttpStatusCode.OK, Payloads.Round(Guid.NewGuid(), 1));
        });
        var store = new FakeSecureStore();
        store.Values[TokenService.AccessTokenKey] = "ada-access";
        store.Values[TokenService.RefreshTokenKey] = "ada-refresh";
        HttpMessageHandler handler = putHonoursCancellation ? new CancellableHandler(stub) : stub;
        var session = ApiClientFactory.Create(StubHandler.BaseAddress, store, handler);
        var engine = new SyncEngine(temp.Db, session.Client);
        var accounts = new AccountSwitchService(temp.Db, engine);
        await accounts.CheckAsync("ada@example.com");
        return new Rig(temp, stub, store, engine, accounts, firstPut, started);
    }

    /// <summary>Like a real socket handler: a cancelled request stops waiting.</summary>
    private sealed class CancellableHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            await base.SendAsync(request, cancellationToken).WaitAsync(cancellationToken);
    }

    [Fact]
    public async Task A_session_change_during_a_push_stops_the_run_before_any_further_request_or_store()
    {
        await using var rig = await CreateAsync(putHonoursCancellation: false);
        var sync = rig.Engine.SyncAsync(TestContext.Current.CancellationToken);
        await rig.FirstPutStarted.Task;

        // Another sign-in starts while the PUT is in flight: the marker goes down and bob's tokens replace ada's.
        await rig.Temp.Db.SyncState.SetAsync(AccountSwitchService.PendingKey, AccountSwitchService.UnverifiedMarker);
        rig.Store.Values[TokenService.AccessTokenKey] = "bob-access";
        rig.Store.Values[TokenService.RefreshTokenKey] = "bob-refresh";
        rig.FirstPut.SetResult(StubHandler.Json(HttpStatusCode.OK, Payloads.Round(Guid.NewGuid(), 1)));
        var result = await sync;

        Assert.True(result.SignedOut);
        Assert.Single(rig.Stub.Requests);                                     // the second round was never sent
        Assert.DoesNotContain(rig.Stub.Requests, r => r.Bearer == "bob-access");
        Assert.DoesNotContain(rig.Stub.Requests, r => r.Path == "/sync/changes");
        Assert.Null(await rig.Temp.Db.SyncState.GetNextSinceAsync());          // nothing pulled
    }

    [Fact]
    public async Task Beginning_a_sign_in_cancels_and_waits_for_a_running_sync()
    {
        await using var rig = await CreateAsync(putHonoursCancellation: true);
        var sync = rig.Engine.SyncAsync(TestContext.Current.CancellationToken);
        await rig.FirstPutStarted.Task;

        await rig.Accounts.BeginSignInAsync();

        Assert.True(sync.IsCompleted);                                         // stopped before any token can change
        Assert.True((await sync).SignedOut);
        Assert.Single(rig.Stub.Requests);
        Assert.Equal(2, await rig.Temp.Db.Rounds.CountDirtyAsync());           // unsent work stays dirty
    }

    [Fact]
    public async Task Signing_out_from_the_profile_stops_a_running_sync_first()
    {
        await using var rig = await CreateAsync(putHonoursCancellation: true);
        var nav = new FakeNavigator();
        var vm = new ProfileViewModel(rig.Temp.Db, rig.Stub.Client(), new TokenService(rig.Store, rig.Stub.Client()), rig.Engine,
            new FakeConnectivity(), rig.Temp.Clock, nav);
        var sync = rig.Engine.SyncAsync(TestContext.Current.CancellationToken);
        await rig.FirstPutStarted.Task;

        await vm.SignOutCommand.ExecuteAsync(null);

        Assert.True(sync.IsCompleted);
        Assert.Single(rig.Stub.Requests);
        Assert.Empty(rig.Store.Values);
        Assert.Equal(Route.SignIn, nav.Last.Route);
    }

    // ---- stored tokens without a verified account (iOS keeps the Keychain across a reinstall; the database does not survive) ----

    [Fact]
    public async Task Stored_tokens_without_a_verified_account_are_verified_on_launch()
    {
        await using var t = await TempDb.CreateAsync();
        var stub = new StubHandler(r => StubHandler.Json(HttpStatusCode.OK,
            new Client.ProfileResponse { Id = Guid.NewGuid(), Email = "Ada@Example.com", FirstName = "A", LastName = "B", Gender = "Male", Units = "Imperial" }));
        var store = new FakeSecureStore();
        store.Values[TokenService.AccessTokenKey] = "a";
        store.Values[TokenService.RefreshTokenKey] = "r";
        var session = ApiClientFactory.Create(StubHandler.BaseAddress, store, stub);
        var nav = new FakeNavigator();
        var vm = new SignInViewModel(session.Tokens, new AccountSwitchService(t.Db), session.Client, nav);

        await vm.InitializeAsync();

        Assert.Equal(Route.Home, nav.Last.Route);
        Assert.True(await AccountSwitchService.IsSyncAllowedAsync(t.Db));
    }

    [Fact]
    public async Task Stored_tokens_whose_account_cannot_be_confirmed_on_launch_are_signed_out()
    {
        await using var t = await TempDb.CreateAsync();
        var stub = new StubHandler(_ => StubHandler.Status(HttpStatusCode.InternalServerError));
        var store = new FakeSecureStore();
        store.Values[TokenService.AccessTokenKey] = "a";
        store.Values[TokenService.RefreshTokenKey] = "r";
        var session = ApiClientFactory.Create(StubHandler.BaseAddress, store, stub);
        var nav = new FakeNavigator();
        var vm = new SignInViewModel(session.Tokens, new AccountSwitchService(t.Db), session.Client, nav);

        await vm.InitializeAsync();

        Assert.Empty(nav.Visits);
        Assert.Empty(store.Values);
        Assert.False(await AccountSwitchService.IsSyncAllowedAsync(t.Db));
    }
}
